using System.Text.Json.Serialization;

namespace TTvHub.Core.Managers.LuaSUMItems;

public struct MainSettings()
{
    [JsonInclude]
    public long StdTimeOut = 30000;
    [JsonInclude]
    public bool IsDarkMode = false;
    [JsonInclude]
    public int ClipCheckIntervalMinutes = 15;
    [JsonInclude]
    public int PointsPerMessage = 2;
    [JsonInclude]
    public int PointsPerClip = 10;
}
