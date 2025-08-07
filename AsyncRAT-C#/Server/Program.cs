using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Server.Helper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/* 
       │ Author       : NYAN CAT
       │ Name         : AsyncRAT Simple RAT (.NET 9.0)
       │ Contact Me   : https://github.com/NYAN-x-CAT

       This program Is distributed for educational purposes only.
*/

// Configure services and logging for .NET 9.0
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<IAsyncRATLogger, AsyncRATLogger>();
    })
    .Build();

var logger = host.Services.GetRequiredService<IAsyncRATLogger>();

// Set up global exception handlers
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException += (sender, e) => HandleThreadException(e, logger);
AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleUnhandledException(e, logger);

try
{
    await logger.LogAsync(LogLevel.Information, "AsyncRAT Server starting up...");

    // Initialize application with modern .NET 9.0 approach
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    // Initialize resources asynchronously
    await InitializeResourcesAsync(logger);

    // Start log cleanup task as background service
    _ = Task.Run(async () => await logger.CleanupOldLogsAsync());

    // Create and run main form
    var form1 = new Form1();
    await logger.LogAsync(LogLevel.Information, "Main form created successfully");

    // Make form1 globally accessible (maintaining compatibility)
    Server.GlobalState.MainForm = form1;

    Application.Run(form1);
}
catch (Exception ex)
{
    await logger.LogAsync(LogLevel.Critical, "Fatal error during application startup", ex);
    await ShowCriticalErrorAsync("A critical error occurred during startup", ex);
    Environment.Exit(1);
}
finally
{
    await logger.LogAsync(LogLevel.Information, "AsyncRAT Server shutting down...");
    await CleanupResourcesAsync(logger);
}

// Local functions for .NET 9.0 style
static async Task InitializeResourcesAsync(IAsyncRATLogger logger)
{
    try
    {
        string batPath = Path.Combine(Application.StartupPath, "Fixer.bat");
        if (!File.Exists(batPath))
        {
            await File.WriteAllTextAsync(batPath, Properties.Resources.Fixer);
            await logger.LogAsync(LogLevel.Information, "Fixer.bat created successfully");
        }
        else
        {
            await logger.LogAsync(LogLevel.Debug, "Fixer.bat already exists");
        }
    }
    catch (UnauthorizedAccessException ex)
    {
        await logger.LogAsync(LogLevel.Error, 
            "Access denied when creating Fixer.bat - application may need administrator privileges", ex);
        
        MessageBox.Show(
            "Access denied when creating required files. Please run as administrator or check file permissions.",
            "Permission Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
    catch (IOException ex)
    {
        await logger.LogAsync(LogLevel.Error, "I/O error when creating Fixer.bat", ex);
        MessageBox.Show(
            "File system error when creating required files. Please check disk space and permissions.",
            "File System Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
    catch (Exception ex)
    {
        await logger.LogAsync(LogLevel.Error, "Unexpected error during resource initialization", ex);
    }
}

static async Task CleanupResourcesAsync(IAsyncRATLogger logger)
{
    try
    {
        Server.GlobalState.MainForm?.Dispose();
        await logger.LogAsync(LogLevel.Information, "Application resources cleaned up");
    }
    catch (Exception ex)
    {
        await logger.LogAsync(LogLevel.Error, "Error during resource cleanup", ex);
    }
}

static void HandleThreadException(ThreadExceptionEventArgs e, IAsyncRATLogger logger)
{
    _ = Task.Run(async () => await logger.LogAsync(LogLevel.Error, "Unhandled thread exception", e.Exception));
    
    var result = MessageBox.Show(
        $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nWould you like to continue running the application?",
        "Application Error",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Error);

    if (result == DialogResult.No)
    {
        _ = Task.Run(async () => await logger.LogAsync(LogLevel.Information, "User chose to exit application after thread exception"));
        Application.Exit();
    }
}

static void HandleUnhandledException(UnhandledExceptionEventArgs e, IAsyncRATLogger logger)
{
    var exception = e.ExceptionObject as Exception;
    _ = Task.Run(async () => await logger.LogAsync(LogLevel.Critical, "Unhandled domain exception", exception));

    if (e.IsTerminating)
    {
        _ = Task.Run(async () => await ShowCriticalErrorAsync("A fatal error occurred and the application must close", exception));
        Environment.Exit(1);
    }
}

static async Task ShowCriticalErrorAsync(string message, Exception? exception)
{
    try
    {
        var errorMessage = $"{message}\n\nError Details:\n{exception?.Message ?? "Unknown error"}";
        
        if (exception is not null)
        {
            errorMessage += $"\n\nFor technical support, please check the log files in the Logs directory.";
        }

        await Task.Run(() => MessageBox.Show(
            errorMessage,
            "Critical Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error));
    }
    catch
    {
        // If we can't even show a message box, write to console as last resort
        await Console.Out.WriteLineAsync($"CRITICAL ERROR: {message}");
        if (exception is not null)
            await Console.Out.WriteLineAsync($"Exception: {exception}");
    }
}

// Global state for backward compatibility
namespace Server
{
    public static class GlobalState
    {
        public static Form1? MainForm { get; set; }
    }
}
