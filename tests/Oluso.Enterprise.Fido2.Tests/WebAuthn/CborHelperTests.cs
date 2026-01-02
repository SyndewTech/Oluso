using FluentAssertions;
using Oluso.Enterprise.Fido2.WebAuthn;
using Xunit;

namespace Oluso.Enterprise.Fido2.Tests.WebAuthn;

/// <summary>
/// Unit tests for CBOR encoding/decoding used in WebAuthn.
/// </summary>
public class CborHelperTests
{
    #region Basic Type Tests

    [Fact]
    public void DecodeMap_WithUnsignedInteger_ReturnsCorrectValue()
    {
        // CBOR: {1: 42}
        // A1 (map of 1) 01 (int 1) 18 2A (int 42)
        var cbor = new byte[] { 0xA1, 0x01, 0x18, 0x2A };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<int>(1).Should().Be(42);
    }

    [Fact]
    public void DecodeMap_WithNegativeInteger_ReturnsCorrectValue()
    {
        // CBOR: {1: -10}
        // A1 (map of 1) 01 (int 1) 29 (negative int -10)
        var cbor = new byte[] { 0xA1, 0x01, 0x29 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<long>(1).Should().Be(-10);
    }

    [Fact]
    public void DecodeMap_WithByteString_ReturnsCorrectValue()
    {
        // CBOR: {"data": h'DEADBEEF'}
        // A1 (map of 1) 64 (text of 4) "data" 44 (bytes of 4) DEADBEEF
        var cbor = new byte[] { 0xA1, 0x64, 0x64, 0x61, 0x74, 0x61, 0x44, 0xDE, 0xAD, 0xBE, 0xEF };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<byte[]>("data").Should().BeEquivalentTo(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    }

    [Fact]
    public void DecodeMap_WithTextString_ReturnsCorrectValue()
    {
        // CBOR: {1: "hello"}
        // A1 (map of 1) 01 (int 1) 65 (text of 5) "hello"
        var cbor = new byte[] { 0xA1, 0x01, 0x65, 0x68, 0x65, 0x6C, 0x6C, 0x6F };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<string>(1).Should().Be("hello");
    }

    [Fact]
    public void DecodeMap_WithArray_ReturnsCorrectValue()
    {
        // CBOR: {1: [1, 2, 3]}
        // A1 (map of 1) 01 (int 1) 83 (array of 3) 01 02 03
        var cbor = new byte[] { 0xA1, 0x01, 0x83, 0x01, 0x02, 0x03 };

        var map = CborHelper.DecodeMap(cbor);
        var array = map.Get<object[]>(1);

        array.Should().HaveCount(3);
        array[0].Should().Be(1L);
        array[1].Should().Be(2L);
        array[2].Should().Be(3L);
    }

    [Fact]
    public void DecodeMap_WithNestedMap_ReturnsCorrectValue()
    {
        // CBOR: {1: {2: 42}}
        // A1 (map of 1) 01 (int 1) A1 (map of 1) 02 (int 2) 18 2A (int 42)
        var cbor = new byte[] { 0xA1, 0x01, 0xA1, 0x02, 0x18, 0x2A };

        var map = CborHelper.DecodeMap(cbor);
        var nested = map.Get<CborMap>(1);

        nested.Get<int>(2).Should().Be(42);
    }

    [Fact]
    public void DecodeMap_WithBoolean_ReturnsCorrectValue()
    {
        // CBOR: {1: true, 2: false}
        // A2 (map of 2) 01 (int 1) F5 (true) 02 (int 2) F4 (false)
        var cbor = new byte[] { 0xA2, 0x01, 0xF5, 0x02, 0xF4 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<bool>(1).Should().BeTrue();
        map.Get<bool>(2).Should().BeFalse();
    }

    #endregion

    #region CborMap Access Tests

    [Fact]
    public void CborMap_Get_WithStringKey_ReturnsValue()
    {
        // CBOR: {"fmt": "none"}
        // A1 (map of 1) 63 (text of 3) "fmt" 64 (text of 4) "none"
        var cbor = new byte[] { 0xA1, 0x63, 0x66, 0x6D, 0x74, 0x64, 0x6E, 0x6F, 0x6E, 0x65 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<string>("fmt").Should().Be("none");
    }

    [Fact]
    public void CborMap_Get_WithIntegerKey_ReturnsValue()
    {
        // CBOR: {-1: 2, -2: h'...', -3: h'...'}
        // This is a typical COSE key structure
        var cbor = new byte[]
        {
            0xA3, // map of 3
            0x20, // negative int -1
            0x02, // int 2 (EC2 curve)
            0x21, // negative int -2
            0x44, 0x01, 0x02, 0x03, 0x04, // bytes of 4
            0x22, // negative int -3
            0x44, 0x05, 0x06, 0x07, 0x08  // bytes of 4
        };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<int>(-1).Should().Be(2);
        map.Get<byte[]>(-2).Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        map.Get<byte[]>(-3).Should().BeEquivalentTo(new byte[] { 0x05, 0x06, 0x07, 0x08 });
    }

    [Fact]
    public void CborMap_Get_WithMissingKey_ThrowsKeyNotFoundException()
    {
        var cbor = new byte[] { 0xA1, 0x01, 0x02 }; // {1: 2}

        var map = CborHelper.DecodeMap(cbor);

        var action = () => map.Get<int>(99);

        action.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void CborMap_TryGet_WithExistingKey_ReturnsTrue()
    {
        var cbor = new byte[] { 0xA1, 0x01, 0x18, 0x2A }; // {1: 42}

        var map = CborHelper.DecodeMap(cbor);

        map.TryGet<int>(1, out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void CborMap_TryGet_WithMissingKey_ReturnsFalse()
    {
        var cbor = new byte[] { 0xA1, 0x01, 0x02 }; // {1: 2}

        var map = CborHelper.DecodeMap(cbor);

        map.TryGet<int>(99, out _).Should().BeFalse();
    }

    [Fact]
    public void CborMap_GetOptional_WithExistingKey_ReturnsValue()
    {
        // CBOR: {"name": "test"}
        var cbor = new byte[] { 0xA1, 0x64, 0x6E, 0x61, 0x6D, 0x65, 0x64, 0x74, 0x65, 0x73, 0x74 };

        var map = CborHelper.DecodeMap(cbor);

        map.GetOptional<string>("name").Should().Be("test");
    }

    [Fact]
    public void CborMap_GetOptional_WithMissingKey_ReturnsNull()
    {
        var cbor = new byte[] { 0xA1, 0x01, 0x02 }; // {1: 2}

        var map = CborHelper.DecodeMap(cbor);

        map.GetOptional<string>("missing").Should().BeNull();
    }

    #endregion

    #region Length Encoding Tests

    [Fact]
    public void DecodeMap_WithSmallLength_DecodesCorrectly()
    {
        // Value 23 (max for inline length)
        // CBOR: {1: 23}
        var cbor = new byte[] { 0xA1, 0x01, 0x17 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<int>(1).Should().Be(23);
    }

    [Fact]
    public void DecodeMap_With1ByteLength_DecodesCorrectly()
    {
        // Value 100 (requires 1 byte length indicator)
        // CBOR: {1: 100}
        var cbor = new byte[] { 0xA1, 0x01, 0x18, 0x64 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<int>(1).Should().Be(100);
    }

    [Fact]
    public void DecodeMap_With2ByteLength_DecodesCorrectly()
    {
        // Value 1000 (requires 2 byte length indicator)
        // CBOR: {1: 1000}
        var cbor = new byte[] { 0xA1, 0x01, 0x19, 0x03, 0xE8 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<int>(1).Should().Be(1000);
    }

    [Fact]
    public void DecodeMap_With4ByteLength_DecodesCorrectly()
    {
        // Value 100000 (requires 4 byte length indicator)
        // CBOR: {1: 100000}
        var cbor = new byte[] { 0xA1, 0x01, 0x1A, 0x00, 0x01, 0x86, 0xA0 };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<int>(1).Should().Be(100000);
    }

    #endregion

    #region WebAuthn-Specific Tests

    [Fact]
    public void DecodeMap_AttestationObject_ParsesFmtAndAuthData()
    {
        // Simplified attestation object structure:
        // {"fmt": "none", "attStmt": {}, "authData": <bytes>}
        // This is a minimal example - real ones are longer
        var cbor = new byte[]
        {
            0xA3, // map of 3
            0x63, 0x66, 0x6D, 0x74, // "fmt"
            0x64, 0x6E, 0x6F, 0x6E, 0x65, // "none"
            0x67, 0x61, 0x74, 0x74, 0x53, 0x74, 0x6D, 0x74, // "attStmt"
            0xA0, // empty map
            0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61, // "authData"
            0x44, 0x01, 0x02, 0x03, 0x04 // 4 bytes of data
        };

        var map = CborHelper.DecodeMap(cbor);

        map.Get<string>("fmt").Should().Be("none");
        map.Get<CborMap>("attStmt").Should().BeEmpty();
        map.Get<byte[]>("authData").Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, 0x04 });
    }

    [Fact]
    public void DecodeMap_CoseKey_ParsesEC2Key()
    {
        // COSE Key for EC2 P-256
        // {1: 2, 3: -7, -1: 1, -2: <x>, -3: <y>}
        // kty=2 (EC2), alg=-7 (ES256), crv=1 (P-256)
        var xCoord = new byte[32];
        var yCoord = new byte[32];
        Array.Fill(xCoord, (byte)0xAA);
        Array.Fill(yCoord, (byte)0xBB);

        var cbor = new List<byte>
        {
            0xA5, // map of 5
            0x01, 0x02, // 1: 2 (kty: EC2)
            0x03, 0x26, // 3: -7 (alg: ES256)
            0x20, 0x01, // -1: 1 (crv: P-256)
            0x21, 0x58, 0x20 // -2: bytes(32)
        };
        cbor.AddRange(xCoord);
        cbor.Add(0x22); // -3
        cbor.Add(0x58); // bytes
        cbor.Add(0x20); // 32
        cbor.AddRange(yCoord);

        var map = CborHelper.DecodeMap(cbor.ToArray());

        map.Get<int>(1).Should().Be(2); // kty: EC2
        map.Get<int>(3).Should().Be(-7); // alg: ES256
        map.Get<int>(-1).Should().Be(1); // crv: P-256
        map.Get<byte[]>(-2).Should().HaveCount(32).And.OnlyContain(b => b == 0xAA);
        map.Get<byte[]>(-3).Should().HaveCount(32).And.OnlyContain(b => b == 0xBB);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void DecodeMap_WithInvalidMajorType_ThrowsFormatException()
    {
        // Trying to decode a byte string as a map
        var cbor = new byte[] { 0x44, 0x01, 0x02, 0x03, 0x04 }; // byte string

        var action = () => CborHelper.DecodeMap(cbor);

        action.Should().Throw<FormatException>()
            .WithMessage("*Expected CBOR map*");
    }

    [Fact]
    public void CborMap_Get_WithWrongType_ThrowsInvalidCastException()
    {
        var cbor = new byte[] { 0xA1, 0x01, 0x65, 0x68, 0x65, 0x6C, 0x6C, 0x6F }; // {1: "hello"}

        var map = CborHelper.DecodeMap(cbor);

        var action = () => map.Get<int>(1);

        action.Should().Throw<InvalidCastException>();
    }

    #endregion

    #region ReadOnlySpan Overload Tests

    [Fact]
    public void DecodeMap_FromReadOnlySpan_Works()
    {
        var cbor = new byte[] { 0xA1, 0x01, 0x02 }; // {1: 2}

        var map = CborHelper.DecodeMap(cbor.AsSpan());

        map.Get<int>(1).Should().Be(2);
    }

    #endregion
}
