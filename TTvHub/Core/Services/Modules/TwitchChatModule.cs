using System.Net;
using TTvHub.Core.Logs;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Services.Modules
{
    public sealed class TwitchChatModule
    {
        private TwitchClient? InnerClient;
        public bool IsConnected => InnerClient?.IsConnected ?? false;
        public bool DisconnectRequsted { get; private set; } = false;

        public event EventHandler<OnMessageReceivedArgs>? OnMessageReceived;
        public event EventHandler<OnChatCommandReceivedArgs>? OnChatCommandReceived;
        public event Action? OnDisconnected;
        public async Task<bool> ConnectAsync(string username, string accessToken)
        {
            if (IsConnected)
            {
                Logger.Log(LogCategory.Warning, "Twitch chat module is already connected.", this);
                return await Task.FromResult(false);
            }
            var credentials = new ConnectionCredentials(username, accessToken);
            if (InnerClient != null)
            {
                await DisconnectAsync();
            }
            InnerClient = new();
            InnerClient.Initialize(credentials, username);
            
            InnerClient.OnMessageReceived += (s, e) => OnMessageReceived?.Invoke(s, e);
            InnerClient.OnChatCommandReceived += (s, e) => OnChatCommandReceived?.Invoke(s, e);
            InnerClient.OnConnected += OnConnectedHandler;
            InnerClient.OnDisconnected += (s, e) => OnDisconnected?.Invoke();
            DisconnectRequsted = false;
            return await Task.FromResult(InnerClient.Connect());
        }

        public async Task<bool> DisconnectAsync()
        {
            if (InnerClient == null)
            {
                Logger.Log(LogCategory.Warning, "Twitch chat module is already disconnected.", this);
                return await Task.FromResult(false);
            }
            DisconnectRequsted = true;
            InnerClient.Disconnect();
            CleanupChatResources();
            return await Task.FromResult(true);
        }

        public void SendMessage(string channel, string message)
        {
            if (IsConnected)
            {
                InnerClient!.SendMessage(channel, message);
            }
        }

        public void SendWhisper(string target, string message)
        {
            if (IsConnected)
            {
                InnerClient!.SendWhisper(target, message);
            } 
        }

        private void OnConnectedHandler(object? sender, OnConnectedArgs e)
        {
            
            Logger.Log(LogCategory.Info, "Chat module is connected", this);
        }

        private void CleanupChatResources()
        {
            if (InnerClient == null) return;
            Logger.Log(LogCategory.Info, "Cleaning up Twitch Chat client resources...", this);
            if (IsConnected) { InnerClient.Disconnect(); }
            InnerClient = null;
            Logger.Log(LogCategory.Info, "Twitch Chat client resources cleaned up.", this);
        }
    }
}
