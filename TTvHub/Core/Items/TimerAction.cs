//using Lua;
//using TTvActionHub.UI.Core.Logs;
//using TTvHub.Core.Items;
//using Logger = TTvActionHub.UI.Core.Logs.StaticLogger;
//using Timer = System.Timers.Timer;

//namespace TTvActionHub.UI.Core.Items;

//public class TimerAction() : IAction
//{
//    public required LuaFunction Function { get; set; }
//    public bool IsRunning => _timer != null;
//    public required LuaState State;
//    public required long TimeOut;
//    public required string Name;
//    private Timer? _timer;
//    public void Run()
//    {
        
//        _timer = new Timer(TimeOut);
//        _timer.Elapsed += TimerElapsed;
//        _timer.Start();
//    }

//    public void Stop()
//    {
//        if (_timer is not null)
//        {
//            _timer.Stop();
//            _timer.Dispose();
//            _timer = null;
//        }
//    }

//    private void TimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
//    {
//        Task.Run(() =>
//        {
//            try
//            {
//                _ = Function.InvokeAsync(State, []).AsTask().GetAwaiter().GetResult();
//                Logger.Log(LogCategory.Info, $"Timer event [{Name}] was executed at [{e.SignalTime}]", this);
//            }
//            catch (Exception ex)
//            {
//                Logger.Log(LogCategory.Error, $"While executing timer event [{Name}] occured an error", this, ex);
//                Logger.Log(LogCategory.Info, $"Stopping timer event [{Name}]", this);
//                this.Stop();
//            }
//        });
//    }
//}
