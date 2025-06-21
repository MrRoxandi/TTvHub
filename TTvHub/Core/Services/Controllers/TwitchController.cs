using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using Logger = TTvHub.Core.Logs.StaticLogger;
using TTvHub.Core.BackEnds.Abstractions;
using System.Collections.Concurrent;
using TTvHub.Core.Services.Modules;
using System.Threading.Channels;
using TwitchLib.Client.Events;
using TTvHub.Core.Managers;
using TTvHub.Core.Twitch;
using TTvHub.Core.Items;
using TTvHub.Core.Logs;
using Lua;

namespace TTvHub.Core.Services.Controllers;

public sealed class TwitchController
{
    public string ProfilePictureUrl { get; private set; } = string.Empty;

    // --- Modules ---
    public readonly TwitchAuthModule Auth;
    private TwitchApi Api => Auth._api;
    private readonly TwitchEventSubModule _eventClient;
    private readonly TwitchChatModule _chatClient;
    private readonly TwitchPointsModule Db;

    // --- Managers ---
    private readonly LuaStartUpManager _configManager;
    // --- Inner logic --- 
    private readonly Channel<(TwitchEvent Event, TwitchEventArgs Args)> _eventChannel = Channel.CreateUnbounded<(TwitchEvent, TwitchEventArgs)>();
    private ConcurrentDictionary<(string, TwitchTools.TwitchEventKind), TwitchEvent> _events = [];
    private readonly LuaState _luaState = LuaState.Create();
    private CancellationTokenSource _serviceCts = new();
    // --- Constants --- 
    private const string LastClipCheckTimeKey = "last_clip_check_time_utc";
    private const int ClipCheckIntervalMinutes = 15;
    private const int PointsPerMessage = 2;
    private const int PointsPerClip = 10;
    public string ServiceStatusMessage { get; private set; } = "Ready";
    public bool IsChatConnected => _chatClient?.IsConnected ?? false;
    public bool IsEventSubConnected => _eventClient?.IsConnected ?? false;
    public bool IsAuthenticated => Auth.IsAuthenticated;

    //public event Action? StateChanged;
    public TwitchController(LuaStartUpManager configManager)
    {
        Auth = new();
        _configManager = configManager;
        Db = new TwitchPointsModule(Auth._api);
        _chatClient = new();
        _chatClient.OnDisconnected += ChatOnDisconnectedHandler;
        _chatClient.OnChatCommandReceived += ChatOnChatCommandReceivedHandler;
        _chatClient.OnMessageReceived += ChatOnMessageReceivedHandler;

        _eventClient = new(Auth);
        _eventClient.WebsocketDisconnected += EventWebsocketDisconnectedHandler;
        _eventClient.ChannelPointsCustomRewardRedemptionAdd += EventChannelPointsCustomRewardRedemptionAddHandler;

    }

    public async Task InitializeAsync()
    {
        await Auth.InitializeAsync();
        if (!IsAuthenticated)
        {
            return; 
        }
        await RequestProfilePicture();
        _ = ProcessEventsQueueAsync(_serviceCts.Token);
        await ReloadEventsAsync();
    }


    #region Connection and Disconnection

    // --- Chat methods block ---
    public async Task<bool> ConnectChatAsync()
    {
        if (IsChatConnected) return await Task.FromResult(false);
        
        return await _chatClient.ConnectAsync(Auth.CurrentUser!.Login, Auth.CurrentUser!.AccessToken);
    }
    public async Task<bool> DisconnectChatAsync()
    {
        if (!IsChatConnected) return await Task.FromResult(false); ;
        return await _chatClient.DisconnectAsync();
    }

