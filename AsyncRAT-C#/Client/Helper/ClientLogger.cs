using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Client.Helper;

/// <summary>
/// Lightweight client logger implementation for AsyncRAT (.NET 9.0)
/// Optimized for minimal performance impact and stealthy operation
/// </summary>
public sealed class ClientLogger : IClientLogger, IDisposable
{
    private readonly ILogger<ClientLogger> _logger;
    private readonly SemaphoreSlim _eventLogLock = new(1, 1);
    private bool _disposed;

    public ClientLogger(ILogger<ClientLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogAsync(LogLevel level, string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            // Log to Microsoft.Extensions.Logging (console, debug, etc.)
            _logger.Log(level, exception, message);

            // In debug mode, also log to debug output
#if DEBUG
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}");
            if (exception is not null)
            {
                Debug.WriteLine($"Exception: {exception}");
            }
#endif

            // Log critical and error messages to Windows Event Log if possible
            if (level is LogLevel.Critical or LogLevel.Error)
            {
                await LogToEventLogAsync(level, message, exception, cancellationToken);
            }
        }
        catch
        {
            // Ignore logging errors to prevent cascading failures
            // Client should be stealthy and not fail due to logging issues
        }
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

    private async Task LogToEventLogAsync(LogLevel level, string message, Exception? exception, CancellationToken cancellationToken)
    {
        // Only log to event log in release mode and if we have permissions
#if !DEBUG
        try
        {
            // Check if we have admin rights before attempting to write to event log
            if (!Methods.IsAdmin()) return;

            await _eventLogLock.WaitAsync(cancellationToken);
            try
            {
                var eventLogType = level switch
                {
                    LogLevel.Critical => EventLogEntryType.Error,
                    LogLevel.Error => EventLogEntryType.Error,
                    LogLevel.Warning => EventLogEntryType.Warning,
                    _ => EventLogEntryType.Information
                };

                var logMessage = message;
                if (exception is not null)
                {
                    logMessage += $"\nException: {exception.Message}";
                }

                // Use a generic application name to avoid detection
                using var eventLog = new EventLog("Application");
                eventLog.Source = "Application";
                eventLog.WriteEntry($"[AsyncRAT Client] {logMessage}", eventLogType);
            }
            finally
            {
                _eventLogLock.Release();
            }
        }
        catch
        {
            // Silently ignore event log errors
            // Client should remain stealthy
        }
#endif
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _eventLogLock?.Dispose();
    }
}