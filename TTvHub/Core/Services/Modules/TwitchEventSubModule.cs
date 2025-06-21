using Microsoft.Extensions.Logging;
using TTvHub.Core.Logs;
using TTvHub.Core.Twitch;
using TTvHub.WinUI;
using TwitchLib.Api.Auth;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Core;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using Windows.Foundation;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Services.Modules
{
    public sealed class TwitchEventSubModule(TwitchAuthModule a)
    {
        private EventSubWebsocketClient? InnerClient;
        private readonly TwitchAuthModule Auth = a;
        private TwitchApi Api => Auth._api;
        public bool IsConnected { get; private set; } = false;
        public string SessionId => InnerClient?.SessionId ?? string.Empty;
        public bool DisconnectRequsted { get; private set; } = false;

        public event AsyncEventHandler<ChannelPointsCustomRewardRedemptionArgs>? ChannelPointsCustomRewardRedemptionAdd;
        public event AsyncEventHandler? WebsocketDisconnected;
        public async Task<bool> ConnectAsync()
        {
            if (IsConnected)
            {
                Logger.Log(LogCategory.Warning, "Twitch EventSub module is already connected.", this);
                return await Task.FromResult(false);
            }
            InnerClient = new();
            InnerClient.ChannelPointsCustomRewardRedemptionAdd += (s,e) => ChannelPointsCustomRewardRedemptionAdd!.Invoke(s, e);
            InnerClient.ErrorOccurred += ErrorOccurredHandler;
            InnerClient.WebsocketDisconnected += OnWebsocketDisconnected;
            InnerClient.WebsocketConnected += OnWebsocketConnected;
            var result = await InnerClient.ConnectAsync();
            IsConnected = result;
            DisconnectRequsted = false;
            return result;
        }

        private Task ErrorOccurredHandler(object sender, ErrorOccuredArgs args)
        {
            Logger.Log(LogCategory.Error, $"EventSub client error: {args.Message}", this, args.Exception);
            return Task.CompletedTask;
        }

        public async Task<bool> DisconnectAsync()
        {
            if (InnerClient == null)
            {
                IsConnected = false;
                Logger.Log(LogCategory.Warning, "Twitch EventSub module is already disconnected.", this);
                return await Task.FromResult(false);
            }
            DisconnectRequsted = true;
            await InnerClient.DisconnectAsync();
            IsConnected = false;
            await CleanupEventSubResources();
            return await Task.FromResult(true);
        }

        private async Task CleanupEventSubResources()
        {
            if (InnerClient == null) return;
            Logger.Log(LogCategory.Info, "Cleaning up Twitch EventSub client resources...", this);
            if (IsConnected) { await InnerClient.DisconnectAsync(); }
            InnerClient = null;
            Logger.Log(LogCategory.Info, "Twitch EventSub client resources cleaned up.", this);
        }

        private async Task OnWebsocketConnected(object s, WebsocketConnectedArgs e)
        {
            IsConnected = true;
            if (!e.IsRequestedReconnect)
            {
                await RegisterEventSubTopicsAsync();
            }
        }

        private Task OnWebsocketDisconnected(object s, EventArgs e)
        {
            IsConnected = false;
            return WebsocketDisconnected != null ? WebsocketDisconnected.Invoke(s, e) : Task.CompletedTask;
        }

        private async Task<bool> RegisterEventSubTopicsAsync()
        {
            if (InnerClient == null || string.IsNullOrEmpty(InnerClient.SessionId))
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
            if (InnerClient == null || string.IsNullOrEmpty(InnerClient.SessionId) ||
                Api.InnerApi.Helix.EventSub == null) return false;
            try
            {
                Logger.Log(LogCategory.Info, $"Attempting to subscribe to [{type}:{version}]", this);
                var response = await Api.InnerApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    type, version, condition,
                    EventSubTransportMethod.Websocket, InnerClient.SessionId
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
    }
}
