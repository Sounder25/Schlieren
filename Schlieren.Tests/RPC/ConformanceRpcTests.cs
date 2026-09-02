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

public sealed class ConformanceRpcTests
{
    private static readonly string ChainIdDir = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "fixtures", "state_tests", "istanbul", "eip1344_chainid"));

    [Fact]
    public void Methods_AreRegistered()
    {
        var router = BuildRouter();
        var methods = router.GetRegisteredMethods();
        Assert.Contains("schlieren_conformancePrepare", methods);
        Assert.Contains("schlieren_conformanceStart", methods);
        Assert.Contains("schlieren_conformancePoll", methods);
        Assert.Contains("schlieren_conformanceCancel", methods);
        Assert.Contains("schlieren_conformanceReadFixture", methods);
    }

    [Fact]
    public async Task Prepare_ResolvesExistingFixtureRoot()
    {
        Assert.True(Directory.Exists(ChainIdDir), ChainIdDir);
        var router = BuildRouter();
        var response = await router.ProcessRequest($$"""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_conformancePrepare","params":[{
              "fork":"Istanbul",
              "fixturesRoot":{{JsonSerializer.Serialize(ChainIdDir)}}
            }]}
            """);
        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("valid").GetBoolean());
        Assert.True(result.GetProperty("fileCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task StartPoll_RunsBoundedSuite()
    {
        var router = BuildRouter();
        var start = await router.ProcessRequest($$"""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_conformanceStart","params":[{
              "fork":"Istanbul",
              "fixturesRoot":{{JsonSerializer.Serialize(ChainIdDir)}},
              "maxCases":1,
              "excludePortedStatic":true
            }]}
            """);
        using var startDoc = JsonDocument.Parse(start);
        var runId = startDoc.RootElement.GetProperty("result").GetProperty("runId").GetString();
        Assert.False(string.IsNullOrEmpty(runId));

        JsonElement snapshot = default;
        for (var i = 0; i < 120; i++)
        {
            var poll = await router.ProcessRequest($$"""
                {"jsonrpc":"2.0","id":2,"method":"schlieren_conformancePoll","params":[{"runId":"{{runId}}"}]}
                """);
            using var pollDoc = JsonDocument.Parse(poll);
            snapshot = pollDoc.RootElement.GetProperty("result").Clone();
            if (snapshot.GetProperty("done").GetBoolean())
                break;
            await Task.Delay(250);
        }

        Assert.True(snapshot.GetProperty("done").GetBoolean(), snapshot.GetProperty("status").GetString());
        Assert.True(snapshot.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(snapshot.GetProperty("passed").GetInt32() + snapshot.GetProperty("failed").GetInt32(),
            snapshot.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ReadFixture_ReturnsJsonForOpenInWorkbench()
    {
        var file = Directory.EnumerateFiles(ChainIdDir, "*.json").First();
        var router = BuildRouter();
        var response = await router.ProcessRequest($$"""
            {"jsonrpc":"2.0","id":1,"method":"schlieren_conformanceReadFixture","params":[{
              "path":{{JsonSerializer.Serialize(file)}}
            }]}
            """);
        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal(Path.GetFileName(file), result.GetProperty("name").GetString());
        var text = result.GetProperty("text").GetString();
        Assert.Contains("\"pre\"", text);
        Assert.Contains("\"transaction\"", text);
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
