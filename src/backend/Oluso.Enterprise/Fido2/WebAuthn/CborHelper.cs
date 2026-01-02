using System.Buffers.Binary;
using System.Text;

namespace Oluso.Enterprise.Fido2.WebAuthn;

/// <summary>
/// Minimal CBOR decoder for WebAuthn attestation objects and COSE keys.
/// Supports only the subset needed for WebAuthn.
/// </summary>
internal static class CborHelper
{
    public static CborMap DecodeMap(byte[] data)
    {
        var reader = new CborReader(data);
        return reader.ReadMap();
    }

    public static CborMap DecodeMap(ReadOnlySpan<byte> data)
    {
        var reader = new CborReader(data.ToArray());
        return reader.ReadMap();
    }
}

internal class CborReader
{
    private readonly byte[] _data;
    private int _position;

    public CborReader(byte[] data)
    {
        _data = data;
        _position = 0;
    }

    public int Position => _position;
    public int Remaining => _data.Length - _position;

    public CborMap ReadMap()
    {
        var (majorType, length) = ReadTypeAndLength();
        if (majorType != 5)
            throw new FormatException($"Expected CBOR map (type 5), got type {majorType}");

        var map = new CborMap();
        for (int i = 0; i < length; i++)
        {
            var key = ReadValue();
            var value = ReadValue();
            map[key] = value;
        }
        return map;
    }

    public object ReadValue()
    {
        var initialByte = _data[_position];
        var majorType = initialByte >> 5;

        return majorType switch
        {
            0 => ReadUnsignedInteger(),
            1 => ReadNegativeInteger(),
            2 => ReadByteString(),
            3 => ReadTextString(),
            4 => ReadArray(),
            5 => ReadMap(),
            6 => ReadTaggedValue(),
            7 => ReadSimpleOrFloat(),
            _ => throw new FormatException($"Unknown CBOR major type: {majorType}")
        };
    }

    private (int majorType, int length) ReadTypeAndLength()
    {
        var initialByte = _data[_position++];
        var majorType = initialByte >> 5;
        var additionalInfo = initialByte & 0x1F;

        int length;
        if (additionalInfo < 24)
        {
            length = additionalInfo;
        }
        else if (additionalInfo == 24)
        {
            length = _data[_position++];
        }
        else if (additionalInfo == 25)
        {
            length = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_position));
            _position += 2;
        }
        else if (additionalInfo == 26)
        {
            length = (int)BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_position));
            _position += 4;
        }
        else if (additionalInfo == 27)
        {
            length = (int)BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_position));
            _position += 8;
        }
        else if (additionalInfo == 31)
        {
            // Indefinite length - not commonly used in WebAuthn
            length = -1;
        }
        else
        {
            throw new FormatException($"Invalid CBOR additional info: {additionalInfo}");
        }

        return (majorType, length);
    }

    private long ReadUnsignedInteger()
    {
        var (_, length) = ReadTypeAndLength();
        return length;
    }

    private long ReadNegativeInteger()
    {
        var (_, length) = ReadTypeAndLength();
        return -1 - length;
    }

    private byte[] ReadByteString()
    {
        var (_, length) = ReadTypeAndLength();
        var result = _data.AsSpan(_position, length).ToArray();
        _position += length;
        return result;
    }

    private string ReadTextString()
    {
        var (_, length) = ReadTypeAndLength();
        var result = Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        return result;
    }

    private object[] ReadArray()
    {
        var (_, length) = ReadTypeAndLength();
        var result = new object[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = ReadValue();
        }
        return result;
    }

    private object ReadTaggedValue()
    {
        // Skip the tag and read the value
        ReadTypeAndLength();
        return ReadValue();
    }

    private object ReadSimpleOrFloat()
    {
        var initialByte = _data[_position++];
        var additionalInfo = initialByte & 0x1F;

        return additionalInfo switch
        {
            20 => false,
            21 => true,
            22 => null!,
            23 => null!, // undefined
            24 => _data[_position++], // simple value
            25 => ReadFloat16(),
            26 => ReadFloat32(),
            27 => ReadFloat64(),
            31 => null!, // break
            _ => additionalInfo // simple value 0-23
        };
    }

    private double ReadFloat16()
    {
        var bits = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_position));
        _position += 2;
        return HalfToDouble(bits);
    }

    private double ReadFloat32()
    {
        var value = BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(_position));
        _position += 4;
        return value;
    }

    private double ReadFloat64()
    {
        var value = BinaryPrimitives.ReadDoubleBigEndian(_data.AsSpan(_position));
        _position += 8;
        return value;
    }

    private static double HalfToDouble(ushort half)
    {
        var exp = (half >> 10) & 0x1F;
        var mant = half & 0x3FF;
        double val;
        if (exp == 0) val = mant * Math.Pow(2, -24);
        else if (exp != 31) val = (mant + 1024) * Math.Pow(2, exp - 25);
        else val = mant == 0 ? double.PositiveInfinity : double.NaN;
        return (half & 0x8000) != 0 ? -val : val;
    }
}

/// <summary>
/// Represents a CBOR map that can be indexed by string or integer keys
/// </summary>
internal class CborMap : Dictionary<object, object>
{
    public T Get<T>(string key)
    {
        if (TryGetValue(key, out var value))
            return ConvertValue<T>(value);
        throw new KeyNotFoundException($"Key '{key}' not found in CBOR map");
    }

    public T Get<T>(int key)
    {
        // CBOR integers might be stored as long
        if (TryGetValue((long)key, out var value))
            return ConvertValue<T>(value);
        if (TryGetValue(key, out value))
            return ConvertValue<T>(value);
        throw new KeyNotFoundException($"Key '{key}' not found in CBOR map");
    }

    public T? GetOptional<T>(string key) where T : class
    {
        if (TryGetValue(key, out var value))
            return ConvertValue<T>(value);
        return null;
    }

    public T? GetOptional<T>(int key) where T : class
    {
        if (TryGetValue((long)key, out var value))
            return ConvertValue<T>(value);
        if (TryGetValue(key, out value))
            return ConvertValue<T>(value);
        return null;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (TryGetValue(key, out var obj))
        {
            value = ConvertValue<T>(obj);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGet<T>(int key, out T? value)
    {
        if (TryGetValue((long)key, out var obj) || TryGetValue(key, out obj))
        {
            value = ConvertValue<T>(obj);
            return true;
        }
        value = default;
        return false;
    }

    private static T ConvertValue<T>(object value)
    {
        if (value is T t) return t;
        if (typeof(T) == typeof(int) && value is long l) return (T)(object)(int)l;
        if (typeof(T) == typeof(uint) && value is long l2) return (T)(object)(uint)l2;
        if (typeof(T) == typeof(CborMap) && value is CborMap map) return (T)(object)map;
        throw new InvalidCastException($"Cannot convert {value?.GetType().Name ?? "null"} to {typeof(T).Name}");
    }
}
