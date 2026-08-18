using System.Text.Json;
using System.Linq;
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

public class DebugTraceAdvancedRpcTests
{
    [Fact]
    public async Task DebugTraceCall_ReturnsDynamicStructLogs()
    {
        var (_, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"debug_traceCall\",\"params\":[{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"0x1000000000000000000000000000000000000001\",\"data\":\"0x\"},\"latest\",{}]}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        var logs = result.GetProperty("structLogs");
        Assert.True(logs.GetArrayLength() >= 1);
        Assert.Equal("PUSH1", logs[0].GetProperty("op").GetString());
        
        // B1: Verify new fields are present
        Assert.True(logs[0].TryGetProperty("gasCostDec", out var gasCostDec));
        Assert.False(string.IsNullOrEmpty(gasCostDec.GetString()));
        Assert.True(logs[0].TryGetProperty("contract", out _));
        Assert.True(logs[0].TryGetProperty("caller", out _));
        Assert.True(logs[0].TryGetProperty("callType", out _));
        Assert.True(logs[0].TryGetProperty("output", out _));
    }

    [Fact]
    public async Task DebugTraceBlockByNumber_ReturnsTracePerTransaction()
    {
        var (_, block, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            $"{{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"debug_traceBlockByNumber\",\"params\":[\"0x{block.Number:x}\"]}}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal(1, result.GetArrayLength());
        Assert.Equal("0x" + new string('a', 64), result[0].GetProperty("txHash").GetString());
        Assert.True(result[0].GetProperty("result").GetProperty("structLogs").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task DebugTraceBlockByHash_ReturnsTracePerTransaction()
    {
        var (_, block, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            $"{{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"debug_traceBlockByHash\",\"params\":[\"{block.Hash}\"]}}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal(1, result.GetArrayLength());
        Assert.Equal("0x" + new string('a', 64), result[0].GetProperty("txHash").GetString());
    }

    [Fact]
    public async Task DebugTraceTransaction_IncludesNestedDepthAndStorageDelta()
    {
        var (_, _, handlers) = BuildNestedFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"debug_traceTransaction\",\"params\":[\"0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\"]}");

        using var doc = JsonDocument.Parse(response);
        var structLogs = doc.RootElement.GetProperty("result").GetProperty("structLogs");
        Assert.True(structLogs.GetArrayLength() > 0);

        // Ensure nested call frame appears.
        var hasDepth2 = structLogs.EnumerateArray().Any(x => x.GetProperty("depth").GetInt32() == 2);
        Assert.True(hasDepth2);

        // Ensure SSTORE step carries storage delta.
        var sstoreLog = structLogs.EnumerateArray().FirstOrDefault(x => x.GetProperty("op").GetString() == "SSTORE");
        Assert.True(sstoreLog.ValueKind != JsonValueKind.Undefined);
        var storage = sstoreLog.GetProperty("storage");
        Assert.True(storage.TryGetProperty("0x0000000000000000000000000000000000000000000000000000000000000000", out var v));
        Assert.Equal("0x0000000000000000000000000000000000000000000000000000000000000001", v.GetString());
        
        // B1: Verify new fields
        Assert.True(sstoreLog.TryGetProperty("gasCostDec", out var gasCostDec));
        Assert.False(string.IsNullOrEmpty(gasCostDec.GetString()));
        Assert.True(sstoreLog.TryGetProperty("contract", out _));
        Assert.True(sstoreLog.TryGetProperty("caller", out _));
    }

    [Fact]
    public async Task DebugTraceTransaction_AppliesTraceOptions()
    {
        var (_, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"debug_traceTransaction\",\"params\":[\"0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",{\"disableStack\":true,\"disableMemory\":true,\"disableStorage\":true,\"limit\":\"0x1\"}]}");

        using var doc = JsonDocument.Parse(response);
        var structLogs = doc.RootElement.GetProperty("result").GetProperty("structLogs");
        Assert.Equal(1, structLogs.GetArrayLength());
        Assert.Empty(structLogs[0].GetProperty("stack").EnumerateArray());
        Assert.Empty(structLogs[0].GetProperty("memory").EnumerateArray());
        Assert.Empty(structLogs[0].GetProperty("storage").EnumerateObject());
    }

    [Fact]
    public async Task DebugTraceBlockByNumber_AppliesTraceOptions()
    {
        var (_, block, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            $"{{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"debug_traceBlockByNumber\",\"params\":[\"0x{block.Number:x}\",{{\"limit\":1}}]}}");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        var structLogs = result[0].GetProperty("result").GetProperty("structLogs");
        Assert.Equal(1, structLogs.GetArrayLength());
    }

    [Fact]
    public async Task DebugTraceCall_RejectsFutureBlockTag()
    {
        var (_, _, handlers) = BuildFixture();
        var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"debug_traceCall\",\"params\":[{\"from\":\"0x2000000000000000000000000000000000000002\",\"to\":\"0x1000000000000000000000000000000000000001\",\"data\":\"0x\"},\"0xffff\",{}]}");

        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Contains("greater than current head", error.GetProperty("message").GetString());
    }

    private static (GlobalState globalState, Block block, EthHandlers handlers) BuildFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);

        // PUSH1 0x01 PUSH1 0x00 SSTORE STOP
        var target = Address.FromHex("0x1000000000000000000000000000000000000001");
        globalState.SetCode(target, new byte[] { 0x60, 0x01, 0x60, 0x00, 0x55, 0x00 });

        var sender = Address.FromHex("0x2000000000000000000000000000000000000002");
        globalState.SetBalance(sender, 1_000_000_000);

        var tx = new Transaction
        {
            Hash = Convert.FromHexString(new string('a', 64)),
            From = sender,
            To = target,
            Nonce = 0,
            GasLimit = 100_000,
            GasPrice = 1,
            Authorization = TransactionAuthorization.Impersonated
        };

        var block = new Block
        {
            Number = 7,
            Hash = "0x" + new string('7', 64),
            Transactions = new List<Transaction> { tx },
            GasLimit = 30_000_000,
            BaseFeePerGas = 1
        };

        chainState.UpdateHead(block);
        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0x" + new string('a', 64),
            BlockNumber = block.Number,
            BlockHash = block.Hash,
            GasUsed = 25000,
            Status = 1,
            EffectiveGasPrice = 1
        });

        var opcodes = new List<IOpcode> { new OpcodeStop(), new OpcodePush1(), new OpcodeSstore() };
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

        return (globalState, block, handlers);
    }

