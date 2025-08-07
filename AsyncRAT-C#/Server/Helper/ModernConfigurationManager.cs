using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace Server.Helper;

/// <summary>
/// Server configuration record for AsyncRAT (.NET 9.0)
/// </summary>
public sealed record ServerConfiguration
{
    public required string Version { get; init; } = "AsyncRAT 0.6.0 (.NET 9.0)";
    public required string CertificatePath { get; init; } = "ServerCertificate.p12";
    public string CertificatePassword { get; init; } = "";
    public int DefaultPort { get; init; } = 6606;
    public int MaxConnections { get; init; } = 500;
    public int BufferSize { get; init; } = 50 * 1024;
    public bool EnableLogging { get; init; } = true;
    public int LogRetentionDays { get; init; } = 7;
    public bool ReportWindow { get; init; } = false;
    public IReadOnlyList<string> BlockedIPs { get; init; } = [];
    public required MinerConfiguration Miner { get; init; } = new();

    /// <summary>
    /// Creates a default configuration instance
    /// </summary>
    public static ServerConfiguration CreateDefault() => new()
    {
        Version = "AsyncRAT 0.6.0 (.NET 9.0)",
        CertificatePath = Path.Combine(Application.StartupPath, "ServerCertificate.p12"),
        DefaultPort = 6606,
        MaxConnections = 500,
        BufferSize = 50 * 1024,
        EnableLogging = true,
        LogRetentionDays = 7,
        ReportWindow = false,
        BlockedIPs = [],
        Miner = new MinerConfiguration()
    };
}

/// <summary>
/// Miner configuration record
/// </summary>
public sealed record MinerConfiguration
{
    public string Pool { get; init; } = "";
    public string Wallet { get; init; } = "";
    public string Password { get; init; } = "";
    public string InjectTo { get; init; } = "";
    public string Hash { get; init; } = "";
}

/// <summary>
/// Configuration validation result
/// </summary>
public readonly record struct ValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static ValidationResult Success => new(true);
    public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// Modern configuration manager for AsyncRAT (.NET 9.0)
/// </summary>
public static class ModernConfigurationManager
{
    private static readonly Lock _configLock = new();
    private static readonly string _configPath = Path.Combine(Application.StartupPath, "config.json");
    private static readonly string _encryptedConfigPath = Path.Combine(Application.StartupPath, "config.dat");
    private static readonly byte[] _entropy = "AsyncRAT-Config-Salt-v2"u8.ToArray();
    
    private static ServerConfiguration? _currentConfig;
    private static readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

    /// <summary>
    /// Gets the current configuration, loading it if necessary
    /// </summary>
    public static ServerConfiguration Current
    {
        get
        {
            lock (_configLock)
            {
                return _currentConfig ??= LoadConfiguration();
            }
        }
    }

    /// <summary>
    /// Saves the configuration asynchronously
    /// </summary>
    public static async Task<bool> SaveConfigurationAsync(ServerConfiguration config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            // Validate configuration
            var validationResult = ValidateConfiguration(config);
            if (!validationResult.IsValid)
            {
                await LogErrorAsync($"Configuration validation failed: {validationResult.ErrorMessage}");
                return false;
            }

            using var lockScope = _configLock.EnterScope();
            
            // Serialize to JSON
            var jsonString = JsonSerializer.Serialize(config, _jsonOptions);
            
            // Encrypt and save
            var encryptedData = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(jsonString), 
                _entropy, 
                DataProtectionScope.LocalMachine);
            
            await File.WriteAllBytesAsync(_encryptedConfigPath, encryptedData, cancellationToken);
            
            // Also save unencrypted version for debugging (in debug mode only)
#if DEBUG
            await File.WriteAllTextAsync(_configPath, jsonString, cancellationToken);
#endif
            
