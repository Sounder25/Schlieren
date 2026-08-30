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

public sealed class OsakaJournalP256Tests
{
    private static readonly Address Sender = Address.FromHex("0x1000000000000000000000000000000000000001");
    private static readonly Address Target = Address.FromHex("0x00000000000000000000000000000000000000aa");

    [Fact(DisplayName = "Osaka journal run of EIP-7951 wrapper verifies official P-256 vector")]
    public async Task OsakaFork_P256Wrapper_ReturnsOne()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var code = NormalizeHex(await File.ReadAllTextAsync(Path.Combine(root, "muscle", "osaka-eip7951-p256verify-wrapper.hex")));
        var data = NormalizeHex(await File.ReadAllTextAsync(Path.Combine(root, "muscle", "osaka-p256-valid.calldata.hex")));

        var router = BuildRouter();
        var prague = await Call(router, Request("Prague", code, data));
        Assert.False(
            prague.GetProperty("steps").EnumerateArray().Any(s =>
                s.GetProperty("op").GetString() == "P256VERIFY"),
            "Prague must not treat 0x0100 as P256VERIFY");

        var osaka = await Call(router, Request("Osaka", code, data));
        Assert.True(osaka.GetProperty("ok").GetBoolean(),
            osaka.GetProperty("execution").GetProperty("error").GetRawText());
        Assert.True(osaka.GetProperty("execution").GetProperty("success").GetBoolean());
        var last = osaka.GetProperty("steps").EnumerateArray().Last();
        var storage = last.GetProperty("storage").EnumerateObject().Select(p => p.Value.GetString() ?? "").ToArray();
        Assert.Contains(storage, v => v.Contains('1'));
        Assert.Contains(storage, v => v.Contains("20") || v.Contains("32"));
    }

    private static string Request(string fork, string code, string data) =>
        $$"""
        {
          "fork": "{{fork}}",
          "transaction": {
            "from": "{{Sender}}",
            "to": "{{Target}}",
            "gasLimit": "0x989680",
            "gasPrice": "0x3b9aca00",
            "data": "{{data}}"
          },
          "blockContext": { "baseFee": "0x0", "number": "0x1" },
          "preState": [
            { "address": "{{Sender}}", "balance": "0x56bc75e2d63100000", "code": "0x" },
            { "address": "{{Target}}", "code": "{{code}}" }
          ]
        }
        """;

    private static async Task<JsonElement> Call(RpcRouter router, string paramsJson)
    {
        var response = await router.ProcessRequest(
            """{"jsonrpc":"2.0","id":1,"method":"schlieren_traceJournal","params":[""" + paramsJson + "]}");
        using var document = JsonDocument.Parse(response);
        if (document.RootElement.TryGetProperty("error", out var error))
            Assert.Fail("RPC error: " + error.GetRawText());
        return document.RootElement.GetProperty("result").Clone();
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

    private static string NormalizeHex(string text)
    {
        var clean = text.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        return clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? clean : "0x" + clean;
    }
}