    private static (GlobalState globalState, Block block, EthHandlers handlers) BuildNestedFixture()
    {
        var globalState = new GlobalState();
        var mempool = new TxMempool();
        var blockStore = new BlockStore();
        var chainState = new ChainState(31337, blockStore);

        var callee = Address.FromHex("0x4000000000000000000000000000000000000004");
        var caller = Address.FromHex("0x5000000000000000000000000000000000000005");
        var sender = Address.FromHex("0x2000000000000000000000000000000000000002");

        // Callee: PUSH1 0x01 PUSH1 0x00 SSTORE STOP
        globalState.SetCode(callee, new byte[] { 0x60, 0x01, 0x60, 0x00, 0x55, 0x00 });
        // Caller performs CALL(callee) with zero value and empty calldata/returndata.
        var callerCode = new List<byte>
        {
            0x60, 0x00, // retLength
            0x60, 0x00, // retOffset
            0x60, 0x00, // argsLength
            0x60, 0x00, // argsOffset
            0x60, 0x00, // value
            0x73        // PUSH20 callee
        };
        callerCode.AddRange(callee.Bytes);
        callerCode.AddRange(new byte[] { 0x61, 0x27, 0x10, 0xF1, 0x00 }); // gas=0x2710, CALL, STOP
        globalState.SetCode(caller, callerCode.ToArray());

        globalState.SetBalance(sender, 1_000_000_000);
        globalState.SetBalance(caller, 1_000_000);

        var tx = new Transaction
        {
            Hash = Convert.FromHexString(new string('d', 64)),
            From = sender,
            To = caller,
            Nonce = 0,
            GasLimit = 200_000,
            GasPrice = 1,
            Authorization = TransactionAuthorization.Impersonated
        };

        var block = new Block
        {
            Number = 9,
            Hash = "0x" + new string('9', 64),
            Transactions = new List<Transaction> { tx },
            GasLimit = 30_000_000,
            BaseFeePerGas = 1
        };

        chainState.UpdateHead(block);
        blockStore.AddReceipt(new TransactionReceipt
        {
            TransactionHash = "0x" + new string('d', 64),
            BlockNumber = block.Number,
            BlockHash = block.Hash,
            GasUsed = 80_000,
            Status = 1,
            EffectiveGasPrice = 1
        });

        var opcodes = new List<IOpcode>
        {
            new OpcodeStop(),
            new OpcodePush1(),
            new OpcodePush2(),
            new OpcodePush20(),
            new OpcodeSstore(),
            new OpcodeCall()
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

        return (globalState, block, handlers);
    }
}
