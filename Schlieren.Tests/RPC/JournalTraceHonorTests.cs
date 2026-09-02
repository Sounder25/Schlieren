using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Server;

namespace Schlieren.Tests.RPC;

/// <summary>
/// End-to-end honor tests: normalized journal request fields must control execution,
/// and pre-state must not persist to the node GlobalState.
/// </summary>
public sealed class JournalTraceHonorTests
{
    private static readonly Address Sender = Address.FromHex("0x1000000000000000000000000000000000000001");
    private static readonly Address Target = Address.FromHex("0x00000000000000000000000000000000000000aa");
    private static readonly Address Delegate = Address.FromHex("0x00000000000000000000000000000000000000bb");
    private static readonly Address Coinbase = Address.FromHex("0xcccccccccccccccccccccccccccccccccccccccc");

    private const string Return42 = "0x602a60005260206000f3";
    private const string ReturnFf = "0x60ff60005260206000f3";
    private const string SloadSlot1Return = "0x60015460005260206000f3";
    private const string SloadSlot0Stop = "0x60005400";
    private const string CoinbaseReturn = "0x4160005260206000f3";
    private const string ChainIdReturn = "0x4660005260206000f3";
    private const string BlobHashReturn = "0x60004960005260206000f3";
    private const string CreateStopInit = "0x00";

    [Fact]
    public async Task PreStateCodeAndStorage_DriveExecution_AndDoNotMutateGlobalState()
    {
        var (state, router) = Build();
        state.SetCode(Target, FromHex(ReturnFf));

        var result = await Call(router, Fill("""
            {
              "fork": "Berlin",
              "transaction": {
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00",
                "value": "0x0",
                "data": "0x"
              },
              "blockContext": { "baseFee": "0x0", "chainId": "0x1", "number": "0x1", "timestamp": "0x3e8" },
              "preState": [
                { "address": "{SENDER}", "nonce": "0x0", "balance": "0x56bc75e2d63100000", "code": "0x" },
                {
                  "address": "{TARGET}",
                  "nonce": "0x0",
                  "balance": "0x0",
                  "code": "{SLOAD1}",
                  "storage": { "0x1": "0x2a" }
                }
              ]
            }
            """));

        AssertSuccess(result);
        Assert.Equal(
            "0x000000000000000000000000000000000000000000000000000000000000002a",
            result.GetProperty("execution").GetProperty("returnData").GetString());
        Assert.Equal(FromHex(ReturnFf), await state.GetCodeAsync(Target));
        Assert.Equal(BigInteger.Zero, await state.GetStorageAtAsync(Target, 1));
    }

    [Fact]
    public async Task BlockContext_CoinbaseAndChainId_AreVisibleToOpcodes()
    {
        var (_, router) = Build();

        var coinbase = await Call(router, Fill("""
            {
              "fork": "Berlin",
              "transaction": {
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00"
              },
              "blockContext": {
                "coinbase": "{COINBASE}",
                "chainId": "0x7a69",
                "baseFee": "0x0",
                "number": "0x1",
                "timestamp": "0x3e8",
                "gasLimit": "0x1c9c380"
              },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                { "address": "{TARGET}", "balance": "0x0", "code": "{COINBASE_CODE}" }
              ]
            }
            """));
        AssertSuccess(coinbase);
        Assert.EndsWith("cccccccccccccccccccccccccccccccccccccccc",
            coinbase.GetProperty("execution").GetProperty("returnData").GetString());

        var chainId = await Call(router, Fill("""
            {
              "fork": "Istanbul",
              "transaction": {
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00"
              },
              "blockContext": { "chainId": "0x7a69", "baseFee": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                { "address": "{TARGET}", "balance": "0x0", "code": "{CHAINID_CODE}" }
              ]
            }
            """));
        AssertSuccess(chainId);
        Assert.Equal(
            "0x0000000000000000000000000000000000000000000000000000000000007a69",
            chainId.GetProperty("execution").GetProperty("returnData").GetString());
    }

