using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace Server.Helper
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    public static class Logger
    {
        private static readonly object _logLock = new object();
        private static readonly string _logDirectory = Path.Combine(Application.StartupPath, "Logs");
        private static readonly string _logFile = Path.Combine(_logDirectory, $"AsyncRAT_{DateTime.Now:yyyyMMdd}.log");

        static Logger()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    Directory.CreateDirectory(_logDirectory);
            }
            catch (Exception ex)
            {
                // Fallback to console if can't create log directory
                Console.WriteLine($"Failed to create log directory: {ex.Message}");
            }
        }

        public static void Log(LogLevel level, string message, Exception exception = null)
        {
            try
            {
                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (exception != null)
                {
                    logEntry += $"\nException: {exception}";
                }

                // Thread-safe file writing
                lock (_logLock)
                {
                    File.AppendAllText(_logFile, logEntry + Environment.NewLine);
                }

                // Also log to UI if available
                LogToUI(level, message);
            }
            catch (Exception ex)
            {
                // Prevent logging errors from crashing the application
                Console.WriteLine($"Logging failed: {ex.Message}");
            }
        }

        public static void Debug(string message) => Log(LogLevel.Debug, message);
        public static void Info(string message) => Log(LogLevel.Info, message);
        public static void Warning(string message) => Log(LogLevel.Warning, message);
        public static void Error(string message, Exception exception = null) => Log(LogLevel.Error, message, exception);
        public static void Critical(string message, Exception exception = null) => Log(LogLevel.Critical, message, exception);

        private static void LogToUI(LogLevel level, string message)
        {
            try
            {
                if (Program.form1?.InvokeRequired == true)
                {
                    Program.form1.Invoke(new Action(() => LogToUIInternal(level, message)));
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
                Color color = level switch
                {
                    LogLevel.Debug => Color.Gray,
                    LogLevel.Info => Color.Green,
                    LogLevel.Warning => Color.Orange,
                    LogLevel.Error => Color.Red,
                    LogLevel.Critical => Color.DarkRed,
                    _ => Color.Black
                };

                // Use existing HandleLogs if available
                new Handle_Packet.HandleLogs().Addmsg($"[{level}] {message}", color);
            }
            catch
            {
                // Ignore UI logging errors
            }
        }

        public static async Task CleanupOldLogsAsync(int daysToKeep = 7)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (!Directory.Exists(_logDirectory)) return;

                    var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                    var logFiles = Directory.GetFiles(_logDirectory, "AsyncRAT_*.log");

                    foreach (var file in logFiles)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.CreationTime < cutoffDate)
                            {
                                fileInfo.Delete();
                                Log(LogLevel.Info, $"Deleted old log file: {fileInfo.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log(LogLevel.Warning, $"Failed to delete old log file {file}: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "Failed to cleanup old logs", ex);
            }
        }
    }
}