using System;
using Xunit;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Test PUSH encoding at width-transition boundaries.
/// </summary>
public class BytecodeEncoderTests
{
    [Theory]
    [InlineData(0, "6000")]           // PUSH1 0
    [InlineData(1, "6001")]           // PUSH1 1
    [InlineData(255, "60ff")]         // PUSH1 255 (max 1-byte)
    [InlineData(256, "610100")]       // PUSH2 256 (min 2-byte)
    [InlineData(257, "610101")]       // PUSH2 257
    [InlineData(65535, "61ffff")]     // PUSH2 65535 (max 2-byte)
    [InlineData(65536, "62010000")]   // PUSH3 65536 (min 3-byte)
    [InlineData(100000, "620186a0")]  // PUSH3 100000 (our gas constant)
    public void EncodePush_BoundaryValues(ulong value, string expected)
    {
        var actual = BytecodeEncoder.EncodePushHex(value);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EncodePush_LargeValue()
    {
        // Max 32-byte value
        var max32Byte = ulong.MaxValue; // 0xffffffffffffffff = 8 bytes
        var hex = BytecodeEncoder.EncodePushHex(max32Byte);
        
        Assert.StartsWith("67", hex); // PUSH8
        Assert.Equal(18, hex.Length); // 1 opcode + 8 bytes = 9 bytes = 18 hex chars
    }
}
