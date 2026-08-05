using System.Numerics;
using System.Text;
using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Conformance;

/// <summary>
/// EELS Single-Case Tracer — Isolation Harness
/// =============================================
/// Runs ONE fixture case in complete isolation and emits:
///   • Full EIP-3155 structLog (every step: PC, opcode, gas, gasCost, stack, memory, storage)
///   • Gas accounting breakdown (intrinsic + EVM + refund counter + sender delta + coinbase delta)
///   • State diff (pre vs post for every account that changed)
///   • Pass/fail verdict with all mismatch details
///
/// Configuration via environment variables:
///
///   EELS_FIXTURES_ROOT   — path to a directory containing one or more fixture .json files
///   EELS_REQUIRED_FORK   — fork name (default: Cancun)
///   EELS_CASE_FILTER     — substring match on case_id (e.g. "callBasic" matches any case containing that)
///   EELS_STRUCT_LOG_OUT  — optional path to write structLog JSON (default: TestResults/struct_log_<ts>.json)
///   EELS_MAX_CASES       — safety cap, default 200 (tracer supports substring filter over multiple cases)
///
/// Run:
///   $env:EELS_FIXTURES_ROOT = "C:/projects/Scrutor/fixtures/state_tests/cancun/eip1153_tstore"
///   $env:EELS_CASE_FILTER   = "test_basic_tload_after_store"
///   dotnet test Scrutor.EELS.Tests/Scrutor.EELS.Tests.csproj --filter "SingleCaseTrace"
/// </summary>
public sealed class EelsSingleCaseTracer
{
    [Fact(DisplayName = "SingleCaseTrace — isolate one fixture case and emit full structLog")]
    public async Task TraceAsync()
    {
        var opts = BuildOptions();
        var loader = new EelsStateFixtureLoader();
        var cases = loader.LoadCases(opts);

        var caseFilter = Environment.GetEnvironmentVariable("EELS_CASE_FILTER") ?? "";
        IReadOnlyList<EelsStateCase> filtered = string.IsNullOrWhiteSpace(caseFilter)
            ? cases
            : cases.Where(c => c.CaseId.Contains(caseFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(filtered.Count > 0,
            $"No cases matched EELS_CASE_FILTER='{caseFilter}' in {opts.FixturesRoot}. " +
            $"Available ({cases.Count} total): {string.Join(", ", cases.Take(5).Select(c => c.CaseId))}");

        // Run the first matching case
        var testCase = filtered.First();
        Console.WriteLine($"Tracing case: {testCase.CaseId}  [{testCase.ForkName}]");
        Console.WriteLine($"Fixture: {testCase.FixturePath}");
        Console.WriteLine();

        var tracer = new CaseTracer();
        var (result, structLog, accountDiff) = await tracer.RunWithTraceAsync(testCase);

        // Emit structLog to file
        var outPath = Environment.GetEnvironmentVariable("EELS_STRUCT_LOG_OUT")
            ?? Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "TestResults",
                $"struct_log_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath, structLog, Encoding.UTF8);

        // Print compact summary to console
        var summary = BuildSummary(testCase, result, accountDiff);
        Console.WriteLine(summary);
        Console.WriteLine($"StructLog written to: {outPath}");

        // Re-run through the conformance executor for authoritative verdict
        var executor = new EelsStateFixtureExecutor();
        var report = await executor.ExecuteAsync(testCase, CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine("── VERDICT ──────────────────────────────────────────────────────────");
        if (report.Mismatches.Count == 0)
        {
            Console.WriteLine("  ✅  PASS — state and receipt match fixture expectations.");
        }
        else
        {
            Console.WriteLine($"  ❌  FAIL — {report.Mismatches.Count} mismatch(es):");
            foreach (var m in report.Mismatches)
                Console.WriteLine($"    • {m}");
        }

        Assert.True(report.Mismatches.Count == 0,
            $"[{testCase.CaseId}] {report.Mismatches.Count} mismatch(es):\n" +
            string.Join("\n", report.Mismatches.Select(m => "  • " + m)));
    }

    private static EelsHarnessOptions BuildOptions()
    {
        var root = Environment.GetEnvironmentVariable("EELS_FIXTURES_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "fixtures", "state_tests"));

        var fork = Environment.GetEnvironmentVariable("EELS_REQUIRED_FORK") ?? "Cancun";

        var maxCasesRaw = Environment.GetEnvironmentVariable("EELS_MAX_CASES");
        if (!int.TryParse(maxCasesRaw, out var maxCases) || maxCases <= 0)
            maxCases = 200;

        var inclSubdirsRaw = Environment.GetEnvironmentVariable("EELS_INCLUDE_SUBDIRS");
        var inclSubdirs = string.Equals(inclSubdirsRaw, "1", StringComparison.Ordinal) ||
                          string.Equals(inclSubdirsRaw, "true", StringComparison.OrdinalIgnoreCase);

        return new EelsHarnessOptions(root, fork, maxCases, inclSubdirs);
    }

    private static string BuildSummary(
        EelsStateCase testCase,
        ExecutionResult result,
        IReadOnlyList<AccountDiffEntry> accountDiff)
    {
        var sb = new StringBuilder();

        sb.AppendLine("── TRANSACTION ─────────────────────────────────────────────────────");
        sb.AppendLine($"  gasLimit      : {testCase.Transaction.GasLimit:N0}");
        sb.AppendLine($"  gasUsed       : {result.GasUsed:N0}");
        sb.AppendLine($"  refundCounter : {result.GasRefundCounter:N0}");
        sb.AppendLine($"  refundCapped  : {Math.Min(result.GasRefundCounter, (long)(result.GasUsed / 5)):N0}  (EIP-3529 cap = gasUsed/5)");
        sb.AppendLine($"  gasRemaining  : {(testCase.Transaction.GasLimit - result.GasUsed):N0}");
        sb.AppendLine($"  success       : {result.IsSuccess}");
        if (!result.IsSuccess)
            sb.AppendLine($"  error         : {result.Error}");
        sb.AppendLine($"  trace steps   : {result.TraceSteps.Count}");
        sb.AppendLine();

        sb.AppendLine("── ACCOUNT DIFF (pre → post) ────────────────────────────────────────");
        if (accountDiff.Count == 0)
        {
            sb.AppendLine("  (no changes)");
        }
        else
        {
            foreach (var entry in accountDiff)
            {
                sb.AppendLine($"  {entry.Address}");
                if (entry.BalanceDelta != 0)
                {
                    var sign = entry.BalanceDelta > 0 ? "+" : "";
                    sb.AppendLine($"    balance  {sign}{entry.BalanceDelta:N0}  " +
                                  $"({EelsHex.ToCanonicalHex(entry.PreBalance)} → {EelsHex.ToCanonicalHex(entry.PostBalance)})");
                }
                if (entry.NonceDelta != 0)
                    sb.AppendLine($"    nonce    {entry.NonceDelta:+#;-#;0}  ({entry.PreNonce} → {entry.PostNonce})");
                foreach (var (slot, prv, now) in entry.StorageChanges.Take(5))
                    sb.AppendLine($"    storage[{EelsHex.ToCanonicalHex(slot)}]  " +
                                  $"{EelsHex.ToCanonicalHex(prv)} → {EelsHex.ToCanonicalHex(now)}");
                if (entry.StorageChanges.Count > 5)
                    sb.AppendLine($"    ... +{entry.StorageChanges.Count - 5} more storage changes");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// CaseTracer: builds pre-state, runs EVM, diffs accounts, emits EIP-3155 JSON
// ---------------------------------------------------------------------------

internal sealed class CaseTracer
{
    private readonly IStateTransition _stateTransition =
        new StateTransition(new EvmMachine(OpcodeCatalog.CreateAll()));

    public async Task<(ExecutionResult result, string structLogJson, IReadOnlyList<AccountDiffEntry> accountDiff)>
        RunWithTraceAsync(EelsStateCase testCase)
    {
        var globalState = new GlobalState();
        foreach (var (address, account) in testCase.PreState)
        {
            globalState.SetBalance(address, account.Balance);
            globalState.SetNonce(address, account.Nonce);
            globalState.SetCode(address, account.Code);
            foreach (var (slot, value) in account.Storage)
                globalState.SetStorageAt(address, slot, value);
        }

        // Snapshot pre-state (IDictionary — returned by GlobalState.Snapshot())
        var preSnapshot = globalState.Snapshot();

        // Enable tracing
        var tx = testCase.Transaction;
        tx.EnableTracing = true;

        ExecutionResult result;
        try
        {
            result = await RunOnLargeStack(() =>
                _stateTransition.ApplyTransactionAsync(
                    tx, globalState, testCase.BlockContext, commit: true));
        }
        catch (Exception ex)
        {
            result = ExecutionResult.Failure(EvmError.InternalError, tx.GasLimit);
            Console.WriteLine($"[TRACER] Exception during execution: {ex.GetType().Name}: {ex.Message}");
        }

        var postSnapshot = globalState.Snapshot();
        var accountDiff  = BuildAccountDiff(preSnapshot, postSnapshot);
        var structLog    = BuildStructLog(testCase, result);

        return (result, structLog, accountDiff);
    }

    // ------------------------------------------------------------------
    // EIP-3155 structLog builder
    // Matching fields: pc (int), op (string), gas (hex), gasCost (hex),
    //   depth (int), stack (hex strings, top = last), memory (hex), storage (hex→hex map)
    // ------------------------------------------------------------------

    private static string BuildStructLog(EelsStateCase testCase, ExecutionResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"caseId\": \"{Escape(testCase.CaseId)}\",");
        sb.AppendLine($"  \"fork\": \"{testCase.ForkName}\",");
        sb.AppendLine($"  \"gasUsed\": {result.GasUsed},");
        sb.AppendLine($"  \"gasRefundCounter\": {result.GasRefundCounter},");
        sb.AppendLine($"  \"success\": {(result.IsSuccess ? "true" : "false")},");
        sb.AppendLine($"  \"error\": {(result.IsSuccess ? "null" : $"\"{result.Error}\"")},");
        sb.AppendLine("  \"structLogs\": [");

        var steps = result.TraceSteps;
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var comma = i < steps.Count - 1 ? "," : "";

            // Stack: List<string> of hex values — EIP-3155 §3 — top of stack is LAST element
            var stackJson = "[" + string.Join(", ", s.Stack.Select(v => $"\"{v}\"")) + "]";

            // Memory: List<string> of 32-byte hex rows — join into flat hex string
            var memHex = string.Concat(s.Memory.Select(row => row.Replace("0x", "").Replace(" ", "")));

            // Storage: Dictionary<string,string> of hex key→value pairs
            var storageJson = "{" + string.Join(", ",
                s.Storage.Select(kvp => $"\"{kvp.Key}\": \"{kvp.Value}\"")) + "}";

            sb.AppendLine($"    {{");
            sb.AppendLine($"      \"pc\": {s.Pc},");
            sb.AppendLine($"      \"op\": \"{Escape(s.Op)}\",");
            sb.AppendLine($"      \"gas\": \"{s.Gas}\",");
            sb.AppendLine($"      \"gasCost\": \"{s.GasCost}\",");
            sb.AppendLine($"      \"depth\": {s.Depth},");
            sb.AppendLine($"      \"stack\": {stackJson},");
            sb.AppendLine($"      \"memory\": \"{memHex}\",");
            sb.AppendLine($"      \"storage\": {storageJson}");
            sb.AppendLine($"    }}{comma}");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Account diff
    // ------------------------------------------------------------------

    private static IReadOnlyList<AccountDiffEntry> BuildAccountDiff(
        IDictionary<Address, Account> pre,
        IDictionary<Address, Account> post)
    {
        var allAddresses = pre.Keys.Union(post.Keys).ToHashSet();
        var diff = new List<AccountDiffEntry>();

        foreach (var addr in allAddresses)
        {
            pre.TryGetValue(addr, out var preAcct);
            post.TryGetValue(addr, out var postAcct);

            var preBalance  = preAcct?.Balance  ?? BigInteger.Zero;
            var postBalance = postAcct?.Balance ?? BigInteger.Zero;
            var preNonce    = preAcct?.Nonce    ?? 0UL;
            var postNonce   = postAcct?.Nonce   ?? 0UL;

            var preStorage  = preAcct?.Storage  ?? new Dictionary<BigInteger, BigInteger>();
            var postStorage = postAcct?.Storage ?? new Dictionary<BigInteger, BigInteger>();

            bool changed = preBalance != postBalance || preNonce != postNonce;
            var storageChanges = new List<(BigInteger slot, BigInteger pre, BigInteger post)>();

            foreach (var slot in preStorage.Keys.Union(postStorage.Keys))
            {
                preStorage.TryGetValue(slot, out var preVal);
                postStorage.TryGetValue(slot, out var postVal);
                if (preVal != postVal)
                {
                    storageChanges.Add((slot, preVal, postVal));
                    changed = true;
                }
            }

            if (changed)
            {
                diff.Add(new AccountDiffEntry(
                    addr.ToString()!,
                    preBalance, postBalance, postBalance - preBalance,
                    preNonce, postNonce, (long)postNonce - (long)preNonce,
                    storageChanges));
            }
        }

        return diff;
    }

    // 32MB stack worker (matches EelsStateFixtureExecutor pattern)
    private static Task<T> RunOnLargeStack<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try   { tcs.SetResult(action().GetAwaiter().GetResult()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, 32 * 1024 * 1024);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------

internal sealed record AccountDiffEntry(
    string Address,
    BigInteger PreBalance,
    BigInteger PostBalance,
    BigInteger BalanceDelta,
    ulong PreNonce,
    ulong PostNonce,
    long NonceDelta,
    IReadOnlyList<(BigInteger slot, BigInteger pre, BigInteger post)> StorageChanges);
