using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace TTvHub.Core.Services.Modules
{
    public sealed class TwitchChatModule : IAsyncDisposable
    {
        private TwitchClient _client = new();

        public bool IsConnected => _client.IsConnected;

        public event EventHandler<OnMessageReceivedArgs>? OnMessageReceived;
        public event EventHandler<OnChatCommandReceivedArgs>? OnChatCommandReceived;
        public event Action? OnConnected;
        public event Action? OnDisconnected;

        public void Connect(string username, string accessToken)
        {
            if (IsConnected) return;

            var credentials = new ConnectionCredentials(username, accessToken);
            _client.Initialize(credentials, username);

            _client.OnMessageReceived += (s, e) => OnMessageReceived?.Invoke(s, e);
            _client.OnChatCommandReceived += (s, e) => OnChatCommandReceived?.Invoke(s, e);
            _client.OnConnected += (s, e) => OnConnected?.Invoke();
            _client.OnDisconnected += (s, e) => OnDisconnected?.Invoke();

            _client.Connect();
        }
        public void Disconnect()
        {
            if (!IsConnected) return;
            _client.Disconnect();
        }

        public void SendMessage(string channel, string message)
        {
            if (IsConnected)
            {
                _client.SendMessage(channel, message);
            }
        }

        public void SendWhisper(string target, string message)
        {
            if (IsConnected)
            {
                _client.SendWhisper(target, message);
            } 
        }

        public async ValueTask DisposeAsync()
        {
            Disconnect();
            OnMessageReceived = null;
            OnChatCommandReceived = null;
            OnConnected = null;
            OnDisconnected = null;
        }

    }
}
