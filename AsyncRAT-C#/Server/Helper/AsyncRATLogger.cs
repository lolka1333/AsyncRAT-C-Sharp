using System.Collections.Concurrent;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Server.Handle_Packet;

namespace Server.Helper;

/// <summary>
/// Modern async logger implementation for AsyncRAT (.NET 9.0)
/// </summary>
public sealed class AsyncRATLogger : IAsyncRATLogger, IDisposable
{
    private readonly ILogger<AsyncRATLogger> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _logDirectory;
    private readonly string _logFile;
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly Timer _flushTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    private long _totalEntries;
    private long _errorCount;
    private long _warningCount;
    private DateTime _lastLogTime = DateTime.Now;
    private bool _disposed;

    public AsyncRATLogger(ILogger<AsyncRATLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logDirectory = Path.Combine(Application.StartupPath, "Logs");
        _logFile = Path.Combine(_logDirectory, $"AsyncRAT_{DateTime.Now:yyyyMMdd}.log");
        
        EnsureLogDirectoryExists();
        
        // Flush logs every 5 seconds
        _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        
        // Start background log processor
        _ = Task.Run(ProcessLogQueueAsync, _cancellationTokenSource.Token);
    }

    public async Task LogAsync(LogLevel level, string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        var logEntry = new LogEntry(
            DateTime.Now,
            level,
            message,
            exception?.ToString(),
            Environment.CurrentManagedThreadId
        );

        _logQueue.Enqueue(logEntry);
        
        // Update statistics
        Interlocked.Increment(ref _totalEntries);
        _lastLogTime = logEntry.Timestamp;
        
        switch (level)
        {
            case LogLevel.Error or LogLevel.Critical:
                Interlocked.Increment(ref _errorCount);
                break;
            case LogLevel.Warning:
                Interlocked.Increment(ref _warningCount);
                break;
        }

        // Also log to Microsoft.Extensions.Logging
        _logger.Log(level, exception, message);
        
        // Log to UI if available
        await LogToUIAsync(level, message);
    }

    public Task DebugAsync(string message, CancellationToken cancellationToken = default) =>
        LogAsync(LogLevel.Debug, message, cancellationToken: cancellationToken);

    public Task InfoAsync(string message, CancellationToken cancellationToken = default) =>
        LogAsync(LogLevel.Information, message, cancellationToken: cancellationToken);

    public Task WarningAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default) =>
        LogAsync(LogLevel.Warning, message, exception, cancellationToken);

    public Task ErrorAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default) =>
        LogAsync(LogLevel.Error, message, exception, cancellationToken);

    public Task CriticalAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default) =>
        LogAsync(LogLevel.Critical, message, exception, cancellationToken);

    public async Task CleanupOldLogsAsync(int daysToKeep = 7, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            if (!Directory.Exists(_logDirectory)) return;

            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            var logFiles = Directory.GetFiles(_logDirectory, "AsyncRAT_*.log");

            var deleteTasks = logFiles
                .Where(file => new FileInfo(file).CreationTime < cutoffDate)
                .Select(async file =>
                {
                    try
                    {
                        File.Delete(file);
                        await LogAsync(LogLevel.Information, $"Deleted old log file: {Path.GetFileName(file)}", cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await LogAsync(LogLevel.Warning, $"Failed to delete old log file {file}: {ex.Message}", cancellationToken: cancellationToken);
                    }
                });

            await Task.WhenAll(deleteTasks);
        }
        catch (Exception ex)
        {
            await LogAsync(LogLevel.Error, "Failed to cleanup old logs", ex, cancellationToken);
        }
    }

    public async Task<LogStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return default;

        try
        {
            var fileInfo = new FileInfo(_logFile);
            var fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0;

            return new LogStatistics(
                Interlocked.Read(ref _totalEntries),
                Interlocked.Read(ref _errorCount),
                Interlocked.Read(ref _warningCount),
                _lastLogTime,
                fileSizeBytes
            );
        }
        catch
        {
            return default;
        }
    }

    private void EnsureLogDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create log directory: {ex.Message}");
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        var logEntries = new List<LogEntry>();
        
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Collect entries for batch processing
                while (_logQueue.TryDequeue(out var entry) && logEntries.Count < 100)
                {
                    logEntries.Add(entry);
                }

                if (logEntries.Count > 0)
                {
                    await WriteLogEntriesAsync(logEntries);
                    logEntries.Clear();
                }

                await Task.Delay(100, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in log processor: {ex.Message}");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    private async Task WriteLogEntriesAsync(IReadOnlyList<LogEntry> entries)
    {
        if (_disposed || entries.Count == 0) return;

        await _fileLock.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.AppendLine(FormatLogEntry(entry));
            }

            await File.AppendAllTextAsync(_logFile, sb.ToString(), _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write log entries: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static string FormatLogEntry(LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} ");
        sb.Append($"[{entry.Level}] ");
        sb.Append($"[Thread:{entry.ThreadId}] ");
        sb.Append(entry.Message);
        
        if (!string.IsNullOrEmpty(entry.Exception))
        {
            sb.AppendLine();
            sb.Append("Exception: ");
            sb.Append(entry.Exception);
        }
        
        return sb.ToString();
    }

    private async Task LogToUIAsync(LogLevel level, string message)
    {
        try
        {
            if (Server.GlobalState.MainForm?.InvokeRequired == true)
            {
                await Task.Run(() => Server.GlobalState.MainForm.Invoke(() => LogToUIInternal(level, message)));
            }
            else
            {
                LogToUIInternal(level, message);
            }
        }
        catch
        {
            // Ignore UI logging errors
        }
    }

    private static void LogToUIInternal(LogLevel level, string message)
    {
        try
        {
            var color = level switch
            {
                LogLevel.Debug => Color.Gray,
                LogLevel.Information => Color.Green,
                LogLevel.Warning => Color.Orange,
                LogLevel.Error => Color.Red,
                LogLevel.Critical => Color.DarkRed,
                _ => Color.Black
            };

            new HandleLogs().Addmsg($"[{level}] {message}", color);
        }
        catch
        {
            // Ignore UI logging errors
        }
    }

    private void FlushLogs(object? state)
    {
        if (_disposed) return;
        
        // Force flush any remaining entries
        _ = Task.Run(async () =>
        {
            var entries = new List<LogEntry>();
            while (_logQueue.TryDequeue(out var entry))
            {
                entries.Add(entry);
            }
            
            if (entries.Count > 0)
            {
                await WriteLogEntriesAsync(entries);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _cancellationTokenSource.Cancel();
        
        // Flush remaining logs
        FlushLogs(null);
        
        _flushTimer?.Dispose();
        _fileLock?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}

/// <summary>
/// Log entry record for structured logging
/// </summary>
internal readonly record struct LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Message,
    string? Exception,
    int ThreadId
);