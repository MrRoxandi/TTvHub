using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using TTvHub.Core.Managers.AuthManagerItems;
using TTvHub.Core.Twitch;
using TTvHub.Core.Logs;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Services.Modules;

public partial class TwitchAuthModule
{
    private static readonly string ClientId = "--";
    private static readonly string ClientSecret = "--";
    private static readonly string RedirectUrl = @"http://localhost:6969/";

    private readonly AuthDbContext _db;
    public TwitchAuthData? CurrentUser { get; private set; } = null;
    public bool IsAuthenticated => CurrentUser != null;
    public string Login => CurrentUser?.Login ?? string.Empty;
    public string TwitchId => CurrentUser?.TwitchUserId ?? string.Empty;

    
    public readonly TwitchApi _api;
    public TwitchAuthModule()
    {
        _db = new AuthDbContext();
        _db.EnsureCreated();
        _api = new TwitchApi(ClientId, ClientSecret, RedirectUrl);
    }

    // public block

    public async Task InitializeAsync()
    {
        var storedData = await ReadStoredDataAsync();
        if (storedData == null)
        {
            Logger.Log(LogCategory.Info, "Saved information could not be found.", this);
            return;
        }
        try
        {
            var decryptedData = await DecryptAuthData(storedData);

            if (!await _api.ValidateTokenAsync(decryptedData.AccessToken))
            {
                Logger.Log(LogCategory.Info, "AccessToken validation failed. Attempting to refresh...", this);
                var (newAccessToken, newRefreshToken) = await _api.RefreshAccessTokenAsync(decryptedData.RefreshToken);

                if (string.IsNullOrEmpty(newAccessToken) || string.IsNullOrEmpty(newRefreshToken))
                {
                    Logger.Log(LogCategory.Error, "Failed to refresh tokens... Need relogin", this);
                    await ClearStoredDataAsync(); 
                    return;
                }

                decryptedData.AccessToken = newAccessToken;
                decryptedData.RefreshToken = newRefreshToken;
            }
            CurrentUser = decryptedData;
            await StoreDataAsync(); 
            Logger.Log(LogCategory.Info, $"Authentication was successful for the user: {CurrentUser.Login}.", this);
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "Error initializing authentication. Clearing credentials.", this, ex);
            await ClearStoredDataAsync();
            CurrentUser = null;
        }
        // If nothing stored do nothing, user should relog with method LoginAsync()
    }

    public async Task LoginAsync()
    {
        if (IsAuthenticated) return;
        try
        {
            var (accessToken, refreshToken) = await _api.GetAuthorizationInfo();
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                Logger.Log(LogCategory.Warning, "Authentication failed: Could not obtain tokens.", this);
                return;
            }

            var (login, userId) = await _api.GetChannelInfoAsync(accessToken);
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(userId))
            {
                Logger.Log(LogCategory.Warning, "Authentication failed: Unable to obtain user information.", this);
                return;
            }

            var authData = new TwitchAuthData
            {
                Login = login,
                TwitchUserId = userId,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };
            CurrentUser = authData;
            await StoreDataAsync();

            Logger.Log(LogCategory.Info, $"Successfully authenticated and saved credentials for {CurrentUser.Login}.", this);
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, "An exception occurred during the authentication process.", this, ex);
            await LogoutAsync();
        }

    }

    public async Task LogoutAsync()
    {
        await ClearStoredDataAsync();

        CurrentUser = null;
        Logger.Log(LogCategory.Info, "The user has been logged out and all credentials have been cleared.", this);
    }

    // private block 
    private async Task<TwitchAuthData?> ReadStoredDataAsync() => await _db.AuthenticationData.FirstOrDefaultAsync();
    private async Task StoreDataAsync()
    {
        await ClearStoredDataAsync();
        ArgumentNullException.ThrowIfNull(CurrentUser);
        var encryptedData = await EncryptAuthData(CurrentUser);
        _db.AuthenticationData.Add(encryptedData);
        await _db.SaveChangesAsync();
    }
    private async Task ClearStoredDataAsync()
    {
        var allData = await _db.AuthenticationData.ToListAsync();
        if (allData.Count != 0)
        {
            _db.AuthenticationData.RemoveRange(allData);
            await _db.SaveChangesAsync();
        }
    }

    private static async Task<TwitchAuthData> DecryptAuthData(TwitchAuthData cryptedData)
    {
        var accessToken = await DecryptAsync(cryptedData.EncryptedAccessToken, ClientSecret);
        var refreshToken = await DecryptAsync(cryptedData.EncryptedRefreshToken, ClientSecret);
        return new TwitchAuthData 
        { 
            Id = cryptedData.Id, 
            AccessToken = accessToken, 
            RefreshToken = refreshToken, 
            Login = cryptedData.Login, 
            TwitchUserId = cryptedData.TwitchUserId
        };
    }

    private static async Task<TwitchAuthData> EncryptAuthData(TwitchAuthData authData)
    {
        var accessToken = await EncryptAsync(authData.AccessToken, ClientSecret);
        var refreshToken = await EncryptAsync(authData.RefreshToken, ClientSecret);
        return new TwitchAuthData
        {
            Id = authData.Id,
            EncryptedAccessToken = accessToken,
            EncryptedRefreshToken = refreshToken,
            Login = authData.Login,
            TwitchUserId = authData.TwitchUserId
        };
    }

    // --- Cryptographi logic here ---
    private static async Task<string> EncryptAsync(string plainText, string secret)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentNullException.ThrowIfNull(secret);

        using var aes = Aes.Create();
        aes.Key = await DeriveKeyAsync(secret);
        aes.GenerateIV(); // IV generation is fast and synchronous

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

        // Use 'await using' for IAsyncDisposable streams
        await using var ms = new MemoryStream();

        // Write the non-secret IV to the beginning of the stream
        await ms.WriteAsync(aes.IV, 0, aes.IV.Length);

        await using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        await using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            await sw.WriteAsync(plainText);
        } // `await using` will asynchronously flush the final crypto block

        return Convert.ToBase64String(ms.ToArray());
    }

    public static async Task<string> DecryptAsync(string cipherTextBase64, string secret)
    {
        ArgumentNullException.ThrowIfNull(cipherTextBase64);
        ArgumentNullException.ThrowIfNull(secret);

        // Convert the Base64 string back into bytes
        var buffer = Convert.FromBase64String(cipherTextBase64);

        using var aes = Aes.Create();
        var ivLength = aes.BlockSize / 8; // Get the IV length in bytes (e.g., 16 for AES-128/256)

        if (buffer.Length < ivLength)
            throw new CryptographicException("Invalid cipher text buffer: too short to contain IV.");

        aes.Key = await DeriveKeyAsync(secret);

        // Extract the IV from the beginning of the buffer
        var iv = new byte[ivLength];
        Array.Copy(buffer, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

        // Use 'await using' for the disposable streams
        await using var ms = new MemoryStream(buffer, ivLength, buffer.Length - ivLength);
        await using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        // StreamReader can also be used with 'await using'
        using var sr = new StreamReader(cs, Encoding.UTF8);

        return await sr.ReadToEndAsync();
    }

    private static Task<byte[]> DeriveKeyAsync(string secret)
    {
        return Task.Run(() =>
        {
            var saltBytes = Encoding.UTF8.GetBytes(secret); // Using the secret as the salt is acceptable for some scenarios
            const int iterations = 10000; // Increased iterations for better security

            using var deriveBytes = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(secret),
                saltBytes,
                iterations,
                HashAlgorithmName.SHA256);

            // Generate a 32-byte (256-bit) key, suitable for AES-256
            return deriveBytes.GetBytes(32);
        });
    }
}