            _currentConfig = config;
            await LogInfoAsync("Configuration saved successfully");
            return true;
        }
        catch (Exception ex)
        {
            await LogErrorAsync($"Failed to save configuration: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Loads the configuration
    /// </summary>
    public static ServerConfiguration LoadConfiguration()
    {
        try
        {
            using var lockScope = _configLock.EnterScope();
            
            // Try to load encrypted configuration first
            if (File.Exists(_encryptedConfigPath))
            {
                var config = LoadEncryptedConfiguration();
                if (config is not null)
                {
                    _ = LogInfoAsync("Configuration loaded successfully from encrypted file");
                    return config;
                }
            }

            // Fallback to unencrypted configuration (debug mode or migration)
            if (File.Exists(_configPath))
            {
                var config = LoadUnencryptedConfiguration();
                if (config is not null)
                {
                    _ = LogInfoAsync("Configuration loaded from unencrypted file");
                    
                    // Migrate to encrypted format
                    _ = Task.Run(async () => await SaveConfigurationAsync(config));
                    
                    return config;
                }
            }

            // Create default configuration
            _ = LogInfoAsync("Creating default configuration");
            var defaultConfig = ServerConfiguration.CreateDefault();
            _ = Task.Run(async () => await SaveConfigurationAsync(defaultConfig));
            return defaultConfig;
        }
        catch (Exception ex)
        {
            _ = LogErrorAsync($"Critical error loading configuration: {ex.Message}", ex);
            return ServerConfiguration.CreateDefault();
        }
    }

    /// <summary>
    /// Validates the configuration
    /// </summary>
    public static ValidationResult ValidateConfiguration(ServerConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.DefaultPort is < 1 or > 65535)
            return ValidationResult.Failure($"Invalid port number: {config.DefaultPort}");

        if (config.MaxConnections is < 1 or > 10000)
            return ValidationResult.Failure($"Invalid max connections: {config.MaxConnections}");

        if (config.BufferSize is < 1024 or > 1024 * 1024) // 1KB to 1MB
            return ValidationResult.Failure($"Invalid buffer size: {config.BufferSize}");

        if (config.LogRetentionDays is < 1 or > 365)
            return ValidationResult.Failure($"Invalid log retention days: {config.LogRetentionDays}");

        return ValidationResult.Success;
    }

    /// <summary>
    /// Reloads the configuration
    /// </summary>
    public static void ReloadConfiguration()
    {
        lock (_configLock)
        {
            _currentConfig = null;
            _ = LogInfoAsync("Configuration reloaded");
        }
    }

    /// <summary>
    /// Updates a specific setting
    /// </summary>
    public static async Task<bool> UpdateSettingAsync<T>(string settingPath, T value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingPath);

        try
        {
            using var lockScope = _configLock.EnterScope();
            
            var config = Current;
            
            // Use reflection to update the property
            var property = typeof(ServerConfiguration).GetProperty(settingPath);
            if (property?.CanWrite != true)
            {
                await LogWarningAsync($"Setting '{settingPath}' not found or not writable");
                return false;
            }

            // Create a new configuration with the updated value (records are immutable)
            var updatedConfig = config with { };
            property.SetValue(updatedConfig, value);
            
            return await SaveConfigurationAsync(updatedConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            await LogErrorAsync($"Failed to update setting '{settingPath}': {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Backs up the current configuration
    /// </summary>
    public static async Task BackupConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backupPath = Path.Combine(Application.StartupPath, $"config_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var jsonString = JsonSerializer.Serialize(Current, _jsonOptions);
            
            await File.WriteAllTextAsync(backupPath, jsonString, cancellationToken);
            await LogInfoAsync($"Configuration backed up to: {backupPath}");
        }
        catch (Exception ex)
        {
            await LogErrorAsync($"Failed to backup configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets configuration statistics
    /// </summary>
    public static async Task<ConfigurationStatistics> GetStatisticsAsync()
    {
        try
        {
            var config = Current;
            var configFileInfo = new FileInfo(_encryptedConfigPath);
            var backupFiles = Directory.GetFiles(Application.StartupPath, "config_backup_*.json");
            
            return new ConfigurationStatistics(
                config.Version,
                configFileInfo.Exists ? configFileInfo.Length : 0,
                configFileInfo.Exists ? configFileInfo.LastWriteTime : DateTime.MinValue,
                backupFiles.Length,
                config.BlockedIPs.Count
            );
        }
        catch (Exception ex)
        {
            await LogErrorAsync($"Failed to get configuration statistics: {ex.Message}", ex);
            return new ConfigurationStatistics("Unknown", 0, DateTime.MinValue, 0, 0);
        }
    }

    // Private helper methods
    private static ServerConfiguration? LoadEncryptedConfiguration()
    {
        try
        {
            var encryptedData = File.ReadAllBytes(_encryptedConfigPath);
            var decryptedData = ProtectedData.Unprotect(
                encryptedData, 
                _entropy, 
                DataProtectionScope.LocalMachine);
            
            var jsonString = Encoding.UTF8.GetString(decryptedData);
            var config = JsonSerializer.Deserialize<ServerConfiguration>(jsonString, _jsonOptions);
            
            if (config is not null && ValidateConfiguration(config).IsValid)
                return config;
        }
        catch (Exception ex)
        {
            _ = LogWarningAsync($"Failed to load encrypted configuration: {ex.Message}", ex);
        }
        
        return null;
    }

    private static ServerConfiguration? LoadUnencryptedConfiguration()
    {
        try
        {
            var jsonString = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<ServerConfiguration>(jsonString, _jsonOptions);
            
            if (config is not null && ValidateConfiguration(config).IsValid)
                return config;
        }
        catch (Exception ex)
        {
            _ = LogWarningAsync($"Failed to load unencrypted configuration: {ex.Message}", ex);
        }
        
        return null;
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    // Async logging helpers (fire and forget)
    private static async Task LogInfoAsync(string message) => 
        await Task.Run(() => Logger.Info($"[ConfigManager] {message}"));

    private static async Task LogWarningAsync(string message, Exception? exception = null) => 
        await Task.Run(() => Logger.Warning($"[ConfigManager] {message}", exception));

    private static async Task LogErrorAsync(string message, Exception? exception = null) => 
        await Task.Run(() => Logger.Error($"[ConfigManager] {message}", exception));
}

/// <summary>
/// Configuration statistics record
/// </summary>
public readonly record struct ConfigurationStatistics(
    string Version,
    long ConfigFileSizeBytes,
    DateTime LastModified,
    int BackupCount,
    int BlockedIPsCount
);

/// <summary>
/// Lock scope for using statement pattern
/// </summary>
file sealed class LockScope : IDisposable
{
    private readonly Lock _lock;
    private bool _disposed;

    public LockScope(Lock lockObj)
    {
        _lock = lockObj;
        _lock.Enter();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Exit();
            _disposed = true;
        }
    }
}

/// <summary>
/// Extension methods for Lock
/// </summary>
file static class LockExtensions
{
    public static LockScope EnterScope(this Lock lockObj) => new(lockObj);
}