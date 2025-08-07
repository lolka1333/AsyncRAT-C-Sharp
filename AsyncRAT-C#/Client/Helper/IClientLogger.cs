using Microsoft.Extensions.Logging;

namespace Client.Helper;

/// <summary>
/// Lightweight client logger interface for AsyncRAT (.NET 9.0)
/// </summary>
public interface IClientLogger
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
}