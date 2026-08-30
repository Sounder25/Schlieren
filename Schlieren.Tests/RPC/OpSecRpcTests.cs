using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.Security;
using Schlieren.Core.State;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Models;
using Schlieren.RPC.Server;

namespace Schlieren.Tests.RPC;

public sealed class OpSecRpcTests : IDisposable
{
    public OpSecRpcTests() => OpSecGate.SetLocked(false);

    public void Dispose() => OpSecGate.SetLocked(false);

    [Fact]
    public void Methods_AreRegistered()
    {
        var router = BuildRouter();
        var methods = router.GetRegisteredMethods();
        Assert.Contains("schlieren_opsecStatus", methods);
        Assert.Contains("schlieren_opsecSet", methods);
        Assert.Contains("schlieren_importCode", methods);
    }

    [Fact]
    public async Task Set_EnablesProcessWideLockout()
    {
        var router = BuildRouter();
        var set = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_opsecSet","params":[{"enabled":true}]}
            """);
        using var setDoc = JsonDocument.Parse(set);
        Assert.True(setDoc.RootElement.GetProperty("result").GetProperty("enabled").GetBoolean());
        Assert.True(OpSecGate.IsLocked);

        var status = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":2,"method":"schlieren_opsecStatus","params":[]}
            """);
        using var statusDoc = JsonDocument.Parse(status);
        Assert.True(statusDoc.RootElement.GetProperty("result").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task ImportCode_PublicProvider_RejectedWhenLocked()
    {
        var router = BuildRouter();
        await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_opsecSet","params":[{"enabled":true}]}
            """);
        var response = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":2,"method":"schlieren_importCode","params":[{
              "address":"0x0000000000000000000000000000000000000001",
              "provider":"https://eth.llamarpc.com"
            }]}
            """);
        using var doc = JsonDocument.Parse(response);
        Assert.Equal(JsonRpcErrorCodes.OpSecViolation, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("OpSec", doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ImportCode_Loopback_IsNotOpSecViolationWhenLocked()
    {
        var router = BuildRouter();
        await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_opsecSet","params":[{"enabled":true}]}
            """);
        var response = await router.ProcessRequest("""
            {"jsonrpc":"2.0","id":2,"method":"schlieren_importCode","params":[{
              "address":"0x0000000000000000000000000000000000000001",
              "provider":"http://127.0.0.1:9"
            }]}
            """);
        using var doc = JsonDocument.Parse(response);
        Assert.False(
            doc.RootElement.TryGetProperty("error", out var error) &&
            error.GetProperty("code").GetInt32() == JsonRpcErrorCodes.OpSecViolation,
            "loopback import must not be rejected as an OpSec violation");
    }

    private static RpcRouter BuildRouter()
    {
        var state = new GlobalState();
        var chain = new ChainState(31337, new BlockStore());
        chain.UpdateHead(new Block { Number = 1, GasLimit = 30_000_000, BaseFeePerGas = 1 });
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
        return new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);
    }
}
