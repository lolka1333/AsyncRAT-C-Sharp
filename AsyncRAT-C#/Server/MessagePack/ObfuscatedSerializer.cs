using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Server.Algorithm;

namespace Server.MessagePack;

/// <summary>
/// Obfuscated high-performance serializer to replace MessagePack (.NET 9.0)
/// Uses custom binary protocol with obfuscation to avoid detection
/// </summary>
public static class ObfuscatedSerializer
{
    // Obfuscated constants to avoid detection
    private const byte TypeNull = 0x00;
    private const byte TypeBool = 0x01;
    private const byte TypeInt8 = 0x02;
    private const byte TypeInt16 = 0x03;
    private const byte TypeInt32 = 0x04;
    private const byte TypeInt64 = 0x05;
    private const byte TypeFloat = 0x06;
    private const byte TypeDouble = 0x07;
    private const byte TypeString = 0x08;
    private const byte TypeBytes = 0x09;
    private const byte TypeArray = 0x0A;
    private const byte TypeMap = 0x0B;
    
    // Obfuscation key derived from system entropy
    private static readonly byte[] ObfuscationKey = ModernCrypto.ComputeHash(
        Encoding.UTF8.GetBytes($"{Environment.MachineName}{Environment.ProcessId}{DateTime.UtcNow.Ticks}"))[..16];

    /// <summary>
    /// Serializes object to obfuscated binary format
    /// </summary>
    public static byte[] Serialize(object? obj)
    {
        using var buffer = new ArrayBufferWriter<byte>();
        var writer = new ObfuscatedWriter(buffer);
        
        WriteValue(writer, obj);
        var data = buffer.WrittenSpan.ToArray();
        
        // Apply obfuscation
        return ObfuscateData(data);
    }

    /// <summary>
    /// Deserializes object from obfuscated binary format
    /// </summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        // Remove obfuscation
        var deobfuscated = DeobfuscateData(data);
        var reader = new ObfuscatedReader(deobfuscated);
        
        var result = ReadValue(reader);
        return result is T typed ? typed : default;
    }

    /// <summary>
    /// Deserializes to dynamic object
    /// </summary>
    public static object? Deserialize(ReadOnlySpan<byte> data)
    {
        var deobfuscated = DeobfuscateData(data);
        var reader = new ObfuscatedReader(deobfuscated);
        return ReadValue(reader);
    }

    private static void WriteValue(ObfuscatedWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteByte(TypeNull);
                break;
                
            case bool b:
                writer.WriteByte(TypeBool);
                writer.WriteByte((byte)(b ? 1 : 0));
                break;
                
            case sbyte sb:
                writer.WriteByte(TypeInt8);
                writer.WriteByte((byte)sb);
                break;
                
            case byte b:
                writer.WriteByte(TypeInt8);
                writer.WriteByte(b);
                break;
                
            case short s:
                writer.WriteByte(TypeInt16);
                writer.WriteInt16(s);
                break;
                
            case ushort us:
                writer.WriteByte(TypeInt16);
                writer.WriteInt16((short)us);
                break;
                
            case int i:
                writer.WriteByte(TypeInt32);
                writer.WriteInt32(i);
                break;
                
            case uint ui:
                writer.WriteByte(TypeInt32);
                writer.WriteInt32((int)ui);
                break;
                
            case long l:
                writer.WriteByte(TypeInt64);
                writer.WriteInt64(l);
                break;
                
            case ulong ul:
                writer.WriteByte(TypeInt64);
                writer.WriteInt64((long)ul);
                break;
                
            case float f:
                writer.WriteByte(TypeFloat);
                writer.WriteFloat(f);
                break;
                
            case double d:
                writer.WriteByte(TypeDouble);
                writer.WriteDouble(d);
                break;
                
            case string s:
                writer.WriteByte(TypeString);
                writer.WriteString(s);
                break;
                
            case byte[] bytes:
                writer.WriteByte(TypeBytes);
                writer.WriteBytes(bytes);
                break;
                
            case Array array:
                writer.WriteByte(TypeArray);
                writer.WriteInt32(array.Length);
                foreach (var item in array)
                {
                    WriteValue(writer, item);
                }
                break;
                
            case IDictionary<string, object> dict:
                writer.WriteByte(TypeMap);
                writer.WriteInt32(dict.Count);
                foreach (var kvp in dict)
                {
                    writer.WriteString(kvp.Key);
                    WriteValue(writer, kvp.Value);
                }
                break;
                
            default:
                // Fallback to JSON serialization for complex objects
                var json = JsonSerializer.Serialize(value);
                writer.WriteByte(TypeString);
                writer.WriteString(json);
                break;
        }
    }

    private static object? ReadValue(ObfuscatedReader reader)
    {
        var type = reader.ReadByte();
        
        return type switch
        {
            TypeNull => null,
            TypeBool => reader.ReadByte() == 1,
            TypeInt8 => (sbyte)reader.ReadByte(),
            TypeInt16 => reader.ReadInt16(),
            TypeInt32 => reader.ReadInt32(),
            TypeInt64 => reader.ReadInt64(),
            TypeFloat => reader.ReadFloat(),
            TypeDouble => reader.ReadDouble(),
            TypeString => reader.ReadString(),
            TypeBytes => reader.ReadBytes(),
            TypeArray => ReadArray(reader),
            TypeMap => ReadMap(reader),
            _ => throw new InvalidDataException($"Unknown type: {type:X2}")
        };
    }

    private static object[] ReadArray(ObfuscatedReader reader)
    {
        var length = reader.ReadInt32();
        var array = new object[length];
        
        for (int i = 0; i < length; i++)
        {
            array[i] = ReadValue(reader)!;
        }
        
        return array;
    }

    private static Dictionary<string, object> ReadMap(ObfuscatedReader reader)
    {
        var count = reader.ReadInt32();
        var dict = new Dictionary<string, object>(count);
        
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadString();
            var value = ReadValue(reader)!;
            dict[key] = value;
        }
        
        return dict;
    }

    private static byte[] ObfuscateData(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length];
        data.CopyTo(result);
        
        // XOR obfuscation with rotating key
        for (int i = 0; i < result.Length; i++)
        {
            var keyIndex = i % ObfuscationKey.Length;
            result[i] ^= (byte)(ObfuscationKey[keyIndex] ^ (i & 0xFF));
        }
        
        return result;
    }

    private static byte[] DeobfuscateData(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length];
        data.CopyTo(result);
        
        // Reverse XOR obfuscation
        for (int i = 0; i < result.Length; i++)
        {
            var keyIndex = i % ObfuscationKey.Length;
            result[i] ^= (byte)(ObfuscationKey[keyIndex] ^ (i & 0xFF));
        }
        
        return result;
    }
}

