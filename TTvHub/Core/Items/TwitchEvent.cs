using Lua;
using System.Diagnostics;
using TTvHub.Core.BackEnds.Abstractions;
using TTvHub.Core.Logs;
using Logger = TTvHub.Core.Logs.StaticLogger;

namespace TTvHub.Core.Items
{
    public struct TwitchEventArgs
    {
        public string Sender;
        public string[]? Args;
        public LuaState State;
        public TwitchTools.PermissionLevel Permission;
    }

    public class TwitchEvent
    {
        // --- Main things in event ---
        public TwitchTools.TwitchEventKind Kind { get; private set; }
        private LuaFunction Function { get; }
        public string Name { get; }

        // --- Other stuff ---
        public readonly TwitchTools.PermissionLevel PermissionLevel;
        private readonly Stopwatch? _coolDownTimer;
        private readonly long? _timeOut;
        public readonly long Cost;

        // --- Executing checks ---
        public bool Executable => IsExecutable();

        public static string ItemName => nameof(TwitchEvent);

        public TwitchEvent(TwitchTools.TwitchEventKind kind, LuaFunction action, string name, TwitchTools.PermissionLevel? permissionLevel = null, long? timeOut = null, long cost = 0)
        {
            Kind = kind;
            Function = action;
            Name = name;
            if (permissionLevel is not { } perm)
                perm = TwitchTools.PermissionLevel.Viewer;
            PermissionLevel = perm;
            if (timeOut is not { } time) return;
            _coolDownTimer = new Stopwatch();
            _timeOut = time;
            Cost = cost;
        }

        public void Execute(TwitchEventArgs args)
        {
            if (args.Permission < PermissionLevel)
            {
                Logger.Log(LogCategory.Info, $"Unable to execute event [{Name}]. {args.Sender} has no permission to do that", this);
                return;
            }
            try
            {
                if (!IsExecutable())
                {
                    Logger.Log(LogCategory.Info, $"Unable to execute event [{Name}]. Event still on cooldown", this);
                    return;
                }

                var argsTable = new LuaTable();
                if (args.Args is not null)
                {
                    foreach (var (value, index) in args.Args.Select((x, i) => (x, i + 1)))
                    {
                        argsTable.Insert(index, value);
                    }
                }

                var action = Function.InvokeAsync(args.State, [args.Sender, (LuaValue)argsTable]);
                _ = action.AsTask().GetAwaiter().GetResult();
                _coolDownTimer?.Restart();
                Logger.Log(LogCategory.Info, $"Event [{Name}] was executed successfully.", this);
            }
            catch (Exception e)
            {
                Logger.Log(LogCategory.Error, $"Unable to execute event [{Name}] due to error:", this, e);
            }
        }

        private bool IsExecutable()
        {
            if (_timeOut == null) return true;
            return !_coolDownTimer!.IsRunning || _coolDownTimer!.ElapsedMilliseconds > _timeOut;
        }
        
    }
}
