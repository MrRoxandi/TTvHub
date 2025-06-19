using System.Collections.Concurrent;
using System.Text;

namespace TTvHub.Core.Logs;

public static class StaticLogger
{
    private static readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private static readonly ConcurrentQueue<LogEntry> _displayQueue = new();
    private static readonly CancellationTokenSource _cts = new();
    private static readonly string _logsDirectory = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "logs")).FullName;
    private static readonly string _logFilePath = Path.Combine(_logsDirectory, $"{DateTime.Now:yyyy-MM-dd_HH-mm}.log");
    private static readonly FileStream _fileStream = new(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
    private static readonly StreamWriter _writer = new(_fileStream) { AutoFlush = false };
    private static readonly Task _processingTask = Task.Run(ProcessLogQueue);
    private static readonly int MaxDisplayLogs = 24;
    
    private static async Task ProcessLogQueue()
    {
        var batch = new StringBuilder();
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cts.Token);

                while (_logQueue.TryDequeue(out var entry))
                {
                    batch.AppendLine(entry.FormattedFileMessage);
                }

                if (batch.Length > 0)
                {
                    await _writer.WriteAsync(batch.ToString());
                    await _writer.FlushAsync();
                    batch.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL LOGGER ERROR]: Failed to write logs. {ex}");
            }
        }
        await FlushRemainingLogsAsync();
    }

    private static async Task FlushRemainingLogsAsync()
    {
        try
        {
            var batch = new StringBuilder();
            while (_logQueue.TryDequeue(out var message))
            {
                batch.AppendLine(message.FormattedFileMessage);
            }

            if (batch.Length > 0)
            {
                await _writer.WriteAsync(batch.ToString());
            }
            await _writer.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL LOGGER ERROR]: Failed to flush remaining logs. {ex}");
        }
    }

    private static void QueueLog(LogCategory type, string message, object source, Exception? err = null)
    {
        var entry = new LogEntry(type, message, source, err);
        _logQueue.Enqueue(entry);
        _displayQueue.Enqueue(entry);
        while (_displayQueue.Count > MaxDisplayLogs)
        {
            _displayQueue.TryDequeue(out _);
        }
        OnLogAdded(entry);
    }

    public static IEnumerable<LogEntry> LastLogs => [.. _displayQueue];
    public static void Log(LogCategory type, string message, object source, Exception? err = null) => QueueLog(type, message, source, err);

    public static event Func<LogEntry, Task>? LogAdded;

    private static void OnLogAdded(LogEntry entry)
    {
        LogAdded?.Invoke(entry);
    }
}