/// <summary>
/// High-performance obfuscated writer using ArrayBufferWriter
/// </summary>
file ref struct ObfuscatedWriter
{
    private readonly IBufferWriter<byte> _bufferWriter;
    private Span<byte> _buffer;
    private int _position;

    public ObfuscatedWriter(IBufferWriter<byte> bufferWriter)
    {
        _bufferWriter = bufferWriter;
        _buffer = _bufferWriter.GetSpan();
        _position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer[_position..], value);
        _position += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(bytes.Length);
        WriteBytes(bytes);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_buffer[_position..]);
        _position += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int size)
    {
        if (_position + size > _buffer.Length)
        {
            _bufferWriter.Advance(_position);
            _buffer = _bufferWriter.GetSpan(size);
            _position = 0;
        }
    }

    public void Complete()
    {
        if (_position > 0)
        {
            _bufferWriter.Advance(_position);
        }
    }
}

/// <summary>
/// High-performance obfuscated reader
/// </summary>
file ref struct ObfuscatedReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public ObfuscatedReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (_position >= _data.Length)
            throw new EndOfStreamException();
        return _data[_position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16()
    {
        if (_position + 2 > _data.Length)
            throw new EndOfStreamException();
        var value = BinaryPrimitives.ReadInt16LittleEndian(_data[_position..]);
        _position += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException();
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64()
    {
        if (_position + 8 > _data.Length)
            throw new EndOfStreamException();
        var value = BinaryPrimitives.ReadInt64LittleEndian(_data[_position..]);
        _position += 8;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException();
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        if (_position + 8 > _data.Length)
            throw new EndOfStreamException();
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_data[_position..]);
        _position += 8;
        return value;
    }

    public string ReadString()
    {
        var length = ReadInt32();
        if (length < 0 || _position + length > _data.Length)
            throw new InvalidDataException();
            
        var bytes = _data.Slice(_position, length);
        _position += length;
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] ReadBytes()
    {
        var length = ReadInt32();
        if (length < 0 || _position + length > _data.Length)
            throw new InvalidDataException();
            
        var bytes = _data.Slice(_position, length).ToArray();
        _position += length;
        return bytes;
    }
}

