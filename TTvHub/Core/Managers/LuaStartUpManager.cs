using Lua;
using Lua.Standard;
using Microsoft.Maui.Storage;
using System.Collections.Concurrent;
using TTvHub.Core.BackEnds.Abstractions;
using TTvHub.Core.Items;
using TTvHub.Core.Logs;
using TTvHub.Core.LuaWrappers.Hardware;
using TTvHub.Core.LuaWrappers.Services;
using TTvHub.Core.LuaWrappers.Stuff;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Managers;

public sealed partial class LuaStartUpManager
{
    public LuaState State { get; }
    public bool ForceRelog { get; private set; }
    public bool MoreLogs { get; private set; }
    public long StdTimeOut { get; private set; }
    public bool IsConfigured { get; private set; } = false;

    public static string ConfigsFolder => Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "configs")).FullName;
    public static string MainConfig => "config.lua";
    public static string TwitchEventsConfig => "TwitchEvents.lua";
    public static string TimerActionsConfig => "TimerActions.lua";


    private LuaStartUpManager()
    {
        State = LuaState.Create();
        State.OpenStandardLibraries();
        State.Environment["Funcs"] = new LuaFunctions();
        State.Environment["Audio"] = new LuaAudio();
        State.Environment["Container"] = new LuaContainer();
        State.Environment["TwitchTools"] = new LuaTwitchTools();
        State.Environment["Keyboard"] = new LuaKeyboard();
        State.Environment["Mouse"] = new LuaMouse();
    }

    public static async Task<LuaStartUpManager> CreateAsync()
    {
        var manager = new LuaStartUpManager();
        return manager;
    }

    public async Task ReadMainConfigAsync()
    {
        var fileResult = await ParseLuaFileAsync(Path.Combine(ConfigsFolder, MainConfig))
            ?? throw new Exception($"File {MainConfig} is not a proper config. Check syntax.");

        ForceRelog = fileResult["force-relog"].Type == LuaValueType.Boolean && fileResult["force-relog"].Read<bool>();

        MoreLogs = fileResult["logs"].Type == LuaValueType.Boolean && fileResult["logs"].Read<bool>();

        long timeout = fileResult["timeout"].Type == LuaValueType.Number
            ? fileResult["timeout"].Read<long>()
            : 30000;

        StdTimeOut = timeout > 0 ? timeout : 30000;

        Logger.Log(LogCategory.Info, "Main configuration loaded successfully", this);
        IsConfigured = true;
    }

    private async Task<LuaTable?> ParseLuaFileAsync(string filePath)
    {
        var configName = Path.GetFileName(filePath);
        if (!File.Exists(filePath))
        {
            Logger.Log(LogCategory.Warning, $"File [{configName}] not found and can not be read.", this);
            return null;
        }
        try
        {
            var result = await State.DoFileAsync(filePath);
            if (result[0].Type != LuaValueType.Table)
            {
                Logger.Log(LogCategory.Error, $"Returned result from {configName} was not a valid table. Check syntax", this);
                return null;
            }
            return result[0].Read<LuaTable>();
        }
        catch (Exception ex)
        {
            Logger.Log(LogCategory.Error, $"During parsing lua file [{configName}] occured an error.", this, ex);
            return null;
        }
    }

    public async Task<ConcurrentDictionary<(string, TwitchTools.TwitchEventKind), TwitchEvent>> LoadTwitchEventsAsync()
    {
        const string fileName = "TwitchEvents.lua";
        var configTable = await ParseLuaFileAsync(fileName);

        if (configTable == null || configTable.HashMapCount == 0)
        {
            Logger.Log(LogCategory.Warning, $"Table from file {fileName} is empty. Ignoring...", this);
            return [];
        }

        var result = new ConcurrentDictionary<(string, TwitchTools.TwitchEventKind), TwitchEvent>();
        var previosKey = LuaValue.Nil;
        while (configTable.TryGetNext(previosKey, out var kvp))
        {
            var currentKey = kvp.Key;
            if (kvp.Value.Type != LuaValueType.Table)
            {
                Logger.Log(LogCategory.Error,
                    $"In file {fileName} ['{currentKey}'] is not a TwitchEvent. Check syntax. Aborting loading process ...", this);
                return [];
            }

            var twEventTable = kvp.Value.Read<LuaTable>();
            if (twEventTable["kind"].Type != LuaValueType.Number)
            {
                Logger.Log(LogCategory.Error,
                    $"In file {fileName} ['{currentKey}']['kind'] is not a TwitchEventKind. Check syntax. Aborting loading process ...", this);
                return [];
            }

            var kind = (TwitchTools.TwitchEventKind)twEventTable["kind"].Read<int>();
            if (twEventTable["action"].Type != LuaValueType.Function)
            {
                Logger.Log(LogCategory.Error,
                    $"In file {fileName} ['{currentKey}']['action'] is not an action. Check syntax. Aborting loading process ...", this);
                return [];
            }

            var action = twEventTable["action"].Read<LuaFunction>();
            long? timeout = null;
            long cmdCost = 0;
            var perm = TwitchTools.PermissionLevel.Viewer;
            if (kind != TwitchTools.TwitchEventKind.Reward)
            {
                if (twEventTable["timeout"].Type != LuaValueType.Number)
                {
                    Logger.Log(LogCategory.Warning,
                        $"In file {fileName} ['{currentKey}']['timeout'] is not a timeout. Will be used default value: {StdTimeOut} ms", this);
                    timeout = StdTimeOut;
                }
                else
                {
                    timeout = twEventTable["timeout"].Read<long>();
                }

                if (timeout < 0 && timeout != -1)
                {
                    Logger.Log(LogCategory.Warning,
                        $"In file {fileName} ['{currentKey}']['timeout'] is not a valid timeout. Will be used default value: {StdTimeOut} ms", this);
                    timeout = StdTimeOut;
                }

                if (twEventTable["perm"].Type != LuaValueType.Number)
                {
                    Logger.Log(LogCategory.Warning,
                        $"In file {fileName} ['{currentKey}']['perm'] is not a permission level. Will be used default value: Viewer", this);
                    perm = TwitchTools.PermissionLevel.Viewer;
                }
                else
                {
                    perm = (TwitchTools.PermissionLevel)twEventTable["perm"].Read<int>();
                }

                cmdCost = twEventTable["cmdCost"].Type != LuaValueType.Number
                    ? 0
                    : twEventTable["cmdCost"].Read<long>();
            }

            result.TryAdd((currentKey.ToString(), kind),
                new TwitchEvent(kind, action, currentKey.ToString(), perm, timeout, cmdCost));
            previosKey = kvp.Key;
        }

        return result;
    }
}
