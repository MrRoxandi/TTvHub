using System.Diagnostics;
using System.Net;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TTvHub.Core.Logs;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Twitch;

public class TwitchApi(string ClientId, string ClientSecret, string RedirectUrl)
{

    public TwitchAPI InnerApi { get; } = new()
    {
        Settings =
        {
            ClientId = ClientId,
            Secret = ClientSecret
        }
    };

    public async Task<(string? Token, string? RefreshToken)> GetAuthorizationInfo()
    {
        Logger.Log(LogCategory.Info, "Requesting new Twitch authentication token.", this);
        var authInfo = await RequestAuthorizationInfo();
        return authInfo;
    }

    private async Task<(string? Token, string? RefreshToken)> RequestAuthorizationInfo()
    {
        using HttpListener listener = new();
        listener.Prefixes.Add(RedirectUrl);
        listener.Start();

        Process.Start(new ProcessStartInfo(TokenUri) { UseShellExecute = true });

        var context = await listener.GetContextAsync();
        var request = context.Request;

        var code = request.QueryString["code"];
        var error = request.QueryString["error"];

        if (!string.IsNullOrEmpty(error)) throw new Exception($"Twitch authorization error: {error}");

        if (string.IsNullOrEmpty(code)) throw new Exception("Twitch authorization code is missing.");

        var (accessToken, refreshToken) = await GetAccessTokenAsync(code);

        if (accessToken == null || refreshToken == null) return (null, null);

        return (accessToken, refreshToken);
    }

    private string TokenUri => InnerApi.Auth.GetAuthorizationCodeUrl(RedirectUrl,
    [
        AuthScopes.Helix_Channel_Read_Redemptions,
        AuthScopes.Helix_Channel_Manage_Redemptions,
        AuthScopes.Chat_Edit, AuthScopes.Chat_Read,
        AuthScopes.Helix_User_Edit, AuthScopes.Helix_Moderator_Read_Chatters
    ], clientId: ClientId);

    private static string ServiceName => "TwitchAPI";

    public async Task<(string? Login, string? ID)> GetChannelInfoAsync(string token)
    {
        InnerApi.Settings.AccessToken = token;

        try
        {
            var usersResponse = await InnerApi.Helix.Users.GetUsersAsync();
            var user = usersResponse.Users.FirstOrDefault();
            if (user != null) return (user.Login, user.Id);
            Logger.Log(LogCategory.Warning, "Could not retrieve user information from Twitch API.", this);
            return (null, null);
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Error getting channel info from Twitch API.", this, ex);
            return (null, null);
        }
    }

    private async Task<(string? AccessToken, string? RefreshToken)> GetAccessTokenAsync(string authorizationCode)
    {
        try
        {
            var result =
                await InnerApi.Auth.GetAccessTokenFromCodeAsync(authorizationCode, ClientSecret, RedirectUrl, ClientId);
            return (result.AccessToken, result.RefreshToken);
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Unable to get access token due to error.", this, ex);
            return (null, null);
        }
    }

    public async Task<(string AccessToken, string RefreshToken)> RefreshAccessTokenAsync(string refreshToken)
    {
        try
        {
            var result = await InnerApi.Auth.RefreshAuthTokenAsync(refreshToken, ClientSecret, ClientId);
            return (result.AccessToken, result.RefreshToken);
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Unable to refresh token due to error.", this, ex);
            return (string.Empty, string.Empty);
        }
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        InnerApi.Settings.AccessToken = token;
        try
        {
            var usersResponse = await InnerApi.Helix.Users.GetUsersAsync();
            return usersResponse.Users.Length != 0; 
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Token validation failed.", this, ex);
            return false;
        }
    }
}