﻿namespace TTvHub.Core.Services.Interfaces;

public interface IPointsService
{
    Task<bool> AddPointsAsync(string username, long points);
    Task<bool> SetPointsAsync(string username, long points);
    Task<long> GetPointsAsync(string username);
    Task<IEnumerable<KeyValuePair<string, long>>> GetAllUsersPointsAsync(); 
    Task<string> GetUserIdByNameAsync(string username); 
    Task<string> GetUserNameByIdAsync(string userId);  
    Task<bool> AddPointsByIdAsync(string userId, long points);
    Task<bool> SetPointsByIdAsync(string userId, long points);
    Task<long> GetPointsByIdAsync(string userId);
}