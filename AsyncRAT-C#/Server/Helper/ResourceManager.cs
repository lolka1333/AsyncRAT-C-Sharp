using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Server.Connection;

namespace Server.Helper
{
    public static class ResourceManager
    {
        private static readonly ConcurrentDictionary<string, IDisposable> _managedResources = new ConcurrentDictionary<string, IDisposable>();
        private static readonly ConcurrentDictionary<string, WeakReference> _weakReferences = new ConcurrentDictionary<string, WeakReference>();
        private static readonly Timer _cleanupTimer;
        private static readonly object _cleanupLock = new object();
        private static volatile bool _isShuttingDown = false;

        static ResourceManager()
        {
            // Start cleanup timer - runs every 5 minutes
            _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            // Register for application shutdown
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
        }

        /// <summary>
        /// Registers a disposable resource for automatic management
        /// </summary>
        public static bool RegisterResource(string key, IDisposable resource)
        {
            if (string.IsNullOrEmpty(key) || resource == null)
            {
                Logger.Warning("Attempted to register null or invalid resource");
                return false;
            }

            try
            {
                // Remove existing resource if present
                if (_managedResources.TryRemove(key, out IDisposable existing))
                {
                    SafeDispose(existing, $"Replacing resource: {key}");
                }

                _managedResources.TryAdd(key, resource);
                Logger.Debug($"Registered resource: {key}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to register resource '{key}'", ex);
                return false;
            }
        }

