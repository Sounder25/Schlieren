using System.Numerics;
using System.Text.Json;
using Schlieren.Core.Primitives;
using Schlieren.RPC;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;

namespace Schlieren.Tests.RPC;

public sealed class JournalTraceRequestParserTests
{
    private const string Addr1 = "0x0000000000000000000000000000000000000001";
    private const string AddrA = "0x00000000000000000000000000000000000000aa";
    private const string AddrB = "0x00000000000000000000000000000000000000bb";
    private const ulong DefaultGas = 30_000_000;

    [Fact]
    public void Flat_MissingTo_IsInvalidParams()
    {
        var ex = Assert.Throws<RpcException>(() => Parse("""{"from":"0x0000000000000000000000000000000000000001"}"""));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("to", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nested_MissingTo_IsCreate()
    {
        var request = Parse("""
            {
              "transaction": {
                "from": "0x0000000000000000000000000000000000000001",
                "data": "0x6000",
                "gasLimit": "0x186a0"
              }
            }
            """);
        Assert.Null(request.To);
        Assert.Equal(Address.FromHex(Addr1), request.From);
        Assert.Equal(new byte[] { 0x60, 0x00 }, request.Data);
    }

    [Fact]
    public void Nested_ZeroAddressTo_IsMessageCallNotCreate()
    {
        var request = Parse("""
            {
              "transaction": {
                "from": "0x0000000000000000000000000000000000000001",
                "to": "0x0000000000000000000000000000000000000000",
                "data": "0x00"
              }
            }
            """);
        Assert.Equal(Address.Zero, request.To);
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0x0")]
    public void Nested_ShortFormTo_IsCreate(string to)
    {
        var request = Parse("""
            {
              "transaction": {
                "from": "0x0000000000000000000000000000000000000001",
                "to": "{TO}",
                "data": "0x00"
              }
            }
            """.Replace("{TO}", to));
        Assert.Null(request.To);
    }

    [Fact]
    public void Nested_NullTo_IsCreate()
    {
        var request = Parse("""
            {
              "transaction": {
                "from": "0x0000000000000000000000000000000000000001",
                "to": null,
                "data": "0x00"
              }
            }
            """);
        Assert.Null(request.To);
    }

    [Fact]
    public void NumericFork_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "fork": 1 }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("fork", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("number", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NumericTo_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse("""{ "to": 123 }"""));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("to", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NumericData_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "data": 0 }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("data", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("number", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NegativeValue_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "value": "-1" }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("unsigned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NegativeHexLikeQuantity_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "gas": "-0x10" }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("unsigned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbsentMaxFee_IsNull_ExplicitZero_IsZero()
    {
        var absent = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "gasPrice": "0xa"
              }
            }
            """);
        Assert.Null(absent.MaxFeePerGas);
        Assert.Equal(0, absent.TxType);
        var resolvedAbsent = JournalTraceRequestParser.ResolveFees(absent);
        Assert.Equal(10, resolvedAbsent.GasPrice);
        Assert.Equal(0, resolvedAbsent.MaxFee);

        var explicitZero = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "gasPrice": "0xa",
                "maxFeePerGas": "0x0"
              }
            }
            """);
        Assert.Equal(2, explicitZero.TxType);
        Assert.Equal(BigInteger.Zero, explicitZero.MaxFeePerGas);
        var resolvedZero = JournalTraceRequestParser.ResolveFees(explicitZero);
        Assert.Equal(BigInteger.Zero, resolvedZero.MaxFee);
        Assert.Equal(10, resolvedZero.GasPrice);
    }

    [Fact]
    public void AbsentPriorityFee_IsNull_ExplicitZero_IsZero()
    {
        var absent = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "maxFeePerGas": "0xa"
              }
            }
            """);
        Assert.Null(absent.MaxPriorityFeePerGas);

        var zero = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "maxFeePerGas": "0xa",
                "maxPriorityFeePerGas": "0x0"
              }
            }
            """);
        Assert.Equal(BigInteger.Zero, zero.MaxPriorityFeePerGas);
    }

    [Fact]
    public void Type2_AbsentMaxFee_InheritsGasPrice()
    {
        var request = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "gasPrice": "0xa",
                "maxPriorityFeePerGas": "0x1"
              }
            }
            """);
        Assert.Equal(2, request.TxType);
        Assert.Null(request.MaxFeePerGas);
        var resolved = JournalTraceRequestParser.ResolveFees(request);
        Assert.Equal(10, resolved.MaxFee);
        Assert.Equal(1, resolved.MaxPriority);
    }

    [Fact]
    public void GasExceedingUint64_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "gas": "0x10000000000000000" }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("uint64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueAtUint256Max_IsAccepted()
    {
        var max = "0x" + new string('f', 64);
        var request = Parse($$"""
            { "to": "{{AddrA}}", "value": "{{max}}" }
            """);
        Assert.Equal(JournalTraceRequestParser.Uint256Max, request.Value);
    }

    [Fact]
    public void ValueExceedingUint256_IsRejected()
    {
        var over = "0x1" + new string('0', 64);
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "value": "{{over}}" }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("uint256", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NegativeGasPrice_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "gasPrice": "-1" }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("unsigned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NegativeMaxFeePerGas_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "maxFeePerGas": "-1"
              }
            }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("unsigned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NegativePreStateBalance_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            {
              "transaction": { "to": "{{AddrA}}" },
              "preState": [
                {
                  "address": "{{Addr1}}",
                  "balance": "-1",
                  "code": "0x"
                }
              ]
            }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("unsigned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonNumberQuantity_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            { "to": "{{AddrA}}", "gas": 100000 }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("quantity string", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorization_ValidNormalized_IsAccepted()
    {
        var request = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "authorizationList": [
                  {
                    "chainId": "0x1",
                    "address": "{{AddrB}}",
                    "nonce": "0x0",
                    "signer": "{{Addr1}}",
                    "valid": true
                  }
                ]
              }
            }
            """);
        Assert.Equal(4, request.TxType);
        var auth = Assert.Single(request.AuthorizationList);
        Assert.True(auth.IsValid);
        Assert.Equal(Address.FromHex(Addr1), auth.Signer);
        Assert.Equal(Address.FromHex(AddrB), auth.DelegateAddress);
        Assert.Equal(1UL, auth.ChainId);
        Assert.Equal(0UL, auth.Nonce);
    }

    [Fact]
    public void Authorization_ExplicitInvalid_WithSigner_RemainsInvalid()
    {
        var request = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "authorizationList": [
                  {
                    "chainId": "0x1",
                    "address": "{{AddrB}}",
                    "nonce": "0x0",
                    "signer": "{{Addr1}}",
                    "valid": false
                  }
                ]
              }
            }
            """);
        var auth = Assert.Single(request.AuthorizationList);
        Assert.False(auth.IsValid);
        Assert.Equal(Address.FromHex(Addr1), auth.Signer);
    }

    [Fact]
    public void Authorization_ExplicitInvalid_DoesNotRequireSigner()
    {
        var request = Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "authorizationList": [
                  {
                    "chainId": "0x0",
                    "address": "{{AddrB}}",
                    "nonce": "0x1",
                    "valid": false
                  }
                ]
              }
            }
            """);
        var auth = Assert.Single(request.AuthorizationList);
        Assert.False(auth.IsValid);
        Assert.Equal(Address.Zero, auth.Signer);
    }

    [Fact]
    public void Authorization_RawSignatureWithoutSigner_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "authorizationList": [
                  {
                    "address": "{{AddrB}}",
                    "yParity": "0x0",
                    "r": "0x01",
                    "s": "0x02"
                  }
                ]
              }
            }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("signer", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not decode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorization_ValidTrueWithoutSigner_IsRejected()
    {
        var ex = Assert.Throws<RpcException>(() => Parse($$"""
            {
              "transaction": {
                "to": "{{AddrA}}",
                "authorizationList": [
                  {
                    "address": "{{AddrB}}",
                    "valid": true
                  }
                ]
              }
            }
            """));
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.ErrorCode);
        Assert.Contains("signer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JournalTraceRequest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JournalTraceRequestParser.Parse([document.RootElement], DefaultGas);
    }
}
