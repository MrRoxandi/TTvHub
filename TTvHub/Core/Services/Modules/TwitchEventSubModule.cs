using TwitchLib.EventSub.Core;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;

namespace TTvHub.Core.Services.Modules
{
    public sealed class TwitchEventSubModule : IAsyncDisposable
    {
        private EventSubWebsocketClient _client = new();
        public event AsyncEventHandler<ChannelPointsCustomRewardRedemptionArgs>? ChannelPointsCustomRewardRedemptionAdd;
        public event AsyncEventHandler<WebsocketConnectedArgs>? WebsocketConnected;
        public event AsyncEventHandler<ErrorOccuredArgs>? ErrorOccurred;
        public event AsyncEventHandler? WebsocketDisconnected;

        public bool IsConnected { get; private set; } = false;
        public string SessionId => _client.SessionId ?? string.Empty;
        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            _client.ChannelPointsCustomRewardRedemptionAdd += (s,e) => ChannelPointsCustomRewardRedemptionAdd!.Invoke(s, e);
            _client.ErrorOccurred += (s,e) => ErrorOccurred!.Invoke(s, e);
            _client.WebsocketDisconnected += OnWebsocketDisconnected;
            _client.WebsocketConnected += OnWebsocketConnected;
            if (await _client.ConnectAsync())
            {
                IsConnected = true;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!IsConnected) return;
            _client.WebsocketConnected -= OnWebsocketConnected;
            _client.WebsocketDisconnected -= OnWebsocketDisconnected;

            await _client.DisconnectAsync();
            IsConnected = false;
        }

        private Task OnWebsocketConnected(object s, WebsocketConnectedArgs e)
        {
            IsConnected = true;
            return WebsocketConnected != null ? WebsocketConnected.Invoke(s, e) : Task.CompletedTask;
        }

        private Task OnWebsocketDisconnected(object s, EventArgs e)
        {
            IsConnected = false;
            return WebsocketDisconnected != null ? WebsocketDisconnected.Invoke(s, e) : Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            ChannelPointsCustomRewardRedemptionAdd = null;
            WebsocketDisconnected = null;
            WebsocketConnected = null;
            ErrorOccurred = null;
        }
    }
}