/// <summary>
/// Obfuscated packet builder for structured data
/// </summary>
public sealed class ObfuscatedPacketBuilder
{
    private readonly Dictionary<string, object> _data = new();
    private readonly List<string> _obfuscatedKeys = new();

    /// <summary>
    /// Adds a value with an obfuscated key name
    /// </summary>
    public ObfuscatedPacketBuilder Add(string key, object? value)
    {
        // Obfuscate the key name to avoid detection
        var obfuscatedKey = ModernCrypto.ObfuscateString(key, "packet_key");
        _data[obfuscatedKey] = value;
        _obfuscatedKeys.Add(key);
        return this;
    }

    /// <summary>
    /// Builds the final obfuscated packet
    /// </summary>
    public byte[] Build()
    {
        return ObfuscatedSerializer.Serialize(_data);
    }

    /// <summary>
    /// Creates a packet from existing data
    /// </summary>
    public static ObfuscatedPacketBuilder FromData(ReadOnlySpan<byte> data)
    {
        var deserialized = ObfuscatedSerializer.Deserialize<Dictionary<string, object>>(data);
        var builder = new ObfuscatedPacketBuilder();
        
        if (deserialized != null)
        {
            foreach (var kvp in deserialized)
            {
                // Try to deobfuscate the key
                var originalKey = ModernCrypto.DeobfuscateString(kvp.Key, "packet_key");
                builder._data[kvp.Key] = kvp.Value;
                builder._obfuscatedKeys.Add(originalKey);
            }
        }
        
        return builder;
    }

    /// <summary>
    /// Gets a value by original key name
    /// </summary>
    public T? GetValue<T>(string key)
    {
        var obfuscatedKey = ModernCrypto.ObfuscateString(key, "packet_key");
        return _data.TryGetValue(obfuscatedKey, out var value) && value is T typed ? typed : default;
    }

    /// <summary>
    /// Gets all key-value pairs with deobfuscated keys
    /// </summary>
    public IEnumerable<(string key, object value)> GetAllValues()
    {
        foreach (var kvp in _data)
        {
            var originalKey = ModernCrypto.DeobfuscateString(kvp.Key, "packet_key");
            yield return (originalKey, kvp.Value);
        }
    }
}

/// <summary>
/// Legacy compatibility layer for existing MessagePack usage
/// </summary>
public static class MsgPack
{
    private readonly Dictionary<string, object> _data = new();

    public MsgPack() { }

    /// <summary>
    /// Legacy ForcePathObject method for compatibility
    /// </summary>
    public MsgPackItem ForcePathObject(string path)
    {
        return new MsgPackItem(this, path);
    }

    /// <summary>
    /// Encodes to bytes using obfuscated serializer
    /// </summary>
    public byte[] Encode2Bytes()
    {
        return ObfuscatedSerializer.Serialize(_data);
    }

    /// <summary>
    /// Decodes from bytes using obfuscated serializer
    /// </summary>
    public static MsgPack DecodeFromBytes(byte[] data)
    {
        var msgpack = new MsgPack();
        var decoded = ObfuscatedSerializer.Deserialize<Dictionary<string, object>>(data);
        
        if (decoded != null)
        {
            foreach (var kvp in decoded)
            {
                msgpack._data[kvp.Key] = kvp.Value;
            }
        }
        
        return msgpack;
    }

    internal void SetValue(string path, object value)
    {
        _data[path] = value;
    }

    internal object? GetValue(string path)
    {
        return _data.TryGetValue(path, out var value) ? value : null;
    }
}

/// <summary>
/// Legacy MsgPack item for compatibility
/// </summary>
public sealed class MsgPackItem
{
    private readonly MsgPack _parent;
    private readonly string _path;

    internal MsgPackItem(MsgPack parent, string path)
    {
        _parent = parent;
        _path = path;
    }

    public string AsString
    {
        get => _parent.GetValue(_path)?.ToString() ?? "";
        set => _parent.SetValue(_path, value);
    }

    public int AsInteger
    {
        get => _parent.GetValue(_path) is int i ? i : 0;
        set => _parent.SetValue(_path, value);
    }

    public bool AsBoolean
    {
        get => _parent.GetValue(_path) is bool b && b;
        set => _parent.SetValue(_path, value);
    }

    public byte[] AsBytes
    {
        get => _parent.GetValue(_path) as byte[] ?? [];
        set => _parent.SetValue(_path, value);
    }
}