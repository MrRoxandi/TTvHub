using Lua;
using System.Collections.Concurrent;
using System.Threading.Channels;
using TTvHub.Core.BackEnds;
using TTvHub.Core.BackEnds.Abstractions;
using TTvHub.Core.Items;
using TTvHub.Core.Logs;
using TTvHub.Core.Managers;
using TTvHub.Core.Services.Interfaces;
using TTvHub.Core.Services.Modules;
using TTvHub.Core.Twitch;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix;
using TwitchLib.Client.Events;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Services.Controllers;

public sealed class TwitchController : IPointsService, IAsyncDisposable
{
    
    private string? _profilePictureUrl;
    public string ProfilePictureUrl => _profilePictureUrl ?? string.Empty;

    // --- Modules ---
    public TwitchAuthModule Auth { get; private set; }
    private TwitchApi Api => Auth._api;
    private TwitchEventSubModule? _eventClient;
    private TwitchChatModule? _chatClient;

    // --- Managers ---
    private readonly LuaStartUpManager _configManager;
    private readonly PointsManager _db = new("Twitch");
    // --- Inner logic --- 
    private readonly Channel<(TwitchEvent Event, TwitchEventArgs Args)> _eventChannel = Channel.CreateUnbounded<(TwitchEvent, TwitchEventArgs)>();
    private ConcurrentDictionary<(string, TwitchTools.TwitchEventKind), TwitchEvent> _events = [];
    private readonly LuaState _luaState = LuaState.Create();
    private CancellationTokenSource _serviceCts = new();
    // --- Constants --- 
    private const string LastClipCheckTimeKey = "last_clip_check_time_utc";
    private const int EventActionShutdownWaitSeconds = 3;
    private const int WorkerTaskShutdownWaitSeconds = 3;
    private const int ViewerPointsIntervalMinutes = 1;
    private const int PointsPerMinuteForViewers = 0;
    private const int ClipCheckIntervalMinutes = 15;
    private const int WorkerQueuePollDelayMs = 100;
    private const int ReconnectDelaySeconds = 5;
    private const int MaxConcurrentEvents = 5;
    private const int PointsPerMessage = 2;
    private const int PointsPerClip = 10;
    public string ServiceStatusMessage { get; private set; } = "Ready";
    public bool IsChatConnected => _chatClient?.IsConnected ?? false;
    private bool ChatDisconnectRequested { get; set; } = false;
    public bool IsEventSubConnected => _eventClient?.IsConnected ?? false;
    private bool EventSubStopRequested { get; set; } = false;
    public bool IsAuthenticated => Auth.IsAuthenticated;

    public event Action? StateChanged;
    public TwitchController(LuaStartUpManager configManager)
    {
        _configManager = configManager;
        Auth = new();
    }

    public async Task InitializeAsync()
    {
        UpdateState("Initializing...");
        await Auth.InitializeAsync();
        if (!IsAuthenticated)
        {
            UpdateState("Authentication required. Please log in.");
            return; 
        }
        _ = GetProfilePictureUrlFromApi();
        _ = ProcessEventsQueueAsync(_serviceCts.Token);
        await ReloadEventsAsync();
        StartClipTimer();
        UpdateState("Ready. Connect modules to start.");
    }


    #region Connection and Disconnection

    public async Task ConnectChatAsync()
    {
        if (IsChatConnected) return;
        _chatClient = new();
        _chatClient.OnConnected += ChatOnConnectedHandler;
        _chatClient.OnDisconnected += ChatOnDisconnectedHandler;
        _chatClient.OnChatCommandReceived += ChatOnChatCommandReceivedHandler;
        _chatClient.OnMessageReceived += ChatOnMessageReceivedHandler;
        _chatClient.Connect(Auth.CurrentUser!.Login, Auth.CurrentUser!.AccessToken);
        ChatDisconnectRequested = false;
    }

    public async Task DisconnectChatAsync()
    {
        ChatDisconnectRequested = true;
        if (!IsChatConnected) return;
        if (_chatClient == null) return;
        _chatClient.OnConnected -= ChatOnConnectedHandler;
        _chatClient.OnDisconnected -= ChatOnDisconnectedHandler;
        _chatClient.OnChatCommandReceived -= ChatOnChatCommandReceivedHandler;
        _chatClient.OnMessageReceived -= ChatOnMessageReceivedHandler;
        _chatClient.Disconnect();
        _chatClient = null;
    }