        /// <summary>
        /// Unregisters and disposes a managed resource
        /// </summary>
        public static bool UnregisterResource(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            try
            {
                if (_managedResources.TryRemove(key, out IDisposable resource))
                {
                    SafeDispose(resource, $"Unregistering resource: {key}");
                    Logger.Debug($"Unregistered resource: {key}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to unregister resource '{key}'", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets a managed resource without removing it from management
        /// </summary>
        public static T GetResource<T>(string key) where T : class, IDisposable
        {
            if (string.IsNullOrEmpty(key))
                return null;

            try
            {
                if (_managedResources.TryGetValue(key, out IDisposable resource))
                {
                    return resource as T;
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get resource '{key}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Registers a weak reference for memory monitoring
        /// </summary>
        public static void RegisterWeakReference(string key, object target)
        {
            if (string.IsNullOrEmpty(key) || target == null)
                return;

            try
            {
                _weakReferences.AddOrUpdate(key, new WeakReference(target), (k, existing) => new WeakReference(target));
                Logger.Debug($"Registered weak reference: {key}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to register weak reference '{key}'", ex);
            }
        }

        /// <summary>
        /// Safely disposes a resource with error handling
        /// </summary>
        public static void SafeDispose(IDisposable resource, string context = null)
        {
            if (resource == null)
                return;

            try
            {
                resource.Dispose();
                if (!string.IsNullOrEmpty(context))
                {
                    Logger.Debug($"Disposed resource: {context}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error disposing resource ({context}): {ex.Message}");
            }
        }

        /// <summary>
        /// Safely closes a socket with proper error handling
        /// </summary>
        public static void SafeCloseSocket(Socket socket, string context = null)
        {
            if (socket == null)
                return;

            try
            {
                if (socket.Connected)
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error shutting down socket ({context}): {ex.Message}");
            }

            try
            {
                socket.Close();
                socket.Dispose();
                
                if (!string.IsNullOrEmpty(context))
                {
                    Logger.Debug($"Closed socket: {context}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error closing socket ({context}): {ex.Message}");
            }
        }

        /// <summary>
        /// Safely closes a stream with proper error handling
        /// </summary>
        public static void SafeCloseStream(Stream stream, string context = null)
        {
            if (stream == null)
                return;

            try
            {
                stream.Close();
                stream.Dispose();
                
                if (!string.IsNullOrEmpty(context))
                {
                    Logger.Debug($"Closed stream: {context}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error closing stream ({context}): {ex.Message}");
            }
        }

        /// <summary>
        /// Forces garbage collection and compaction
        /// </summary>
        public static void ForceGarbageCollection()
        {
            try
            {
                Logger.Debug("Forcing garbage collection");
                
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                // Compact large object heap if available (.NET 4.5.1+)
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                }
                catch
                {
                    // LOH compaction not available on this framework version
                }

                Logger.Debug("Garbage collection completed");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during garbage collection", ex);
            }
        }

        /// <summary>
        /// Gets current memory usage statistics
        /// </summary>
        public static MemoryStatistics GetMemoryStatistics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                
                return new MemoryStatistics
                {
                    WorkingSet = process.WorkingSet64,
                    PrivateMemorySize = process.PrivateMemorySize64,
                    VirtualMemorySize = process.VirtualMemorySize64,
                    ManagedMemory = GC.GetTotalMemory(false),
                    Generation0Collections = GC.CollectionCount(0),
                    Generation1Collections = GC.CollectionCount(1),
                    Generation2Collections = GC.CollectionCount(2),
                    ManagedResourceCount = _managedResources.Count,
                    WeakReferenceCount = _weakReferences.Count
                };
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get memory statistics", ex);
                return new MemoryStatistics();
            }
        }

        /// <summary>
        /// Performs cleanup of dead weak references and unused resources
        /// </summary>
        private static void PerformCleanup(object state)
        {
            if (_isShuttingDown)
                return;

            lock (_cleanupLock)
            {
                try
                {
                    Logger.Debug("Starting resource cleanup");

                    // Clean up dead weak references
                    CleanupWeakReferences();

                    // Check for disposed managed resources
                    CleanupManagedResources();

                    // Log memory statistics
                    var stats = GetMemoryStatistics();
                    Logger.Debug($"Memory: Working Set: {FormatBytes(stats.WorkingSet)}, " +
                               $"Managed: {FormatBytes(stats.ManagedMemory)}, " +
                               $"Resources: {stats.ManagedResourceCount}");

                    Logger.Debug("Resource cleanup completed");
                }
                catch (Exception ex)
                {
                    Logger.Error("Error during resource cleanup", ex);
                }
            }
        }

        private static void CleanupWeakReferences()
        {
            var deadReferences = new List<string>();

            foreach (var kvp in _weakReferences)
            {
                if (!kvp.Value.IsAlive)
                {
                    deadReferences.Add(kvp.Key);
                }
            }

            foreach (string key in deadReferences)
            {
                _weakReferences.TryRemove(key, out _);
                Logger.Debug($"Removed dead weak reference: {key}");
            }

            if (deadReferences.Count > 0)
            {
                Logger.Debug($"Cleaned up {deadReferences.Count} dead weak references");
            }
        }

        private static void CleanupManagedResources()
        {
            var disposedResources = new List<string>();

            foreach (var kvp in _managedResources)
            {
                try
                {
                    // Check if resource is a socket and if it's disposed
                    if (kvp.Value is Socket socket && !socket.Connected)
                    {
                        disposedResources.Add(kvp.Key);
                    }
                    // Check if resource is a client and if it's disconnected
                    else if (kvp.Value is Clients client && client.TcpClient?.Connected != true)
                    {
                        disposedResources.Add(kvp.Key);
                    }
                }
                catch
                {
                    // If we can't check the resource state, assume it's disposed
                    disposedResources.Add(kvp.Key);
                }
            }

            foreach (string key in disposedResources)
            {
                UnregisterResource(key);
            }

            if (disposedResources.Count > 0)
            {
                Logger.Debug($"Cleaned up {disposedResources.Count} disposed resources");
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F1} {suffixes[suffixIndex]}";
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Shutdown();
        }

        private static void OnDomainUnload(object sender, EventArgs e)
        {
            Shutdown();
        }

        /// <summary>
        /// Shuts down the resource manager and disposes all managed resources
        /// </summary>
        public static void Shutdown()
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;

            try
            {
                Logger.Info("Shutting down resource manager");

                // Stop cleanup timer
                _cleanupTimer?.Dispose();

                // Dispose all managed resources
                foreach (var kvp in _managedResources)
                {
                    SafeDispose(kvp.Value, $"Shutdown cleanup: {kvp.Key}");
                }

                _managedResources.Clear();
                _weakReferences.Clear();

                Logger.Info("Resource manager shutdown completed");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during resource manager shutdown", ex);
            }
        }

        /// <summary>
        /// Creates a disposable scope that automatically manages resources
        /// </summary>
        public static ResourceScope CreateScope(string scopeName = null)
        {
            return new ResourceScope(scopeName ?? Guid.NewGuid().ToString());
        }
    }

    public class MemoryStatistics
    {
        public long WorkingSet { get; set; }
        public long PrivateMemorySize { get; set; }
        public long VirtualMemorySize { get; set; }
        public long ManagedMemory { get; set; }
        public int Generation0Collections { get; set; }
        public int Generation1Collections { get; set; }
        public int Generation2Collections { get; set; }
        public int ManagedResourceCount { get; set; }
        public int WeakReferenceCount { get; set; }
    }

    public class ResourceScope : IDisposable
    {
        private readonly string _scopeName;
        private readonly List<IDisposable> _scopedResources;
        private bool _disposed = false;

        public ResourceScope(string scopeName)
        {
            _scopeName = scopeName;
            _scopedResources = new List<IDisposable>();
        }

        public T AddResource<T>(T resource) where T : IDisposable
        {
            if (resource != null && !_disposed)
            {
                _scopedResources.Add(resource);
            }
            return resource;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var resource in _scopedResources)
                {
                    ResourceManager.SafeDispose(resource, $"Scope: {_scopeName}");
                }

                _scopedResources.Clear();
                _disposed = true;
            }
        }
    }
}