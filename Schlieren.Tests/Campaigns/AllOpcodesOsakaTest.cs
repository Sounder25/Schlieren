using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

public sealed class AllOpcodesOsakaTest
{
    private readonly ITestOutputHelper _out;
    public AllOpcodesOsakaTest(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("blockchain_test", "blockchain_tests")]
    [InlineData("blockchain_test_engine", "blockchain_tests_engine")]
    public async Task R7_EEST_AllOpcodes_Osaka(string format, string fixtureDirectory)
    {
        var fixturePath = FixturePath(fixtureDirectory);
        // ── Load fixture ──────────────────────────────────────────────────────
        Assert.True(System.IO.File.Exists(fixturePath), $"missing {format}: {fixturePath}");
        var json   = await System.IO.File.ReadAllTextAsync(fixturePath);
        using var doc  = JsonDocument.Parse(json);
        var root   = doc.RootElement;
        var caseProp = root.EnumerateObject().First();
        var test   = caseProp.Value;
        _out.WriteLine($"  format   {format}");
        _out.WriteLine($"  case     {caseProp.Name}");

        var network = test.GetProperty("network").GetString()!;
        Assert.Equal("Osaka", network);

        // ── Seed pre-state ────────────────────────────────────────────────────
        var pre   = test.GetProperty("pre");
        var state = new GlobalState();
        int preCount = 0;
        foreach (var acct in pre.EnumerateObject())
        {
            var addr    = Address.FromHex(acct.Name);
            var balance = HexToBigInt(acct.Value.GetProperty("balance").GetString()!);
            var nonce   = HexToUlong(acct.Value.GetProperty("nonce").GetString()!);
            var code    = HexToBytes(acct.Value.GetProperty("code").GetString()!);
            state.SetBalance(addr, balance);
            if (nonce > 0) state.SetNonce(addr, nonce);
            if (code.Length > 0) state.SetCode(addr, code);
            foreach (var slot in acct.Value.GetProperty("storage").EnumerateObject())
            {
                var k = HexToBigInt(slot.Name);
                var v = HexToBigInt(slot.Value.GetString()!);
                if (v != BigInteger.Zero) state.SetStorageAt(addr, k, v);
            }
            preCount++;
        }

        Assert.True(preCount > 200, $"Expected >200 pre accounts, got {preCount}");

        // Engine fixtures carry a raw signed tx, not decoded fields. Same Osaka
        // all_opcodes case lives next door in blockchain_tests with sender/to/gas.
        var txSource = test;
        JsonDocument? siblingDoc = null;
        if (!test.TryGetProperty("blocks", out _))
        {
            var siblingJson = await System.IO.File.ReadAllTextAsync(FixturePath("blockchain_tests"));
            siblingDoc = JsonDocument.Parse(siblingJson);
            txSource = siblingDoc.RootElement.EnumerateObject().First().Value;
        }

        try
        {

        // ── Build transaction ─────────────────────────────────────────────────
        var txEl  = txSource.GetProperty("blocks")[0].GetProperty("transactions")[0];
        var sender = Address.FromHex(txEl.GetProperty("sender").GetString()!);
        var to     = Address.FromHex(txEl.GetProperty("to").GetString()!);
        var gasLim = HexToUlong(txEl.GetProperty("gasLimit").GetString()!);
        var gasPrice = HexToUlong(txEl.GetProperty("gasPrice").GetString()!);
        var value  = HexToBigInt(txEl.TryGetProperty("value", out var vp) ? vp.GetString()! : "0x0");
        var data   = HexToBytes(txEl.TryGetProperty("data", out var dp) ? dp.GetString()! : "0x");
        var nonceTx = HexToUlong(txEl.GetProperty("nonce").GetString()!);

        var tx = new Transaction
        {
            From     = sender,
            To       = to,
            Value    = value,
            Data     = data,
            GasLimit = gasLim,
            GasPrice = gasPrice,
            MaxFeePerGas = gasPrice,
            MaxPriorityFeePerGas = 0,
            TxType   = 0,
            Nonce    = nonceTx,
            Authorization = TransactionAuthorization.Impersonated,
            AccessList = Array.Empty<AccessListEntry>(),
            AuthorizationList = Array.Empty<Eip7702Authorization>(),
            EnableTracing = false,
        };

        // ── Block context ─────────────────────────────────────────────────────
        JsonElement bh;
        string? parentBeaconHex;
        if (test.TryGetProperty("engineNewPayloads", out var payloads) &&
            payloads.GetArrayLength() > 0)
        {
            var payload = payloads[0];
            var parms = payload.GetProperty("params");
            bh = parms[0];
            parentBeaconHex = parms.GetArrayLength() > 2 ? parms[2].GetString() : "0x";
        }
        else
        {
            bh = test.GetProperty("blocks")[0].GetProperty("blockHeader");
            parentBeaconHex = bh.TryGetProperty("parentBeaconBlockRoot", out var pbrp)
                ? pbrp.GetString()
                : "0x";
        }

        var parentBeaconRoot = HexToBytes(parentBeaconHex ?? "0x");
        var parentHash = HexToBytes(
            bh.TryGetProperty("parentHash", out var php) ? php.GetString()! : "0x");

        var numberKey = bh.TryGetProperty("number", out _) ? "number" : "blockNumber";
        var coinbaseKey = bh.TryGetProperty("coinbase", out _) ? "coinbase" : "feeRecipient";

        var block = new BlockContext
        {
            ChainId      = 1,
            Number       = HexToUlong(bh.GetProperty(numberKey).GetString()!),
            Timestamp    = HexToUlong(bh.GetProperty("timestamp").GetString()!),
            GasLimit     = HexToUlong(bh.GetProperty("gasLimit").GetString()!),
            Coinbase     = Address.FromHex(bh.GetProperty(coinbaseKey).GetString()!),
            BaseFeePerGas = HexToUlong(bh.TryGetProperty("baseFeePerGas", out var bfp) ? bfp.GetString()! : "0x0"),
            Hash                 = parentHash,
            ParentBeaconBlockRoot = parentBeaconRoot,
            Rules        = ForkRulesFactory.For("Osaka"),
        };

        // ── Execute ───────────────────────────────────────────────────────────
        var machine  = new EvmMachine(
            typeof(IOpcode).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                         && typeof(IOpcode).IsAssignableFrom(t))
                .Select(t => (IOpcode)Activator.CreateInstance(t)!)
                .ToList());
        var pipeline = new StateTransition(machine);

        // Block prelude: EIP-4788 + EIP-2935 system calls before the transaction
        var preludeResult = new System.Collections.Generic.List<string>();
        var origApply = pipeline;
        // Run prelude with tracing
        if (block.Rules.HasEip4788BeaconRoot && block.ParentBeaconBlockRoot?.Length > 0)
            _out.WriteLine($"  prelude: EIP-4788 parentBeaconRoot={Convert.ToHexString(block.ParentBeaconBlockRoot)}");
        if (block.Rules.HasEip2935BlockHashHistory && block.Hash?.Length > 0)
            _out.WriteLine($"  prelude: EIP-2935 parentHash={Convert.ToHexString(block.Hash)}");
        await BlockPrelude.ApplyAsync(block, state, pipeline);

        ExecutionResult result;
        Exception? boom = null;
        try
        {
            result = await Task.Run(() =>
                new System.Threading.Thread(() => { }, 32 * 1024 * 1024) is var _ ?
                    pipeline.ApplyTransactionAsync(tx, state, block, commit: true).GetAwaiter().GetResult() :
                    throw new Exception());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            boom = ex;
            result = ExecutionResult.Failure(EvmError.InternalError, gasLim);
        }

        // ── Receipt check ─────────────────────────────────────────────────────
        ulong expectedGasUsed;
        bool expectedStatus;
        if (test.TryGetProperty("blocks", out var blocksEl))
        {
            expectedGasUsed = HexToUlong(
                blocksEl[0].GetProperty("receipts")[0].GetProperty("cumulativeGasUsed").GetString()!);
            expectedStatus = blocksEl[0].GetProperty("receipts")[0].GetProperty("status").GetBoolean();
        }
        else
        {
            expectedGasUsed = HexToUlong(bh.GetProperty("gasUsed").GetString()!);
            expectedStatus = true;
        }

        _out.WriteLine($"  status   expected={expectedStatus}  actual={result.IsSuccess}");
        _out.WriteLine($"  gasUsed  expected={expectedGasUsed}  actual={result.GasUsed}");
        if (boom != null)
            _out.WriteLine($"  ENGINE EXCEPTION: {boom.GetType().Name}: {boom.Message}");

        // ── postState comparison ──────────────────────────────────────────────
        var postState = test.GetProperty("postState");
        var mismatches = new List<string>();
        int checked_ = 0;

        foreach (var acct in postState.EnumerateObject())
        {
            var addr    = Address.FromHex(acct.Name);
            var expBal  = HexToBigInt(acct.Value.GetProperty("balance").GetString()!);
            var expNonce = HexToUlong(acct.Value.GetProperty("nonce").GetString()!);

            var actBal   = state.GetBalanceAsync(addr).GetAwaiter().GetResult();
            var actNonce = state.GetNonceAsync(addr).GetAwaiter().GetResult();

            if (actBal != expBal)
                mismatches.Add($"balance {acct.Name}: expected={expBal} actual={actBal}");
            if (actNonce != expNonce)
                mismatches.Add($"nonce   {acct.Name}: expected={expNonce} actual={actNonce}");

            foreach (var slot in acct.Value.GetProperty("storage").EnumerateObject())
            {
                var k    = HexToBigInt(slot.Name);
                var expV = HexToBigInt(slot.Value.GetString()!);
                var actV = state.GetStorageAtAsync(addr, k).GetAwaiter().GetResult();
                if (actV != expV)
                    mismatches.Add($"storage {acct.Name}[{slot.Name}]: expected={expV} actual={actV}");
            }
            checked_++;
        }

        // ── Report ────────────────────────────────────────────────────────────
        _out.WriteLine($"\n  postState accounts checked : {checked_}");
        _out.WriteLine($"  mismatches                 : {mismatches.Count}");
        foreach (var m in mismatches.Take(20))
            _out.WriteLine($"    ✗ {m}");

        // ── Assert ────────────────────────────────────────────────────────────
        Assert.Null(boom);
        Assert.Equal(expectedStatus, result.IsSuccess);
        Assert.Equal(expectedGasUsed, result.GasUsed);
        Assert.Empty(mismatches);
        }
        finally
        {
            siblingDoc?.Dispose();
        }
    }

    private static string FixturePath(string fixtureDirectory)
    {
        var stateTestsRoot = Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT");
        var corpusRoot = !string.IsNullOrWhiteSpace(stateTestsRoot)
            ? Directory.GetParent(Path.GetFullPath(stateTestsRoot))?.FullName
            : @"C:\projects\Schlieren\fixtures";

        return Path.Combine(
            corpusRoot ?? throw new InvalidOperationException("Fixture corpus root is unavailable"),
            fixtureDirectory,
            "for_osaka", "frontier", "opcodes", "all_opcodes", "all_opcodes.json");
    }

    // ── Hex helpers ───────────────────────────────────────────────────────────

    private static byte[] HexToBytes(string h)
    {
        if (string.IsNullOrEmpty(h) || h == "0x") return Array.Empty<byte>();
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        if (s.Length % 2 != 0) s = "0" + s;
        return Convert.FromHexString(s);
    }

    private static ulong HexToUlong(string h)
    {
        if (string.IsNullOrEmpty(h) || h is "0x" or "0x0") return 0;
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    private static BigInteger HexToBigInt(string h)
    {
        if (string.IsNullOrEmpty(h) || h is "0x" or "0x0") return BigInteger.Zero;
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        return BigInteger.Parse("0" + s, System.Globalization.NumberStyles.HexNumber);
    }
}
