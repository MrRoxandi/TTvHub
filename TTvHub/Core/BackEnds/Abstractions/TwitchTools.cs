﻿using TTvHub.Core.Services.Controllers;
using TwitchLib.Client.Enums;

namespace TTvHub.Core.BackEnds.Abstractions;

public static class TwitchTools
{
    internal static TwitchController? Service { get; set; }

    public static void SendMessage(string message)
    {
        if (Service is not { } client)
            throw new Exception("Unable to send twitch chat message. Client is null");
        client.SendChatMessage(message);
    }

    public static void SendWhisper(string target, string message)
    {
        if (Service is not { } client)
            throw new Exception("Unable to send twitch chat message. Client is null");
        client.SendWhisper(target, message);
    }

    public static void AddPoints(string name, int value)
    {
        if (Service is not { } client) throw new Exception("Unable to add points to the twitch user. Client is null");
        client.AddPointsAsync(name, value).GetAwaiter().GetResult();
    }

    public static long GetPoints(string name)
    {
        if (Service is not { } client) throw new Exception("Unable to get points of the twitch user. Client is null");
        return client.GetPointsAsync(name).GetAwaiter().GetResult();
    }

    public static void SetPoints(string name, int value)
    {
        if (Service is not { } client) throw new Exception("Unable to set points to the twitch user. Client is null");
        client.SetPointsAsync(name, value).GetAwaiter().GetResult();
    }

    public static long GetEventCost(string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) throw new Exception("Unable to get cost for event with empty name");
        if (Service is not { } client) throw new Exception("Unable to send twitch chat message. Client is null");
        var cost = client.GetEventCost(eventName);
        if (cost is not { } c)
            throw new Exception($"Unable to get cost of {eventName} event. Reason: this events doesn't exist.");
        return c;
    }

    public enum PermissionLevel : int
    {
        Viewer,
        Vip,
        Subscriber,
        Moderator,
        Broadcaster
    }

    public enum TwitchEventKind : int
    {
        Command = 0,
        Reward
    }

    public static PermissionLevel ParseFromTwitchLib(UserType type, bool isSub, bool isVip)
    {
        return type switch
        {
            UserType.Moderator or UserType.GlobalModerator or UserType.Staff or UserType.Admin => PermissionLevel
                .Moderator,
            UserType.Broadcaster => PermissionLevel.Broadcaster,
            _ => isSub ? PermissionLevel.Subscriber : isVip ? PermissionLevel.Vip : PermissionLevel.Viewer
        };
    }
}