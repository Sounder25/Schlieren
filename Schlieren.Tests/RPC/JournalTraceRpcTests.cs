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

public sealed class JournalTraceRpcTests
{
    [Fact]
    public void Method_IsRegistered()
    {
        var (_, router) = BuildFixture();
        Assert.Contains("schlieren_traceJournal", router.GetRegisteredMethods());
    }

    [Fact]
    public async Task EphemeralCode_ReturnsJournalDtosWithoutPersistingCode()
    {
        var (state, router) = BuildFixture();
        var target = Address.FromHex("0x4000000000000000000000000000000000000004");

        var response = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_traceJournal","params":[{
              "from":"0x1000000000000000000000000000000000000001",
              "to":"0x4000000000000000000000000000000000000004",
              "gas":"0x186a0",
              "code":"0x602a60005200",
              "fork":"Osaka"
            }]}
            """);

        using var document = JsonDocument.Parse(response);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(result.GetProperty("events").EnumerateArray());
        Assert.NotEmpty(result.GetProperty("frames").EnumerateArray());
        var steps = result.GetProperty("steps");
        Assert.NotEmpty(steps.EnumerateArray());
        var first = steps[0];
        Assert.True(first.TryGetProperty("stack", out _));
        Assert.True(first.TryGetProperty("memory", out _));
        Assert.True(first.TryGetProperty("storage", out _));
        Assert.True(result.GetProperty("conservation").GetProperty("isConserved").GetBoolean());
        Assert.Empty(await state.GetCodeAsync(target));
    }

    [Fact]
    public async Task DisableFlags_OmitSelectedSnapshots()
    {
        var (_, router) = BuildFixture();
        var response = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":2,"method":"schlieren_traceJournal","params":[{
              "to":"0x4000000000000000000000000000000000000004",
              "code":"0x600100",
              "gas":"0x186a0",
              "fork":"Osaka",
              "disableStack":true,
              "disableStorage":true
            }]}
            """);

        using var document = JsonDocument.Parse(response);
        var step = document.RootElement.GetProperty("result").GetProperty("steps")[0];
        Assert.False(step.TryGetProperty("stack", out _));
        Assert.True(step.TryGetProperty("memory", out _));
        Assert.False(step.TryGetProperty("storage", out _));
    }

    [Fact]
    public async Task CodeWithoutTo_IsInvalidParams()
    {
        var (_, router) = BuildFixture();
        var response = await router.ProcessRequest(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"schlieren_traceJournal\",\"params\":[{\"code\":\"0x00\"}]}");

        using var document = JsonDocument.Parse(response);
        Assert.Equal(-32602, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task StoredNestedCall_UsesExplicitChildFrameIds()
    {
        var (state, router) = BuildFixture();
        var caller = Address.FromHex("0x5000000000000000000000000000000000000005");
        var callee = Address.FromHex("0x6000000000000000000000000000000000000006");
        state.SetCode(callee, [0x60, 0x01, 0x60, 0x00, 0x55, 0x00]);
        var code = new List<byte>
        {
            0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x73
        };
        code.AddRange(callee.Bytes);
        code.AddRange([0x61, 0xc3, 0x50, 0xf1, 0x00]);
        state.SetCode(caller, code.ToArray());

        var response = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":4,"method":"schlieren_traceJournal","params":[{
              "from":"0x1000000000000000000000000000000000000001",
              "to":"0x5000000000000000000000000000000000000005",
              "gas":"0x30d40",
              "fork":"Osaka"
            }]}
            """);

        using var document = JsonDocument.Parse(response);
        var result = document.RootElement.GetProperty("result");
        var frames = result.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(2, frames.Length);
        var rootId = frames.Single(frame => frame.GetProperty("parentId").ValueKind == JsonValueKind.Null)
            .GetProperty("id").GetInt64();
        var childId = frames.Single(frame => frame.GetProperty("parentId").ValueKind != JsonValueKind.Null)
            .GetProperty("id").GetInt64();
        Assert.Equal(rootId, frames.Single(frame => frame.GetProperty("id").GetInt64() == childId)
            .GetProperty("parentId").GetInt64());
        var sstore = result.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("op").GetString() == "SSTORE");
        Assert.Equal(childId, sstore.GetProperty("frameId").GetInt64());
    }

    private static (GlobalState State, RpcRouter Router) BuildFixture()
    {
        var state = new GlobalState();
        var sender = Address.FromHex("0x1000000000000000000000000000000000000001");
        state.SetBalance(sender, 10_000_000_000);
        var chain = new ChainState(31337, new BlockStore());
        chain.UpdateHead(new Block
        {
            Number = 10,
            Hash = "0x" + new string('a', 64),
            GasLimit = 30_000_000,
            BaseFeePerGas = 1
        });
        var machine = new EvmMachine(
        [
            new OpcodeStop(), new OpcodePush1(), new OpcodeMstore(), new OpcodeSstore(),
            new OpcodePush2(), new OpcodePush20(), new OpcodeCall(), new OpcodeRevert()
        ]);
        var handlers = new EthHandlers(
            state,
            new TxMempool(),
            chain,
            new StateTransition(machine),
            Mock.Of<IMiningService>(),
            new ImpersonationService(),
            new AccountManager(),
            new NodeConfiguration { Accounts = 0, ChainId = 31337 },
            Mock.Of<IStateManager>());
        return (state, new RpcRouter(handlers, NullLogger<RpcRouter>.Instance));
    }
}