    private void ChatOnMessageReceivedHandler(object? sender, OnMessageReceivedArgs e)
    {
        var chatMessage = e.ChatMessage;
        if (chatMessage.Message.Length < 10 || chatMessage.Username == Auth.CurrentUser!.Login) return;
        Logger.Log(LogCategory.Info, $"Awarding {chatMessage.Username} with {PointsPerMessage} for message.", this);
        _ = AddPointsAsync(chatMessage.DisplayName, PointsPerMessage);
    }

    private void ChatOnChatCommandReceivedHandler(object? sender, OnChatCommandReceivedArgs e)
    {
        if (_events.TryGetValue((e.Command.CommandText, TwitchTools.TwitchEventKind.Command), out var twitchEvent))
        {
            var chatCommand = e.Command;
            var senderUsername = chatCommand.ChatMessage.Username;
            var cmdArgStr = chatCommand.ArgumentsAsString.Replace("\U000e0000", "").Trim();
            var cmdArgs = string.IsNullOrEmpty(cmdArgStr) ? null : cmdArgStr.Split(' ');

            var userLevel = TwitchTools.ParseFromTwitchLib(
                chatCommand.ChatMessage.UserType,
                chatCommand.ChatMessage.IsSubscriber,
                chatCommand.ChatMessage.IsVip);

            var eventArgs = new TwitchEventArgs
            {
                Sender = senderUsername,
                Args = cmdArgs,
                Permission = userLevel,
                State = _luaState
            };
            _eventChannel.Writer.TryWrite((twitchEvent, eventArgs));
        }
    }

    private void ChatOnDisconnectedHandler()
    {
        UpdateState("Chat Disconnected");
        if (ChatDisconnectRequested) return;
        // TODO: Better reconnection
        Task.Delay(ReconnectDelaySeconds).ContinueWith(async _ =>
        {
            await DisconnectChatAsync();
            await ConnectChatAsync();
        });
    }

    private void ChatOnConnectedHandler()
    {
        UpdateState("Chat Connected");
    }

    public async Task ConnectEventSubAsync()
    {
        if (IsEventSubConnected) return;
        _eventClient = new();
        _eventClient.ErrorOccurred += EventErrorOccurredHandler;
        _eventClient.WebsocketConnected += EventWebsocketConnectedHandler;
        _eventClient.WebsocketDisconnected += EventWebsocketDisconnectedHandler;
        _eventClient.ChannelPointsCustomRewardRedemptionAdd += EventChannelPointsCustomRewardRedemptionAddHandler;
        await _eventClient.ConnectAsync();
        EventSubStopRequested = false;
    }

    public async Task DisconnectEventSubAsync()
    {
        if (!IsEventSubConnected) return;
        if (_eventClient == null) return;
        _eventClient.ErrorOccurred -= EventErrorOccurredHandler;
        _eventClient.WebsocketConnected -= EventWebsocketConnectedHandler;
        _eventClient.WebsocketDisconnected -= EventWebsocketDisconnectedHandler;
        _eventClient.ChannelPointsCustomRewardRedemptionAdd -= EventChannelPointsCustomRewardRedemptionAddHandler;
        await _eventClient.DisconnectAsync();
        _eventClient = null;
        EventSubStopRequested = true;
    }

    private Task EventErrorOccurredHandler(object sender, ErrorOccuredArgs args)
    {
        Logger.Log(LogCategory.Error, $"EventSub client error: {args.Message}", this, args.Exception);
        return Task.CompletedTask;
    }

    private Task EventWebsocketDisconnectedHandler(object sender, EventArgs args)
    {
        Logger.Log(LogCategory.Warning, "EventSub WebSocket disconnected.", this);
        if (EventSubStopRequested) return Task.CompletedTask;
        // TODO: Better reconnection
        Task.Delay(ReconnectDelaySeconds).ContinueWith(async _ =>
        {
            await ConnectEventSubAsync();
        });
        return Task.CompletedTask;
    }

    private Task EventChannelPointsCustomRewardRedemptionAddHandler(object sender, ChannelPointsCustomRewardRedemptionArgs e)
    {
        var redemptionEvent = e.Notification.Payload.Event;
        if (_events.TryGetValue((redemptionEvent.Reward.Title, TwitchTools.TwitchEventKind.Reward), out var twitchEvent))
        {
            var senderUsername = redemptionEvent.UserName;
            var rewardArgsStr = redemptionEvent.UserInput.Trim();
            var rewardArgs = string.IsNullOrEmpty(rewardArgsStr) ? null : rewardArgsStr.Split(' ');

            var eventArgs = new TwitchEventArgs
            {
                Sender = senderUsername,
                Args = rewardArgs,
                Permission = TwitchTools.PermissionLevel.Viewer
            };

            _eventChannel.Writer.TryWrite((twitchEvent, eventArgs));
        }
        return Task.CompletedTask;
    }

