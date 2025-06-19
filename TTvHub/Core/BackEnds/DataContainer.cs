using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TTvHub.Core.BackEnds.ContainerItems;
using TTvHub.Core.Logs;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.BackEnds;

public sealed class DataContainer
{
    private const string BackEndName = "Container";
    private readonly JsonDbContext _db;

    public DataContainer()
    {
        _db = new JsonDbContext();
        _db.EnsureCreated();
    }

    // --- Random data related methods ---

    public void AddOrUpdateItem<T>(string name, T value)
    {
        var dataTable = _db.DataTable.FirstOrDefault(t => t.Name == name);
        dataTable ??= new JsonTable { Name = name };
        dataTable.JsonData = JsonSerializer.Serialize(value);
        if (dataTable.Id == 0)
            _db.DataTable.Add(dataTable);
        _db.SaveChanges();
    }

    public async Task AddOrUpdateItemAsync<T>(string name, T value)
    {
        var dataTable = _db.DataTable.FirstOrDefault(t => t.Name == name);
        dataTable ??= new JsonTable { Name = name };
        dataTable.JsonData = JsonSerializer.Serialize(value);
        if (dataTable.Id == 0)
            await _db.DataTable.AddAsync(dataTable);
        await _db.SaveChangesAsync();
    }

    public T? GetItem<T>(string name)
    {
        var dataTable = _db.DataTable.FirstOrDefault(t => t.Name == name);
        if (dataTable == null) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(dataTable.JsonData);
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, $"Error deserializing item '{name}' to type {typeof(T).Name}.", this, ex);
            return default;
        }
    }

    public async Task<T?> GetItemAsync<T>(string name)
    {
        return await Task.Run(() =>
            {
                var dataTable = _db.DataTable.FirstOrDefault(t => t.Name == name);
                if (dataTable == null) return default;
                try
                {
                    return JsonSerializer.Deserialize<T>(dataTable.JsonData);
                }
                catch (Exception ex)
                {
                    Logger.Log(LogCategory.Error,
                        $"Error deserializing item '{name}' to type {typeof(T).Name}.", this, ex);
                    return default;
                }
            }
        ).ConfigureAwait(false);
    }

    public bool RemoveItem(string name)
    {
        var dataTable = _db.DataTable.FirstOrDefault(t => t.Name == name);
        if (dataTable == null) return false;
        _db.DataTable.Remove(dataTable);
        _db.SaveChanges();
        Logger.Log(LogCategory.Info, $"Item '{name}' was removed successfully.", this);
        return true;
    }

    public async Task<bool> RemoveItemAsync(string name)
    {
        var dataTable = _db.DataTable.FirstOrDefault(t => t.Name == name);
        if (dataTable == null) return true;
        _db.DataTable.Remove(dataTable);
        await _db.SaveChangesAsync();
        Logger.Log(LogCategory.Info, $"Item '{name}' was removed successfully.", this);
        return true;
    }

    public bool Contains(string name)
    {
        return _db.DataTable.Any(t => t.Name == name);
    }

    public async Task<bool> ContainsAsync(string name)
    {
        return await _db.DataTable.AnyAsync(t => t.Name == name);
    }
}