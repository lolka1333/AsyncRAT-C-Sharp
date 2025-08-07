using Microsoft.Extensions.Logging;

namespace Server.Helper;

/// <summary>
/// Modern async logger interface for AsyncRAT with .NET 9.0 features
/// </summary>
public interface IAsyncRATLogger
{
    /// <summary>
    /// Logs a message asynchronously with the specified log level
    /// </summary>
    Task LogAsync(LogLevel level, string message, Exception? exception = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs debug information (only in debug builds)
    /// </summary>
    Task DebugAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs informational messages
    /// </summary>
    Task InfoAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs warning messages
    /// </summary>
    Task WarningAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs error messages
    /// </summary>
    Task ErrorAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs critical error messages
    /// </summary>
    Task CriticalAsync(string message, Exception? exception = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old log files asynchronously
    /// </summary>
    Task CleanupOldLogsAsync(int daysToKeep = 7, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current log statistics
    /// </summary>
    Task<LogStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Log statistics for monitoring
/// </summary>
public readonly record struct LogStatistics(
    long TotalEntries,
    long ErrorCount,
    long WarningCount,
    DateTime LastLogTime,
    long LogFileSizeBytes
);