    private async Task EventWebsocketConnectedHandler(object sender, WebsocketConnectedArgs args)
    {
        UpdateState("EventSub Connected");
        Logger.Log(LogCategory.Info, "EventSub client connected.", this);
        await RegisterEventSubTopicsAsync();
    }

    private async Task<bool> RegisterEventSubTopicsAsync()
    {
        if (_eventClient == null || string.IsNullOrEmpty(_eventClient.SessionId))
        {
            Logger.Log(LogCategory.Error, "Cannot register EventSub topics: Client is not connected or Session ID is missing.", this);
            return false;
        }

        if (Auth.CurrentUser == null || string.IsNullOrEmpty(Auth.CurrentUser.TwitchUserId))
        {
            Logger.Log(LogCategory.Error, "Cannot register EventSub topics: Broadcaster User ID is missing.", this);
            return false;
        }

        var allSuccess = true;

        var condition = new Dictionary<string, string> { { "broadcaster_user_id", Auth.CurrentUser.TwitchUserId } };

        var rewardResult =
            await SubscribeToEventAsync("channel.channel_points_custom_reward_redemption.add", "1",
                condition); // channel points reward
        if (!rewardResult) allSuccess = false;

        return allSuccess;
    }
    private async Task<bool> SubscribeToEventAsync(string type, string version, Dictionary<string, string> condition)
    {
        if (_eventClient == null || string.IsNullOrEmpty(_eventClient.SessionId) ||
            Api.InnerApi.Helix.EventSub == null) return false;
        try
        {
            Logger.Log(LogCategory.Info, $"Attempting to subscribe to [{type}:{version}]", this);
            var response = await Api.InnerApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                type, version, condition,
                EventSubTransportMethod.Websocket, _eventClient.SessionId
            );
            if (response?.Subscriptions == null || response.Subscriptions.Length == 0)
            {
                Logger.Log(LogCategory.Error, 
                    $"EventSub subscription request for [{type}:{version}] failed or returned empty data. Check scopes and broadcaster ID. Cost: {response?.TotalCost}, MaxCost: {response?.MaxTotalCost}", this);
                return false;
            }

            var subscriptionSuccessful = true;
            foreach (var sub in response.Subscriptions)
            {
                Logger.Log(LogCategory.Info,
                    $"EventSub subscription to [{sub.Type} v{sub.Version}] Status: {sub.Status}. Cost: {sub.Cost}. ID: {sub.Id}", this);
                if (sub.Status is "enabled" or "webhook_callback_verification_pending")
                    continue; // "enabled" for websocket
                Logger.Log(LogCategory.Warning,
                    $"EventSub subscription for [{sub.Type}] has status: {sub.Status}. Expected 'enabled'.", this);

                if (sub.Status.Contains("fail") || sub.Status.Contains("revoked") || sub.Status.Contains("error"))
                    subscriptionSuccessful = false;
            }

            if (!subscriptionSuccessful)
                Logger.Log(LogCategory.Error,
                    "One or more EventSub subscriptions for did not report 'enabled' status.", this);
            return subscriptionSuccessful;
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error,
                $"Failed to subscribe to EventSub topic [{type}:{version}] due to an API error.", this, ex);
            return false;
        }
    }
    #endregion

    #region Start and Stop

    #endregion

    public void SendChatMessage(string message)
    {
        if (!IsChatConnected)
        {
            Logger.Log(LogCategory.Warning, "Cannot send message: Chat client is not connected.", this);
            return;
        }
        _chatClient?.SendMessage(Auth.CurrentUser!.Login, message);
    }

    public void SendWhisper(string target, string message)
    {
        if (!IsChatConnected)
        {
            _chatClient?.SendWhisper(target, message);
            return;
        }

        Logger.Log(LogCategory.Warning, "Cannot send whisper: Twitch Chat client is not connected.", this);
    }

    public long? GetEventCost(string eventName)
    {
        if (_events == null) return null;
        var result = _events.TryGetValue((eventName, TwitchTools.TwitchEventKind.Command), out var tEvent);
        if (!result || tEvent is null) return null;
        return tEvent.Cost;
    }

    public async Task ReloadEventsAsync() => _events = await _configManager.LoadTwitchEventsAsync();
    //public async Task ReloadEventsAsync() => throw new NotImplementedException("Will be implemented later");

    private async Task ProcessEventsQueueAsync(CancellationToken token)
    {
        await foreach (var (twitchEvent, eventArgs) in _eventChannel.Reader.ReadAllAsync(token))
        {
            try
            {
                if (await CanExecuteEvent(twitchEvent, eventArgs))
                {
                    Logger.Log(LogCategory.Info, $"Executing event '{twitchEvent.Name}' for {eventArgs.Sender}.", this);
                    twitchEvent.Execute(eventArgs);

                    if (twitchEvent.Cost > 0)
                    {
                        await AddPointsAsync(eventArgs.Sender, -twitchEvent.Cost);
                        Logger.Log(LogCategory.Info, $"Charged {twitchEvent.Cost} points from {eventArgs.Sender} for event '{twitchEvent.Name}'.", this);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogCategory.Error, $"Failed to process event '{twitchEvent.Name}'.", this, ex);
            }
        }
    }

    private async Task<bool> CanExecuteEvent(TwitchEvent twitchEvent, TwitchEventArgs args)
    {
        if (args.Permission < twitchEvent.PermissionLevel)
        {
            Logger.Log(LogCategory.Info, $"User {args.Sender} lacks permission for event '{twitchEvent.Name}'.", this);
            return false;
        }

        if (twitchEvent.Cost > 0)
        {
            var userPoints = await GetPointsAsync(args.Sender);
            if (userPoints < twitchEvent.Cost)
            {
                SendChatMessage($"@{args.Sender}, you need {twitchEvent.Cost} points for !{twitchEvent.Name}, but you only have {userPoints}.");
                return false;
            }
        }

        if (!twitchEvent.Executable)
        {
            Logger.Log(LogCategory.Info, $"Event '{twitchEvent.Name}' is on cooldown.", this);
            return false;
        }

        return true;
    }

    private Timer? _clipPointsTimer;

    

    private void StartClipTimer()
    {
        if (_clipPointsTimer == null && !string.IsNullOrEmpty(Auth.CurrentUser!.TwitchUserId))
        {
            _clipPointsTimer = new Timer(CheckForNewClipsAndAwardPoints, null, TimeSpan.FromSeconds(20),
                TimeSpan.FromMinutes(ClipCheckIntervalMinutes));
            Logger.Log(LogCategory.Info,
                $"Clip points timer started. Interval: {ClipCheckIntervalMinutes} min.", this);
        }
        else if (string.IsNullOrEmpty(Auth.CurrentUser!.TwitchUserId))
        {
            Logger.Log(LogCategory.Warning, 
                "Cannot start Clip points timer: TwitchApi or Broadcaster ID is not configured.", this);
        }
    }
    private void StopClipTimer()
    {
        _clipPointsTimer?.Dispose();
        _clipPointsTimer = null;
        Logger.Log(LogCategory.Info, "Clip points timer stopped.", this);
    }

    private async void CheckForNewClipsAndAwardPoints(object? state)
    {
        if (!IsAuthenticated)
        {
            Logger.Log(LogCategory.Info, "Unable check for clips. Twitch is not Authenticated.", this);
            return;
        }
        
        Logger.Log(LogCategory.Info, "Checking for new clips...", this);
        var cursor = string.Empty;
        var lastCheckTimeUtc = await Container.GetValueAsync<DateTime?>(LastClipCheckTimeKey)
                               ?? DateTime.Today.AddYears(-3);
        do
        {
            try
            {
                var result = await Api.InnerApi.Helix.Clips.GetClipsAsync(
                    broadcasterId: Auth.CurrentUser!.TwitchUserId,
                    accessToken: Auth.CurrentUser!.AccessToken,
                    startedAt: lastCheckTimeUtc,
                    endedAt: DateTime.Today,
                    after: string.IsNullOrEmpty(cursor) ? null : cursor,
                    first: 100
                );

                if (result == null)
                {
                    Logger.Log(LogCategory.Error, "Api error, get empty response.", this);
                    break;
                }

                if (result.Clips is { Length: > 0 } clips)
                {
                    Logger.Log(LogCategory.Info, $"Processing points reward for {clips.Length} clips...", this);
                    foreach (var clip in clips.OrderByDescending(t => t.CreatedAt))
                    {
                        // Checking name
                        if (string.IsNullOrWhiteSpace(clip.CreatorName)) continue;
                        if (string.Equals(clip.CreatorName, Auth.CurrentUser!.Login,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Log(LogCategory.Info, 
                                $"Clip {clip.Id} by {clip.CreatorName} (bot/channel) skipped for points.", this);
                            continue;
                        }

                        Logger.Log(LogCategory.Info,
                            $"adding {PointsPerClip} point to {clip.CreatorName} for creating clip ({clip.Id[..8]})", this);
                        await AddPointsByIdAsync(clip.CreatorId, PointsPerClip);
                    }
                }
                else
                {
                    Logger.Log(LogCategory.Info, "No new clips found since last check.", this);
                }

                //
                cursor = result.Pagination.Cursor;
            }
            catch (Exception apiEx)
            {
                Logger.Log(LogCategory.Error, "Failed to get clips from Twitch API:", this, apiEx);
                return;
            }
        } while (!string.IsNullOrEmpty(cursor));

        await Container.InsertValueAsync(LastClipCheckTimeKey, DateTime.UtcNow);
    }

    public async Task<bool> AddPointsAsync(string username, long points) => await ModifyUserPoints(username, points);

    public async Task<bool> SetPointsAsync(string username, long points) =>
        await ModifyUserPoints(username, points, false);


    public async Task<long> GetPointsAsync(string username) => await _db.GetUserPointsAsync(username);

    public async Task<IEnumerable<KeyValuePair<string, long>>> GetAllUsersPointsAsync()
    {
        var r = await _db.GetAllUsersPointsAsync();
        return [..r];
    }

    public async Task<string?> GetUserIdByNameAsync(string username)
    {
        var user = await _db.GetUserAsync(username);
        return user?.UserId ?? null;
    }

    public async Task<string?> GetUserNameByIdAsync(string userId)
    {
        var user = await _db.GetUserByIdAsync(userId);
        return user?.Username ?? null;
    }

    public async Task<bool> AddPointsByIdAsync(string userId, long points) => await ModifyUserPointsById(userId, points);

    public async Task<bool> SetPointsByIdAsync(string userId, long points) =>
        await ModifyUserPointsById(userId, points, false);

    public async Task<long> GetPointsByIdAsync(string userId) => await _db.GetUserPointsByIdAsync(userId);


    private async Task<bool> ModifyUserPoints(string username, long points, bool isAdding = true)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var userId = await GetUserIdFromApi(username);
        if (userId is null)
        {
            Logger.Log(LogCategory.Warning, $"Cannot modify points for '{username}': User ID not found.", this);
            return false;
        }

        var userExists = await _db.ContainsIdAsync(userId);
        if (userExists)
            return isAdding
                ? await _db.AddUserPointsByIdAsync(userId, points)
                : await _db.SetUserPointsByIdAsync(userId, points);
        await _db.CreateUserAsync(username, userId);
        return isAdding ?
            await _db.AddUserPointsByIdAsync(userId, points) :
            await _db.SetUserPointsByIdAsync(userId, points);
    }

    private async Task<bool> ModifyUserPointsById(string userId, long points, bool isAdding = true)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var userExists = await _db.ContainsIdAsync(userId);
        if (userExists)
            return isAdding
                ? await _db.AddUserPointsByIdAsync(userId, points)
                : await _db.SetUserPointsByIdAsync(userId, points);
        var username = await GetUserLoginFromApi(userId);
        if (username is null)
        {
            Logger.Log(LogCategory.Warning, $"Cannot modify points for 'id: {userId}': Username not found.", this);
            return false;
        }
        await _db.CreateUserAsync(username, userId);

        return isAdding ?
            await _db.AddUserPointsByIdAsync(userId, points) :
            await _db.SetUserPointsByIdAsync(userId, points);
    }

    private async Task<string?> GetUserIdFromApi(string username)
    {
        var id = await Api.InnerApi.Helix.Users.GetUsersAsync(logins: [username]);
        return id?.Users.First().Id;
    }

    private async Task<string?> GetUserLoginFromApi(string id)
    {
        var users = await Api.InnerApi.Helix.Users.GetUsersAsync([id]);
        return users?.Users.First().Login;
    }

    public async Task<string> GetProfilePictureUrlFromApi()
    {
        if (!string.IsNullOrEmpty(_profilePictureUrl))
        {
            return _profilePictureUrl;
        }
        if (Auth.CurrentUser?.TwitchUserId == null)
        {
            return string.Empty;
        }

        try
        {
            var users = await Api.InnerApi.Helix.Users.GetUsersAsync(ids: [Auth.CurrentUser.TwitchUserId]);
            var url = users?.Users.FirstOrDefault()?.ProfileImageUrl ?? string.Empty;

            _profilePictureUrl = url;

            StateChanged?.Invoke();

            return url;
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Failed to get profile picture from API", this, ex);
            return string.Empty;
        }
    }

    private void UpdateState(string message)
    {
        ServiceStatusMessage = message;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_chatClient != null) await _chatClient.DisposeAsync();
        if (_eventClient != null) await _eventClient.DisposeAsync();
    }
}
