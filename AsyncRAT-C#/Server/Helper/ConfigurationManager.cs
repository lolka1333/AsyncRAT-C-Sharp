using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;

namespace Server.Helper
{
    public class ServerConfiguration
    {
        public string Version { get; set; } = "AsyncRAT 0.5.8";
        public string CertificatePath { get; set; } = "ServerCertificate.p12";
        public string CertificatePassword { get; set; } = "";
        public int DefaultPort { get; set; } = 6606;
        public int MaxConnections { get; set; } = 500;
        public int BufferSize { get; set; } = 50 * 1024;
        public bool EnableLogging { get; set; } = true;
        public int LogRetentionDays { get; set; } = 7;
        public bool ReportWindow { get; set; } = false;
        public string[] BlockedIPs { get; set; } = Array.Empty<string>();
        public MinerConfiguration Miner { get; set; } = new MinerConfiguration();
    }

    public class MinerConfiguration
    {
        public string Pool { get; set; } = "";
        public string Wallet { get; set; } = "";
        public string Password { get; set; } = "";
        public string InjectTo { get; set; } = "";
        public string Hash { get; set; } = "";
    }

    public static class ConfigurationManager
    {
        private static readonly object _configLock = new object();
        private static readonly string _configPath = Path.Combine(Application.StartupPath, "config.json");
        private static readonly string _encryptedConfigPath = Path.Combine(Application.StartupPath, "config.dat");
        private static ServerConfiguration _currentConfig;
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("AsyncRAT-Config-Salt");

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

        public static bool SaveConfiguration(ServerConfiguration config)
        {
            try
            {
                lock (_configLock)
                {
                    // Validate configuration
                    if (!ValidateConfiguration(config))
                    {
                        Logger.Error("Configuration validation failed");
                        return false;
                    }

                    // Serialize to JSON
                    var options = new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    
                    string jsonString = JsonSerializer.Serialize(config, options);
                    
                    // Encrypt and save
                    byte[] encryptedData = ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(jsonString), 
                        _entropy, 
                        DataProtectionScope.LocalMachine);
                    
                    File.WriteAllBytes(_encryptedConfigPath, encryptedData);
                    
                    // Also save unencrypted version for debugging (in debug mode only)
#if DEBUG
                    File.WriteAllText(_configPath, jsonString);
#endif
                    
                    _currentConfig = config;
                    Logger.Info("Configuration saved successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save configuration", ex);
                return false;
            }
        }

        public static ServerConfiguration LoadConfiguration()
        {
            try
            {
                lock (_configLock)
                {
                    // Try to load encrypted configuration first
                    if (File.Exists(_encryptedConfigPath))
                    {
                        try
                        {
                            byte[] encryptedData = File.ReadAllBytes(_encryptedConfigPath);
                            byte[] decryptedData = ProtectedData.Unprotect(
                                encryptedData, 
                                _entropy, 
                                DataProtectionScope.LocalMachine);
                            
                            string jsonString = Encoding.UTF8.GetString(decryptedData);
                            var config = JsonSerializer.Deserialize<ServerConfiguration>(jsonString, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                PropertyNameCaseInsensitive = true
                            });

                            if (ValidateConfiguration(config))
                            {
                                Logger.Info("Configuration loaded successfully from encrypted file");
                                return config;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Failed to load encrypted configuration, trying fallback", ex);
                        }
                    }

                    // Fallback to unencrypted configuration (debug mode or migration)
                    if (File.Exists(_configPath))
                    {
                        try
                        {
                            string jsonString = File.ReadAllText(_configPath);
                            var config = JsonSerializer.Deserialize<ServerConfiguration>(jsonString, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                PropertyNameCaseInsensitive = true
                            });

                            if (ValidateConfiguration(config))
                            {
                                Logger.Info("Configuration loaded from unencrypted file");
                                
                                // Migrate to encrypted format
                                SaveConfiguration(config);
                                
                                return config;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Failed to load unencrypted configuration", ex);
                        }
                    }

                    // Create default configuration
                    Logger.Info("Creating default configuration");
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig);
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Critical error loading configuration", ex);
                return CreateDefaultConfiguration();
            }
        }

        private static bool ValidateConfiguration(ServerConfiguration config)
        {
            if (config == null)
            {
                Logger.Error("Configuration is null");
                return false;
            }

            if (config.DefaultPort < 1 || config.DefaultPort > 65535)
            {
                Logger.Error($"Invalid port number: {config.DefaultPort}");
                return false;
            }

            if (config.MaxConnections < 1 || config.MaxConnections > 10000)
            {
                Logger.Error($"Invalid max connections: {config.MaxConnections}");
                return false;
            }

            if (config.BufferSize < 1024 || config.BufferSize > 1024 * 1024) // 1KB to 1MB
            {
                Logger.Error($"Invalid buffer size: {config.BufferSize}");
                return false;
            }

            if (config.LogRetentionDays < 1 || config.LogRetentionDays > 365)
            {
                Logger.Error($"Invalid log retention days: {config.LogRetentionDays}");
                return false;
            }

            return true;
        }

        private static ServerConfiguration CreateDefaultConfiguration()
        {
            return new ServerConfiguration
            {
                CertificatePath = Path.Combine(Application.StartupPath, "ServerCertificate.p12"),
                DefaultPort = 6606,
                MaxConnections = 500,
                BufferSize = 50 * 1024,
                EnableLogging = true,
                LogRetentionDays = 7,
                ReportWindow = false,
                BlockedIPs = Array.Empty<string>(),
                Miner = new MinerConfiguration()
            };
        }

        public static void ReloadConfiguration()
        {
            lock (_configLock)
            {
                _currentConfig = null;
                Logger.Info("Configuration reloaded");
            }
        }

        public static bool UpdateSetting<T>(string settingPath, T value)
        {
            try
            {
                lock (_configLock)
                {
                    var config = Current;
                    
                    // Simple property update using reflection
                    var property = typeof(ServerConfiguration).GetProperty(settingPath);
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(config, value);
                        return SaveConfiguration(config);
                    }
                    
                    Logger.Warning($"Setting '{settingPath}' not found or not writable");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update setting '{settingPath}'", ex);
                return false;
            }
        }

        public static void BackupConfiguration()
        {
            try
            {
                string backupPath = Path.Combine(Application.StartupPath, $"config_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                string jsonString = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(backupPath, jsonString);
                
                Logger.Info($"Configuration backed up to: {backupPath}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to backup configuration", ex);
            }
        }
    }
}