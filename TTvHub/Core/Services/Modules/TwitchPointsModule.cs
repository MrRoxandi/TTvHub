using Microsoft.EntityFrameworkCore;
using TTvHub.Core.Managers.PointsManagerItems;
using TTvHub.Core.Twitch;
using TwitchLib.Api.Helix.Models.Common;

namespace TTvHub.Core.Services.Modules;

public class TwitchPointsModule
{
    private readonly string Tag = "Twitch";
    private readonly PointsDbContext _db ;
    private readonly TwitchApi _api;

    // --- Public Block ---

    public TwitchPointsModule(TwitchApi api) 
    {
        _api = api;
        _db = new PointsDbContext(Tag);
        _db.EnsureCreated();
    }

    public async Task<bool> AddPointsAsync(string userName, long points)
    {
        if (string.IsNullOrEmpty(userName)) return await Task.FromResult(false);
        var id = await GetUserIdByName(userName);
        if (string.IsNullOrEmpty(id)) return await Task.FromResult(false);
        return await AddUserPointsAsync(points, id);
    }
    public async Task<bool> AddPointsByIdAsync(string id, long points)
    {
        if (string.IsNullOrEmpty(id)) return await Task.FromResult(false);
        return await AddUserPointsAsync(points, id);
    }

    public async Task<bool> SetPointsAsync(string userName, long points)
    {
        if (string.IsNullOrEmpty(userName)) return await Task.FromResult(false);
        var id = await GetUserIdByName(userName);
        if (string.IsNullOrEmpty(id)) return await Task.FromResult(false);
        return await SetUserPointsAsync(points, id);
    }
    public async Task<bool> SetPointsByIdAsync(string id, long points)
    {
        if (string.IsNullOrEmpty(id)) return await Task.FromResult(false);
        return await SetUserPointsAsync(points, id);
    }

    public async Task<long> GetPointsAsync(string userName)
    {
        if (string.IsNullOrEmpty(userName)) return await Task.FromResult(0);
        var id = await GetUserIdByName(userName);
        if (string.IsNullOrEmpty(id)) return await Task.FromResult(0);
        return await GetUserPointsAsync(id);
    }

    public async Task<long> GetPointsByIdAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return await Task.FromResult(0);
        return await GetUserPointsAsync(id);
    }

    public async Task<IEnumerable<KeyValuePair<string, long>>> GetAllPointsAsync() => await GetAllUsersPointsAsync();
    // --- Private Block ---

    private async Task<bool> ContainsUserAsync(string userId = "")
    {
        if (string.IsNullOrEmpty(userId)) return await Task.FromResult(false);
        return await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId) != null;
    }
    private async Task<bool> CreateUserAsync(string userId = "")
    {
        if (await ContainsUserAsync(userId)) return await Task.FromResult(true);
        var isUserId = !string.IsNullOrEmpty(userId);
        if (!isUserId) return await Task.FromResult(false);
        try
        {
            var userNameApi = await _api.InnerApi.Helix.Users.GetUsersAsync(ids: [userId]);
            var login = userNameApi.Users.First().Login;
            await _db.Users.AddAsync(new PointsData() { Points = 0, UserId = userId, Username = login });
            await _db.SaveChangesAsync();
            return await Task.FromResult(true);
        }
        catch (Exception)
        {
            return await Task.FromResult(false);
        }
    } 
    private async Task<PointsData> GetUserAsync(string userId = "") => await _db.Users.FirstAsync(u => u.UserId == userId);
    
    private async Task<bool> AddUserPointsAsync(long points, string userId = "")
    {
        if (string.IsNullOrEmpty(userId)) return await Task.FromResult(false);
        if (!await ContainsUserAsync(userId))
        {
            var res = await CreateUserAsync(userId);
            if (!res) return await Task.FromResult(false);
        }
        var user = await GetUserAsync(userId);
        user.Points += points;
        await _db.SaveChangesAsync();
        return await Task.FromResult(true);
    }
    private async Task<bool> SetUserPointsAsync(long points, string userId = "")
    {
        if (string.IsNullOrEmpty(userId)) return await Task.FromResult(false);
        if (!await ContainsUserAsync(userId))
        {
            var res = await CreateUserAsync(userId);
            if (!res) return await Task.FromResult(false);
        }
        var user = await GetUserAsync(userId);
        user.Points = points;
        await _db.SaveChangesAsync();
        return await Task.FromResult(true);
    }
    private async Task<long> GetUserPointsAsync(string userId)
    {
        if (!await ContainsUserAsync(userId)) return await Task.FromResult(0);
        var user = await GetUserAsync(userId);
        return user.Points;
    }
    private async Task<IEnumerable<KeyValuePair<string, long>>> GetAllUsersPointsAsync()
    {
        return await _db.Users.Select(u => new KeyValuePair<string, long>(u.Username, u.Points)).ToArrayAsync();
    }

    private async Task<string> GetUserIdByName(string userName)
    {
        if (string.IsNullOrEmpty(userName)) return await Task.FromResult("");
        try
        {
            var user = await _api.InnerApi.Helix.Users.GetUsersAsync(logins: [userName]);
            return await Task.FromResult(user.Users.First().Id);
        }
        catch (Exception) { }
        return await Task.FromResult("");
    }
}
