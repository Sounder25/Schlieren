using Microsoft.Extensions.Logging.Abstractions;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.RPC.Handlers;
using Schlieren.RPC.Server;

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
    new NoopMining(),
    new ImpersonationService(),
    new AccountManager(),
    new NodeConfiguration { Accounts = 0, ChainId = 31337 },
    new NoopState());
var router = new RpcRouter(handlers, NullLogger<RpcRouter>.Instance);

var opsec = await router.ProcessRequest(
    """{"jsonrpc":"2.0","id":1,"method":"schlieren_opsecSet","params":[{"enabled":true}]}""");
if (!opsec.Contains("\"enabled\":true", StringComparison.Ordinal))
{
    Console.WriteLine("FAIL opsec " + opsec);
    return 1;
}

var blocked = await router.ProcessRequest("""
    {"jsonrpc":"2.0","id":2,"method":"schlieren_importCode","params":[{
      "address":"0x0000000000000000000000000000000000000001",
      "provider":"https://eth.llamarpc.com"
    }]}
    """);
if (!blocked.Contains("-32042", StringComparison.Ordinal))
{
    Console.WriteLine("FAIL lockout " + blocked);
    return 2;
}

Console.WriteLine("ok opsec smoke");
return 0;

sealed class NoopMining : IMiningService
{
    public Task MineAsync(CancellationToken ct = default) => Task.CompletedTask;
}

sealed class NoopState : IStateManager
{
    public Task SaveStateAsync(string filePath) => Task.CompletedTask;
    public Task LoadStateAsync(string filePath) => Task.CompletedTask;
    public StateDumpDto CaptureState() => new();
    public void RestoreState(StateDumpDto dto) { }
}