    private void ChatOnMessageReceivedHandler(object? sender, OnMessageReceivedArgs e)
    {
        var chatMessage = e.ChatMessage;
        if (chatMessage.Message.Length < 10 || chatMessage.Username == Auth.CurrentUser!.Login) return;
        Logger.Log(LogCategory.Info, $"Awarding {chatMessage.Username} with {PointsPerMessage} for message.", this);
        _ = Db.AddPointsByIdAsync(chatMessage.UserId, PointsPerMessage);
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
                UserId = chatCommand.ChatMessage.UserId,
                Sender = senderUsername,
                Permission = userLevel,
                State = _luaState,
                Args = cmdArgs
            };
            _eventChannel.Writer.TryWrite((twitchEvent, eventArgs));
        }
    }

    private void ChatOnDisconnectedHandler()
    {
        if (_chatClient.DisconnectRequsted)
        {
            Logger.Log(LogCategory.Info, "Chat module is disconnected", this);
            return;
        }
        Logger.Log(LogCategory.Warning, "Chat module is disconnected unexpectedly. Reconnecting in 5 seconds", this);
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (_chatClient.DisconnectRequsted)
                {
                    Logger.Log(LogCategory.Info, "Reconnect cancelled (stop requested)..", this);
                    return;
                }
                await _chatClient.DisconnectAsync();
                await _chatClient.ConnectAsync(Auth.CurrentUser!.Login, Auth.CurrentUser.AccessToken);
            }
            catch (Exception ex)
            {
                Logger.Log(LogCategory.Error, "An error occurred during reconnection.", this, ex);
                await _chatClient.DisconnectAsync();
            }
        });
    }

    // --- EventSub methods block ---
    public async Task ConnectEventSubAsync()
    {
        if (IsEventSubConnected) return;
        await _eventClient.ConnectAsync();
    }
    public async Task DisconnectEventSubAsync()
    {
        if (!IsEventSubConnected) return;
        if (_eventClient == null) return;
        await _eventClient.DisconnectAsync();
    }

    private Task EventWebsocketDisconnectedHandler(object sender, EventArgs args)
    {
        if (_eventClient.DisconnectRequsted)
        {
            Logger.Log(LogCategory.Info, "EventSub module is disconnected", this);
            return Task.CompletedTask;
        }
        Logger.Log(LogCategory.Warning, "EventSub module is disconnected unexpectedly. Reconnecting in 5 seconds", this);
        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (_eventClient.DisconnectRequsted)
                {
                    Logger.Log(LogCategory.Info, "Reconnect cancelled (stop requested)..", this);
                    return;
                }
                await _eventClient.DisconnectAsync();
                await _eventClient.ConnectAsync();
            }
            catch (Exception ex)
            {
                Logger.Log(LogCategory.Error, "An error occurred during reconnection.", this, ex);
                await _eventClient.DisconnectAsync();
            }
        });
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
                Permission = TwitchTools.PermissionLevel.Viewer,
                UserId = redemptionEvent.UserId,
                Sender = senderUsername,
                Args = rewardArgs,
                State = _luaState,
            };

            _eventChannel.Writer.TryWrite((twitchEvent, eventArgs));
        }
        return Task.CompletedTask;
    }

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

    public long GetEventCost(string eventName)
    {
        if (_events == null) return 0;
        var result = _events.TryGetValue((eventName, TwitchTools.TwitchEventKind.Command), out var tEvent);
        if (!result || tEvent is null) return 0;
        return tEvent.Cost;
    }

    public async Task ReloadEventsAsync() => _events = await _configManager.LoadTwitchEventsAsync();
    
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
                        await Db.AddPointsByIdAsync(eventArgs.UserId, -twitchEvent.Cost);
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
            var userPoints = await Db.GetPointsByIdAsync(args.UserId);
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

    
    public async Task<bool> StartClipTimerAsync()
    {
        if (!IsAuthenticated)
        {
            Logger.Log(LogCategory.Warning,
                "Cannot start Clip points timer: TwitchApi or Broadcaster ID is not configured.", this);
            return await Task.FromResult(false);
        }
        _clipPointsTimer?.Dispose();
        _clipPointsTimer = new Timer(CheckForNewClipsAndAwardPoints, null, TimeSpan.FromSeconds(20),
                TimeSpan.FromMinutes(ClipCheckIntervalMinutes));
        Logger.Log(LogCategory.Info,
            $"Clip points timer started. Interval: {ClipCheckIntervalMinutes} min.", this);
        return await Task.FromResult(true);
    }
    public async Task<bool> StopClipTimerAsync()
    {
        if (_clipPointsTimer == null)
        {
            Logger.Log(LogCategory.Warning, "Unable to stop Clips timer. Is't not running...", this);
            return await Task.FromResult(false);
        }
        _clipPointsTimer?.Dispose();
        _clipPointsTimer = null;
        Logger.Log(LogCategory.Info, "Clip points timer stopped.", this);
        return await Task.FromResult(true);
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
                        await Db.AddPointsByIdAsync(clip.CreatorId, PointsPerClip);
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

    public async Task<bool> AddPointsAsync(string username, long points) => await Db.AddPointsAsync(username, points);
    public async Task<bool> SetPointsAsync(string username, long points) => await Db.SetPointsAsync(username, points);
    public async Task<long> GetPointsAsync(string username) => await Db.GetPointsAsync(username);
    public async Task<IEnumerable<KeyValuePair<string, long>>> GetAllUsersPointsAsync() => await Db.GetAllPointsAsync();

    public async Task RequestProfilePicture()
    {
        if (!string.IsNullOrEmpty(ProfilePictureUrl))
        {
            return;
        }
        if (Auth.CurrentUser?.TwitchUserId == null)
        {
            return;
        }

        try
        {
            var users = await Api.InnerApi.Helix.Users.GetUsersAsync(ids: [Auth.CurrentUser.TwitchUserId]);
            if (users == null)
            {
                return;
            }
            ProfilePictureUrl = users.Users.First().ProfileImageUrl;
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Failed to get profile picture from API", this, ex);
            ProfilePictureUrl = string.Empty;
        }
    }

}
