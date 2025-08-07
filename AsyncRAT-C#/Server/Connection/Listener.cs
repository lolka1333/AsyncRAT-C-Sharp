using System.Net;
using System.Net.Sockets;
using System;
using System.Windows.Forms;
using System.Drawing;
using Server.Handle_Packet;
using System.Diagnostics;
using Server.Helper;
using System.Threading.Tasks;

namespace Server.Connection
{
    class Listener : IDisposable
    {
        private Socket Server { get; set; }
        private bool _isDisposed = false;
        private bool _isListening = false;

        public bool IsListening => _isListening && Server?.IsBound == true;

        public async Task<bool> StartAsync(int port)
        {
            try
            {
                if (_isListening)
                {
                    Logger.Warning($"Listener is already running on port {port}");
                    return false;
                }

                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, port);
                Server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    SendBufferSize = 50 * 1024,
                    ReceiveBufferSize = 50 * 1024,
                };

                // Enable socket reuse
                Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                
                Server.Bind(ipEndPoint);
                Server.Listen(500);
                
                _isListening = true;
                Logger.Info($"Server listening on port {port}");
                
                // Start accepting connections asynchronously
                await Task.Run(() => Server.BeginAccept(EndAccept, null));
                
                return true;
            }
            catch (SocketException ex)
            {
                Logger.Error($"Socket error starting listener on port {port}: {ex.Message} (Error Code: {ex.ErrorCode})", ex);
                
                // Show user-friendly error messages
                string userMessage = ex.ErrorCode switch
                {
                    10048 => $"Port {port} is already in use. Please choose a different port.",
                    10013 => $"Access denied. You may need administrator privileges to bind to port {port}.",
                    _ => $"Failed to start server on port {port}: {ex.Message}"
                };
                
                MessageBox.Show(userMessage, "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Critical($"Unexpected error starting listener on port {port}", ex);
                MessageBox.Show($"Unexpected error starting server: {ex.Message}", "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        [Obsolete("Use StartAsync instead")]
        public void Connect(object port)
        {
            // Keep for backward compatibility but use new async method
            Task.Run(async () => await StartAsync(Convert.ToInt32(port)));
        }

        public void Stop()
        {
            try
            {
                if (_isListening)
                {
                    _isListening = false;
                    Server?.Close();
                    Logger.Info("Server listener stopped");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error stopping server listener", ex);
            }
        }

        private void EndAccept(IAsyncResult ar)
        {
            Socket clientSocket = null;
            try
            {
                if (!_isListening || Server == null || _isDisposed)
                    return;

                clientSocket = Server.EndAccept(ar);
                
                if (clientSocket != null && clientSocket.Connected)
                {
                    // Log new connection
                    string clientEndPoint = clientSocket.RemoteEndPoint?.ToString() ?? "Unknown";
                    Logger.Info($"New client connection from: {clientEndPoint}");
                    
                    // Create new client handler
                    var client = new Clients(clientSocket);
                }
            }
            catch (ObjectDisposedException)
            {
                // Server socket was disposed, this is expected during shutdown
                Logger.Debug("Server socket disposed during EndAccept");
                return;
            }
            catch (SocketException ex)
            {
                Logger.Warning($"Socket error accepting client connection: {ex.Message} (Error Code: {ex.ErrorCode})");
                
                // Try to close the problematic client socket
                try
                {
                    clientSocket?.Close();
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Error("Unexpected error accepting client connection", ex);
                
                // Try to close the problematic client socket
                try
                {
                    clientSocket?.Close();
                }
                catch { }
            }
            finally
            {
                // Continue accepting new connections if still listening
                try
                {
                    if (_isListening && Server != null && !_isDisposed)
                    {
                        Server.BeginAccept(EndAccept, null);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    Logger.Error("Error continuing to accept connections", ex);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    Stop();
                    Server?.Dispose();
                }
                _isDisposed = true;
            }
        }

        ~Listener()
        {
            Dispose(false);
        }
    }
}