    [Fact]
    public async Task ContractCall_DoesNotInternalError()
    {
        var (_, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Osaka",
              "transaction": {
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00"
              },
              "blockContext": { "baseFee": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                {
                  "address": "{TARGET}",
                  "code": "0x600060006000600060007300000000000000000000000000000000000000bb61fffff100"
                },
                { "address": "{DELEGATE}", "code": "0x600160020160005260206000f3" }
              ]
            }
            """));
        AssertSuccess(result);
        var ops = result.GetProperty("steps").EnumerateArray()
            .Select(s => s.GetProperty("op").GetString())
            .ToArray();
        Assert.Contains("CALL", ops);
        Assert.True(ops.Length > 8);
    }

    [Fact]
    public async Task LiteralZeroAddressTo_IsMessageCall_NotCreate()
    {
        var (_, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Osaka",
              "transaction": {
                "from": "{SENDER}",
                "to": "0x0000000000000000000000000000000000000000",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00",
                "data": "0x00"
              },
              "blockContext": { "baseFee": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                { "address": "0x0000000000000000000000000000000000000000", "code": "0x00" }
              ]
            }
            """));
        AssertSuccess(result);
        var root = result.GetProperty("frames").EnumerateArray()
            .Single(f => f.GetProperty("parentId").ValueKind == JsonValueKind.Null);
        Assert.Equal("Root", root.GetProperty("callType").GetString());
        Assert.Equal(
            "0x0000000000000000000000000000000000000000",
            root.GetProperty("contractAddress").GetString(),
            ignoreCase: true);
    }

    [Fact]
    public async Task PrestateAaCallsBb_ChildReturnAndStorage()
    {
        var (_, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Osaka",
              "transaction": {
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00"
              },
              "blockContext": { "baseFee": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                {
                  "address": "{TARGET}",
                  "code": "0x600260006000600060007300000000000000000000000000000000000000bb61fffff13d600060003e3d6000f3"
                },
                { "address": "{DELEGATE}", "code": "0x60426000556012600053603460015360026000f3" }
              ]
            }
            """));
        AssertSuccess(result);
        Assert.Contains(result.GetProperty("frames").EnumerateArray(),
            f => (f.GetProperty("contractAddress").GetString() ?? "")
                .EndsWith("bb", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("0x1234", result.GetProperty("execution").GetProperty("returnData").GetString());
    }

    [Fact]
    public async Task NestedNullTo_IsCreate()
    {
        var (state, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Berlin",
              "transaction": {
                "from": "{SENDER}",
                "to": null,
                "gasLimit": "0x30d40",
                "gasPrice": "0x3b9aca00",
                "data": "{CREATE_INIT}"
              },
              "blockContext": { "baseFee": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "nonce": "0x0", "balance": "0x56bc75e2d63100000", "code": "0x" }
              ]
            }
            """));

        AssertSuccess(result);
        var root = result.GetProperty("frames").EnumerateArray()
            .Single(f => f.GetProperty("parentId").ValueKind == JsonValueKind.Null);
        Assert.Equal("Root", root.GetProperty("callType").GetString());
        Assert.NotEqual(Target.ToString(), root.GetProperty("contractAddress").GetString());
        Assert.Empty(await state.GetCodeAsync(Target));
    }

    [Fact]
    public async Task OpcodeCreate_EmitsChildCreateFrame()
    {
        var (_, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Berlin",
              "transaction": {
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0x30d40",
                "gasPrice": "0x3b9aca00",
                "data": "0x"
              },
              "blockContext": { "baseFee": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                { "address": "{TARGET}", "balance": "0x0", "code": "0x600060006000f000" }
              ]
            }
            """));
        AssertSuccess(result);
        var types = result.GetProperty("frames").EnumerateArray()
            .Select(f => f.GetProperty("callType").GetString())
            .ToArray();
        Assert.Contains("Create", types);
    }

    [Fact]
    public async Task AccessList_WarmsStorageSlot()
    {
        var (_, router) = Build();
        var cold = await Call(router, Fill(SloadJson(withAccessList: false)));
        var warm = await Call(router, Fill(SloadJson(withAccessList: true)));
        AssertSuccess(cold);
        AssertSuccess(warm);
        Assert.Equal(2100, SloadCost(cold));
        Assert.Equal(100, SloadCost(warm));
    }

    [Fact]
    public async Task Eip7702_NormalizedAuthorization_RunsDelegateCode()
    {
        var (state, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Prague",
              "transaction": {
                "type": "0x4",
                "from": "{SENDER}",
                "to": "{SENDER}",
                "gasLimit": "0xf4240",
                "maxFeePerGas": "0x3b9aca00",
                "maxPriorityFeePerGas": "0x1",
                "value": "0x0",
                "data": "0x",
                "authorizationList": [
                  {
                    "chainId": "0x0",
                    "address": "{DELEGATE}",
                    "nonce": "0x0",
                    "signer": "{SENDER}",
                    "valid": true
                  }
                ]
              },
              "blockContext": { "chainId": "0x1", "baseFee": "0x7", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "nonce": "0x0", "balance": "0x56bc75e2d63100000", "code": "0x" },
                { "address": "{DELEGATE}", "nonce": "0x1", "balance": "0x0", "code": "{RETURN42}" }
              ]
            }
            """));

        AssertSuccess(result);
        Assert.Equal(
            "0x000000000000000000000000000000000000000000000000000000000000002a",
            result.GetProperty("execution").GetProperty("returnData").GetString());
        Assert.Empty(await state.GetCodeAsync(Sender));
    }

    [Fact]
    public async Task BlobVersionedHash_IsVisibleToBlobHashOpcode()
    {
        var hash = "0x01" + new string('a', 62);
        var (_, router) = Build();
        var result = await Call(router, Fill("""
            {
              "fork": "Cancun",
              "transaction": {
                "type": "0x3",
                "from": "{SENDER}",
                "to": "{TARGET}",
                "gasLimit": "0xf4240",
                "maxFeePerGas": "0x3b9aca00",
                "maxPriorityFeePerGas": "0x1",
                "maxFeePerBlobGas": "0x1",
                "blobVersionedHashes": ["{BLOBHASH}"],
                "data": "0x"
              },
              "blockContext": { "baseFee": "0x7", "excessBlobGas": "0x0", "number": "0x1" },
              "preState": [
                { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
                { "address": "{TARGET}", "balance": "0x0", "code": "{BLOB_CODE}" }
              ]
            }
            """.Replace("{BLOBHASH}", hash)));

        AssertSuccess(result);
        Assert.Equal(hash, result.GetProperty("execution").GetProperty("returnData").GetString());
    }

    [Fact]
    public async Task RealChainIdFixtureShape_HonorsPrestateAndBlockChainId()
    {
        var (_, router) = Build();
        var result = await Call(router, """
            {
              "fork": "Berlin",
              "transaction": {
                "from": "0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e",
                "to": "0xabe9bbd35a69090cd5e70acc90e8d161d530a307",
                "nonce": "0x0",
                "gasLimit": "0x186a0",
                "gasPrice": "0x3b9aca00",
                "value": "0x0",
                "data": "0x"
              },
              "blockContext": {
                "coinbase": "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba",
                "gasLimit": "0x07270e00",
                "number": "0x1",
                "timestamp": "0x3e8",
                "chainId": "0x1",
                "baseFee": "0x0"
              },
              "preState": [
                {
                  "address": "0xb32fd55d1dcffe091b3b2c07c06ba3308e95064e",
                  "nonce": "0x0",
                  "balance": "0x3635c9adc5dea00000",
                  "code": "0x"
                },
                {
                  "address": "0xabe9bbd35a69090cd5e70acc90e8d161d530a307",
                  "nonce": "0x1",
                  "balance": "0x0",
                  "code": "0x4660015500"
                }
              ]
            }
            """);

        AssertSuccess(result);
        var ops = result.GetProperty("steps").EnumerateArray()
            .Select(s => s.GetProperty("op").GetString())
            .ToArray();
        Assert.Contains("CHAINID", ops);
        Assert.Contains("SSTORE", ops);
    }

    private static string SloadJson(bool withAccessList) =>
        """
        {
          "fork": "Berlin",
          "transaction": {
            "from": "{SENDER}",
            "to": "{TARGET}",
            "gasLimit": "0x186a0",
            "gasPrice": "0x3b9aca00"
            {ACCESS}
          },
          "blockContext": { "baseFee": "0x0", "number": "0x1" },
          "preState": [
            { "address": "{SENDER}", "balance": "0x56bc75e2d63100000", "code": "0x" },
            { "address": "{TARGET}", "balance": "0x0", "code": "{SLOAD0}" }
          ]
        }
        """.Replace("{ACCESS}", withAccessList
            ? """, "accessList": [ { "address": "{TARGET}", "storageKeys": ["0x0"] } ]"""
            : "");

    private static long SloadCost(JsonElement result) =>
        result.GetProperty("steps").EnumerateArray()
            .Single(s => s.GetProperty("op").GetString() == "SLOAD")
            .GetProperty("gasCost").GetInt64();

    private static void AssertSuccess(JsonElement result)
    {
        Assert.True(result.GetProperty("ok").GetBoolean(),
            result.GetProperty("execution").GetProperty("error").GetRawText());
        Assert.True(result.GetProperty("execution").GetProperty("success").GetBoolean(),
            result.GetProperty("execution").GetProperty("error").GetRawText());
    }

    private static async Task<JsonElement> Call(RpcRouter router, string paramsJson)
    {
        var response = await router.ProcessRequest(
            """{"jsonrpc":"2.0","id":1,"method":"schlieren_traceJournal","params":[""" + paramsJson + "]}");
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.TryGetProperty("error", out var error))
            Assert.Fail("RPC error: " + error.GetRawText());
        return document.RootElement.GetProperty("result").Clone();
    }

    private static string Fill(string template) =>
        template
            .Replace("{SENDER}", Sender.ToString())
            .Replace("{TARGET}", Target.ToString())
            .Replace("{DELEGATE}", Delegate.ToString())
            .Replace("{COINBASE}", Coinbase.ToString())
            .Replace("{SLOAD1}", SloadSlot1Return)
            .Replace("{SLOAD0}", SloadSlot0Stop)
            .Replace("{COINBASE_CODE}", CoinbaseReturn)
            .Replace("{CHAINID_CODE}", ChainIdReturn)
            .Replace("{RETURN42}", Return42)
            .Replace("{BLOB_CODE}", BlobHashReturn)
            .Replace("{CREATE_INIT}", CreateStopInit);

    private static (GlobalState State, RpcRouter Router) Build()
    {
        var state = new GlobalState();
        state.SetBalance(Sender, 10_000_000_000);
        var chain = new ChainState(31337, new BlockStore());
        chain.UpdateHead(new Block
        {
            Number = 10,
            Hash = "0x" + new string('a', 64),
            GasLimit = 30_000_000,
            BaseFeePerGas = 1
        });
        var opcodes = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!)
            .ToList();
        var handlers = new EthHandlers(
            state,
            new TxMempool(),
            chain,
            new StateTransition(new EvmMachine(opcodes)),
            Mock.Of<IMiningService>(),
            new ImpersonationService(),
            new AccountManager(),
            new NodeConfiguration { Accounts = 0, ChainId = 31337 },
            Mock.Of<IStateManager>());
        return (state, new RpcRouter(handlers, NullLogger<RpcRouter>.Instance));
    }

    private static byte[] FromHex(string hex)
    {
        var clean = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return Convert.FromHexString(clean);
    }
}
