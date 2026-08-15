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
using Xunit;

namespace Schlieren.Tests.RPC;

public class DebugInspectRpcTests
{
    [Fact]
    public async Task DebugInspect_RejectsEmptyParams()
    {
        var (globalState, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""debug_inspect"",""params"":[]}");

        using var doc = JsonDocument.Parse(response);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("Missing inspect request object", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DebugInspect_GoldenCase_FrontierCreateWithFeePairMismatch_ProvenDiagnosis()
    {
        // Golden case from InspectGoldenCase: Frontier CREATE with fee-pair mismatch
        var (globalState, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        // Setup: sender with specific balance to trigger mismatch
        var sender = Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
        var coinbase = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
        globalState.SetBalance(sender, 0xa6040); // Actual balance (vs expected 0xf4240)

        var response = await router.ProcessRequest(
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""debug_inspect"",""params"":[{
                ""from"":""0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff"",
                ""to"":null,
                ""data"":""0x6000"",
                ""gas"":""0x186a0"",
                ""gasPrice"":""0xa"",
                ""fork"":""Frontier"",
                ""coinbase"":""0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba"",
                ""mismatches"":[
                    ""balance mismatch for 0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff: expected=0xf4240, actual=0xa6040"",
                    ""balance mismatch for 0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba: expected=0x0, actual=0x4e200""
                ]
            }]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        
        // J3: Assert fee-pair PROVEN diagnosis
        Assert.True(result.TryGetProperty("diagnosis", out var diagnosis));
        Assert.True(diagnosis.TryGetProperty("root", out var root));
        
        // Must be PROVEN with fee-pair mismatch
        Assert.Equal("TX.CREATE_SURCHARGE", root.GetProperty("ruleId").GetString());
        Assert.Equal("PROVEN", root.GetProperty("grade").GetString());
        
        // Should have gas delta
        Assert.True(root.TryGetProperty("gasDelta", out var gasDelta));
        Assert.Equal(32000, gasDelta.GetInt64());
    }

    [Fact]
    public async Task DebugInspect_ReturnsInspectResultWithDiagnosis()
    {
        var (globalState, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        // Simple CREATE contract deployment
        var response = await router.ProcessRequest(
            @"{""jsonrpc"":""2.0"",""id"":1,""method"":""debug_inspect"",""params"":[{
                ""from"":""0x1000000000000000000000000000000000000001"",
                ""to"":null,
                ""data"":""0x6000"",
                ""gas"":""0x186a0"",
                ""value"":""0x0"",
                ""gasPrice"":""0xa"",
                ""fork"":""Prague""
            }]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        
        // B2: Verify inspect result structure
        Assert.True(result.TryGetProperty("ok", out var ok));
        Assert.True(ok.GetBoolean());
        
        Assert.True(result.TryGetProperty("fork", out var fork));
        Assert.Equal("Prague", fork.GetString());
        
        Assert.True(result.TryGetProperty("execution", out var execution));
        Assert.True(execution.TryGetProperty("success", out _));
        Assert.True(execution.TryGetProperty("gasUsed", out _));
        Assert.True(execution.TryGetProperty("gasLimit", out _));
        Assert.True(execution.TryGetProperty("refundCounter", out _));
        
        Assert.True(result.TryGetProperty("trace", out var trace));
        Assert.True(trace.TryGetProperty("structLogs", out var structLogs));
        Assert.True(structLogs.GetArrayLength() > 0);
        
        // Verify structLog has new fields
        var firstLog = structLogs[0];
        Assert.True(firstLog.TryGetProperty("gasCostDec", out _));
        Assert.True(firstLog.TryGetProperty("contract", out _));
        Assert.True(firstLog.TryGetProperty("caller", out _));
        
        Assert.True(result.TryGetProperty("gasTree", out var gasTree));
        Assert.True(gasTree.TryGetProperty("label", out _));
        Assert.True(gasTree.TryGetProperty("gas", out _));
        Assert.True(gasTree.TryGetProperty("totalGas", out _));
        
        Assert.True(result.TryGetProperty("diagnosis", out var diagnosis));
        Assert.True(diagnosis.TryGetProperty("fingerprint", out _));
        Assert.True(diagnosis.TryGetProperty("firstPhase", out _));
    }

    [Fact]
    public async Task DebugInspect_HandlesReverts()
    {
        var (globalState, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        // Call a contract that will revert
        var targetAddr = Address.FromHex("0x4000000000000000000000000000000000000004");
        // PUSH1 0xFF PUSH1 0x00 REVERT (revert with 1 byte of data)
        globalState.SetCode(targetAddr, new byte[] { 0x60, 0xFF, 0x60, 0x00, 0xFD });

        var response = await router.ProcessRequest(
            @"{""jsonrpc"":""2.0"",""id"":2,""method"":""debug_inspect"",""params"":[{
                ""from"":""0x1000000000000000000000000000000000000001"",
                ""to"":""0x4000000000000000000000000000000000000004"",
                ""gas"":""0x5208"",
                ""gasPrice"":""0x1"",
                ""fork"":""Prague""
            }]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        
        Assert.True(result.TryGetProperty("execution", out var execution));
        Assert.False(execution.GetProperty("success").GetBoolean());
        
        Assert.True(result.TryGetProperty("diagnosis", out var diagnosis));
        // Should have diagnosis
        Assert.True(diagnosis.TryGetProperty("fingerprint", out _));
    }

    [Fact]
    public async Task DebugInspect_RespectsForkParameter()
    {
        var (globalState, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            @"{""jsonrpc"":""2.0"",""id"":3,""method"":""debug_inspect"",""params"":[{
                ""from"":""0x1000000000000000000000000000000000000001"",
                ""to"":null,
                ""data"":""0x6000"",
                ""gas"":""0x186a0"",
                ""fork"":""Frontier""
            }]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        
        // Should indicate Frontier fork
        Assert.Equal("Frontier", result.GetProperty("fork").GetString());
    }

    private static (GlobalState globalState, EthHandlers handlers) BuildFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);

        var sender = Address.FromHex("0x1000000000000000000000000000000000000001");
        globalState.SetBalance(sender, 10_000_000_000);

        var block = new Block
        {
            Number = 10,
            Hash = "0x" + new string('a', 64),
            GasLimit = 30_000_000,
            BaseFeePerGas = 1
        };
        chainState.UpdateHead(block);

        var opcodes = new List<IOpcode> 
        { 
            new OpcodeStop(), 
            new OpcodePush1(),
            new OpcodePush2(),
            new OpcodePush20(),
            new OpcodeSstore(),
            new OpcodeCall(),
            new OpcodeRevert()
        };
        
        var stateTransition = new StateTransition(new EvmMachine(opcodes));
        var miningService = new Mock<IMiningService>();
        var impersonation = new ImpersonationService();
        var accountManager = new AccountManager();
        var stateManager = new Mock<IStateManager>();

        var handlers = new EthHandlers(
            globalState,
            mempool,
            chainState,
            stateTransition,
            miningService.Object,
            impersonation,
            accountManager,
            new NodeConfiguration { Accounts = 0, ChainId = 31337 },
            stateManager.Object);

        return (globalState, handlers);
    }
}
