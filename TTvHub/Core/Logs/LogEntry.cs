using System.Text;

namespace TTvHub.Core.Logs;

public enum LogCategory { Info,  Warning, Error, Debug }

public class LogEntry(LogCategory cat, string message, object source, Exception? exception = null)
{
    public DateTime TimeStamp { get; } = DateTime.Now;
    public LogCategory Category { get; } = cat;
    public string Message { get; } = message;
    public string Source { get; } = source.GetType().ToString().Split('.').Last();
    public Exception? Exception { get; } = exception;

    public string FormattedDisplayMessage => Exception switch
    {
        null => $"[{TimeStamp:HH:mm::ss}] [{Category}] [{Source}]: {Message}",
        _ => $"[{TimeStamp:HH:mm::ss}] [{Category}] [{Source}]: {Message} | Exception: {Exception?.Message}"
    };
    public string FormattedFileMessage
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{TimeStamp:HH:mm::ss}] [{Category}] [{Source}]: {Message}");
            if (Exception is not null)
            {
                sb.AppendLine("--- EXCEPTION ---")
                  .AppendLine($"Message: {Exception.Message}")
                  .AppendLine($"StackTrace: {Exception.StackTrace}")
                  .AppendLine("-------------------");
            }
            return sb.ToString();
        }
    }
    public override string ToString() => FormattedDisplayMessage;

}
