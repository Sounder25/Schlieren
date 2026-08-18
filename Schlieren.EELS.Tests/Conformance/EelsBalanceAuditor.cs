using System.Collections.Concurrent;
using System.Numerics;
using System.Text;
using System.Threading;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Conformance;

/// <summary>
/// EELS Balance Auditor — Automatic Gas Ledger Checker
/// =====================================================
/// For every failing fixture case this tool reconstructs the EXPECTED sender
/// balance from first principles using the 5-term gas ledger equation, then
/// flags exactly which term produces the wrong result in Schlieren vs the fixture.
///
/// The 5-term equation (sender perspective):
///
///   expected_post_balance =
///       pre_balance                               [Term 0 — pre-state]
///     - gasLimit × effectiveGasPrice              [Term 1 — upfront gas deduction]
///     - value                                     [Term 2 — value transfer]
///     + (gasLimit - totalGasUsed) × effectiveGasPrice  [Term 3 — unused gas refund]
///     + min(gasRefundCounter, totalGasUsed / 5) × effectiveGasPrice  [Term 4 — EIP-3529 refund]
///     + value                                     [Term 5 — restored on revert (if !success)]
///
/// effectiveGasPrice:
///   • type-0/1 legacy: GasPrice
///   • type-2/3 EIP-1559: min(MaxFeePerGas, BaseFeePerGas + MaxPriorityFeePerGas)
///
/// Run:
///   $env:EELS_FIXTURES_ROOT  = "C:/projects/Schlieren/fixtures/state_tests/cancun"
///   $env:EELS_INCLUDE_SUBDIRS = "1"
///   $env:EELS_MAX_CASES      = "9999"
///   dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter "EelsBalanceAudit"
/// </summary>
public sealed class EelsBalanceAuditRunner
{
    [Fact(DisplayName = "EelsBalanceAudit — reconstruct ledger for all failing cases")]
    public async Task RunAsync()
    {
        var opts = EelsHarnessOptions.FromEnvironment();
        var report = await EelsBalanceAuditor.RunAsync(opts, CancellationToken.None);

        var markdown = EelsBalanceAuditor.RenderMarkdown(report);

        var outDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "TestResults");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, $"balance_audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md");
        await File.WriteAllTextAsync(outPath, markdown, Encoding.UTF8);

        Console.WriteLine(markdown);
        Console.WriteLine($"Report written to: {outPath}");

        // Always passes — the report IS the artifact
        Assert.True(true, "Balance audit complete — see TestResults/ for ledger breakdown.");
    }
}

// ---------------------------------------------------------------------------
// Core auditor logic
// ---------------------------------------------------------------------------

public static class EelsBalanceAuditor
{
    /// <summary>
    /// Runs fixture cases, and for each balance mismatch reconstructs the
    /// 5-term ledger to identify which term is wrong.
    /// </summary>
    public static async Task<BalanceAuditReport> RunAsync(
        EelsHarnessOptions opts,
        CancellationToken ct = default)
    {
        var loader = new EelsStateFixtureLoader();
        var cases  = loader.LoadCases(opts);

        // Thread-safe accumulators for parallel execution
        var ledgerBag          = new ConcurrentBag<LedgerAuditRow>();
        var totalCasesCount    = 0;
        var balanceMismatches  = 0;

        // [AI-EDIT 2026-08-05] Parallel sweep — each slot owns its own
        // EelsStateFixtureExecutor (instance LargeStackWorker, 32MB thread).
        // No shared-queue contention. Bounded at ProcessorCount so we don't
        // spawn 9,999 large-stack threads simultaneously.
        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(cases, parallelOpts, async (testCase, innerCt) =>
        {
            Interlocked.Increment(ref totalCasesCount);

            var executor = new EelsStateFixtureExecutor();
            var report = await executor.ExecuteAsync(testCase, innerCt);

            var balanceErrors = report.Mismatches
                .Where(m => m.StartsWith("balance mismatch", StringComparison.Ordinal))
                .ToList();

            if (balanceErrors.Count == 0)
                return;

            Interlocked.Increment(ref balanceMismatches);

            foreach (var mismatch in balanceErrors)
            {
                var row = AuditLedger(testCase, report, mismatch);
                ledgerBag.Add(row);
            }
        });

        // Deterministic output order (case_id alphabetical) regardless of parallel scheduling
        var ledgerRows = ledgerBag
            .OrderBy(r => r.CaseId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Aggregate term-fault counts
        var termFaults = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in ledgerRows)
        {
            foreach (var fault in row.FaultedTerms)
            {
                termFaults.TryGetValue(fault, out var n);
                termFaults[fault] = n + 1;
            }
        }

        // Group by consistent delta to find single-root-cause buckets
        var deltaBuckets = ledgerRows
            .GroupBy(r => r.ActualBalance - r.ExpectedBalance)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new DeltaBucket(
                Delta: g.Key,
                Count: g.Count(),
                Examples: g.Take(3).Select(r => r.CaseId).ToList()))
            .ToList();

        return new BalanceAuditReport(
            FixturesRoot: opts.FixturesRoot,
            Fork: opts.ForkName,
            TotalCases: totalCasesCount,
            BalanceMismatchCases: balanceMismatches,
            LedgerRows: ledgerRows,
            TermFaultCounts: termFaults,
            TopDeltaBuckets: deltaBuckets);
    }

    // ------------------------------------------------------------------
    // 5-term ledger reconstruction
    // ------------------------------------------------------------------

    private static LedgerAuditRow AuditLedger(
        EelsStateCase testCase,
        EelsCaseExecutionReport executionReport,
        string mismatch)
    {
        var tx    = testCase.Transaction;
        var block = testCase.BlockContext;

        // ── Parse fixture mismatch for expected/actual ───────────────
        var expectedBalance = ParseHexFromMismatch(mismatch, "expected") ?? BigInteger.Zero;
        var actualBalance   = ParseHexFromMismatch(mismatch, "actual")   ?? BigInteger.Zero;
        var address         = ParseAddressFromMismatch(mismatch) ?? tx.From.ToString()!;

        // ── Term 0: pre-state balance ─────────────────────────────────
        BigInteger preBalance = BigInteger.Zero;
        if (testCase.PreState.TryGetValue(tx.From, out var senderPreAcct))
            preBalance = senderPreAcct.Balance;
        // If mismatch is on a different account (coinbase, recipient), use fixture pre-state
        var addrParsed = TryParseAddress(address);
        if (addrParsed.HasValue && testCase.PreState.TryGetValue(addrParsed.Value, out var addrPreAcct))
            preBalance = addrPreAcct.Balance;

        // ── Effective gas price (EIP-1559 §6.2) ──────────────────────
        var baseFee = new BigInteger(block.BaseFeePerGas);
        BigInteger effectiveGasPrice;
        if (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero)
            effectiveGasPrice = BigInteger.Min(tx.MaxFeePerGas, baseFee + tx.MaxPriorityFeePerGas);
        else
            effectiveGasPrice = tx.GasPrice;

        // ── Term 1: upfront gas deduction ─────────────────────────────
        // Schlieren charges: gasLimit × effectiveGasPrice (not maxFeePerGas — type-2 refunds difference later)
        var term1_upfront_gas = new BigInteger(tx.GasLimit) * effectiveGasPrice;

        // ── Term 2: value transfer ────────────────────────────────────
        var term2_value = tx.Value;

        // ── Term 3: unused gas refund ─────────────────────────────────
        // totalGasUsed = report.GasUsed (already includes intrinsic, as returned by StateTransition)
        var totalGasUsed = (BigInteger)executionReport.GasUsed;
        var unusedGas = new BigInteger(tx.GasLimit) > totalGasUsed
            ? new BigInteger(tx.GasLimit) - totalGasUsed
            : BigInteger.Zero;
        var term3_unused_gas_refund = unusedGas * effectiveGasPrice;

        // ── Term 4: EIP-3529 capped storage refund ────────────────────
        // [AI-EDIT 2026-08-05] GasRefundCounter is now surfaced on the report.
        // Exact computation (matching StateTransition.cs lines 278-283):
        //   maxRefund     = totalGasUsed / 5
        //   cappedRefund  = min(refundCounter, maxRefund)
        //   term4_amount  = cappedRefund × effectiveGasPrice
        //
        // Note: StateTransition subtracts the capped refund FROM totalGasUsed before
        // computing the unused-gas refund, so the order matters. We replicate that here.
        var rawRefundCounter  = executionReport.GasRefundCounter;   // before capping
        var term4_max_gas     = totalGasUsed / 5;
        var term4_capped_gas  = BigInteger.Min(rawRefundCounter, term4_max_gas);
        var term4_exact_refund = term4_capped_gas * effectiveGasPrice;

        // Adjusted total gas (StateTransition applies the cap before computing gasRefund)
        var totalGasUsedAfterRefund = totalGasUsed > term4_capped_gas
            ? totalGasUsed - term4_capped_gas
            : BigInteger.Zero;
        var unusedGasAfterRefund = new BigInteger(tx.GasLimit) > totalGasUsedAfterRefund
            ? new BigInteger(tx.GasLimit) - totalGasUsedAfterRefund
            : BigInteger.Zero;
        var term3_corrected_refund = unusedGasAfterRefund * effectiveGasPrice;

        // ── Term 5: value restoration on revert ──────────────────────
        var term5_value_restore = executionReport.ExecutionSucceeded ? BigInteger.Zero : tx.Value;

        // ── Reconstruct expected sender balance (all 5 terms) ─────────
        // This now matches exactly what StateTransition.cs computes.
        var reconstructed =
              preBalance
            - term1_upfront_gas   // T1: upfront deduction (gasLimit × effectiveGasPrice)
            - term2_value         // T2: value sent
            + term3_corrected_refund  // T3: unused gas returned (after EIP-3529 adjustment)
            + term5_value_restore;    // T5: value restored on revert

        // ── Coinbase Fee Routing Audit (EIP-1559 §6.2) ────────────────
        BigInteger priorityFeePerGas;
        if (tx.TxType >= 2)
            priorityFeePerGas = BigInteger.Min(tx.MaxPriorityFeePerGas, tx.MaxFeePerGas > baseFee ? tx.MaxFeePerGas - baseFee : BigInteger.Zero);
        else
            priorityFeePerGas = effectiveGasPrice;

        BigInteger preCoinbase = BigInteger.Zero;
        if (testCase.PreState.TryGetValue(block.Coinbase, out var cbPreAcct))
            preCoinbase = cbPreAcct.Balance;

        BigInteger expectedCoinbaseFee = totalGasUsedAfterRefund * priorityFeePerGas;
        BigInteger expectedCoinbaseBalance = preCoinbase + expectedCoinbaseFee;

        // ── Identify faulted terms ─────────────────────────────────────
        var faultedTerms = new List<string>();
        var delta = actualBalance - expectedBalance;

        // Coinbase Fee Routing fault check
        if (addrParsed.HasValue && addrParsed.Value.Equals(block.Coinbase))
        {
            var cbDelta = actualBalance - expectedCoinbaseBalance;
            if (cbDelta != 0)
            {
                faultedTerms.Add($"Coinbase Fee Routing: balance off by {cbDelta:+#;-#;0} (expected priority fee={expectedCoinbaseFee}, baseFee={baseFee})");
            }
        }

        // Term 1 fault: if delta ≈ gasLimit × (effectiveGasPrice − something)
        // We check if the delta is a multiple of effectiveGasPrice
        if (effectiveGasPrice > 0 && delta % effectiveGasPrice == 0)
        {
            var gasDelta = delta / effectiveGasPrice;
            if (gasDelta != 0)
                faultedTerms.Add($"Term1 (upfront gas): off by {gasDelta:+#;-#;0} gas units at effectiveGasPrice={effectiveGasPrice}");
        }

        // Term 2 fault: if delta == ±value
        if (tx.Value > 0 && BigInteger.Abs(delta) == tx.Value)
            faultedTerms.Add($"Term2 (value transfer): delta == {(delta > 0 ? "+" : "-")}value ({tx.Value})");

        // Term 3 fault: if delta matches unused gas × price
        if (term3_unused_gas_refund > 0 && delta == term3_unused_gas_refund)
            faultedTerms.Add($"Term3 (unused gas refund): refund not credited (={term3_unused_gas_refund})");
        else if (term3_unused_gas_refund > 0 && delta == -term3_unused_gas_refund)
            faultedTerms.Add($"Term3 (unused gas refund): refund double-credited (={term3_unused_gas_refund})");

        // Term 4 fault: exact diagnosis using GasRefundCounter
        // Expected contribution = term4_exact_refund; if Schlieren got it wrong the
        // reconstructed balance won't match the fixture. We compare directly.
        if (term4_exact_refund > 0)
        {
            var expectedWithoutT4 = reconstructed - term4_exact_refund;
            if (BigInteger.Abs(actualBalance - (expectedWithoutT4 + term4_exact_refund)) >
                BigInteger.Abs(actualBalance - expectedWithoutT4))
            {
                // Adding T4 made it worse — Schlieren is double-counting the refund
                faultedTerms.Add(
                    $"Term4 (EIP-3529 refund): double-counted. " +
                    $"refundCounter={rawRefundCounter}, capped={term4_capped_gas} gas, " +
                    $"value={term4_exact_refund}");
            }
            else if (BigInteger.Abs(delta) == term4_exact_refund)
            {
                // Delta exactly equals the expected refund — Schlieren skipped it entirely
                faultedTerms.Add(
                    $"Term4 (EIP-3529 refund): not credited. " +
                    $"refundCounter={rawRefundCounter}, capped={term4_capped_gas} gas, " +
                    $"value={term4_exact_refund}");
            }
            else if (delta > 0 && delta < term4_exact_refund)
            {
                faultedTerms.Add(
                    $"Term4 (EIP-3529 refund): partially credited. " +
                    $"expected={term4_exact_refund}, shortfall={term4_exact_refund - delta}");
            }
        }

        // Term 5 fault: if this is a revert but value wasn't restored
        if (!executionReport.ExecutionSucceeded && tx.Value > 0 && BigInteger.Abs(delta) == tx.Value)
            faultedTerms.Add($"Term5 (value restore on revert): value={tx.Value} not restored to sender");

        // Catch-all if nothing matched
        if (faultedTerms.Count == 0)
        {
            if (delta != 0)
                faultedTerms.Add($"Unknown: Δ={delta:+#;-#;0} — run eels-single-case-tracer for step trace");
        }

        return new LedgerAuditRow(
            CaseId: testCase.CaseId,
            FixturePath: testCase.FixturePath,
            Address: address,
            PreBalance: preBalance,
            ExpectedBalance: expectedBalance,
            ActualBalance: actualBalance,
            EffectiveGasPrice: effectiveGasPrice,
            GasLimit: tx.GasLimit,
            TotalGasUsed: (ulong)totalGasUsed,
            ValueTransfer: tx.Value,
            Term1_UpfrontGas: term1_upfront_gas,
            Term2_Value: term2_value,
            // Use corrected Term 3 (unused gas after EIP-3529 refund counter is subtracted)
            Term3_UnusedGasRefund: term3_corrected_refund,
            // Use exact Term 4 from the live GasRefundCounter — no longer a heuristic
            Term4_ExactStorageRefund: term4_exact_refund,
            Term4_RawRefundCounter: rawRefundCounter,
            Term4_CappedGas: term4_capped_gas,
            Term5_ValueRestore: term5_value_restore,
            Reconstructed: reconstructed,
            ReconstructionDelta: reconstructed - expectedBalance,
            FaultedTerms: faultedTerms,
            ExecutionSucceeded: executionReport.ExecutionSucceeded,
            TxType: tx.TxType);
    }

    // ------------------------------------------------------------------
    // Markdown renderer
    // ------------------------------------------------------------------

    public static string RenderMarkdown(BalanceAuditReport r)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# EELS Balance Auditor — Gas Ledger Report");
        sb.AppendLine();
        sb.AppendLine($"- **Fork**          : `{r.Fork}`");
        sb.AppendLine($"- **Fixtures root** : `{r.FixturesRoot}`");
        sb.AppendLine($"- **Generated**     : `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`");
        sb.AppendLine();

        // KPI
        sb.AppendLine("## KPI");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| :----- | ----: |");
        sb.AppendLine($"| Total cases             | {r.TotalCases} |");
        sb.AppendLine($"| Cases with balance fail | {r.BalanceMismatchCases} |");
        sb.AppendLine($"| Total ledger rows       | {r.LedgerRows.Count} |");
        sb.AppendLine();

        // Ledger equation reminder
        sb.AppendLine("## The 5-Term Ledger");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("expected_post_balance =");
        sb.AppendLine("  pre_balance                                      [Term 0]");
        sb.AppendLine("- gasLimit × effectiveGasPrice                     [Term 1: upfront gas]");
        sb.AppendLine("- value                                            [Term 2: transfer]");
        sb.AppendLine("+ (gasLimit - totalGasUsed) × effectiveGasPrice    [Term 3: unused gas refund]");
        sb.AppendLine("+ min(refundCounter, totalGasUsed/5) × price       [Term 4: EIP-3529 refund]");
        sb.AppendLine("+ value (only if execution reverted)               [Term 5: value restore]");
        sb.AppendLine();
        sb.AppendLine("effectiveGasPrice:");
        sb.AppendLine("  type 0/1: GasPrice");
        sb.AppendLine("  type 2/3: min(MaxFeePerGas, BaseFee + MaxPriorityFeePerGas)");
        sb.AppendLine("```");
        sb.AppendLine();

        // Term fault summary
        sb.AppendLine("## Term Fault Summary");
        sb.AppendLine();
        sb.AppendLine("> Which ledger term caused the most failures?");
        sb.AppendLine();
        sb.AppendLine("| Term | Occurrences | Description |");
        sb.AppendLine("| :--- | ----------: | :---------- |");
        foreach (var (fault, cnt) in r.TermFaultCounts.OrderByDescending(kvp => kvp.Value))
        {
            // Trim to first term keyword
            var label = fault.Length > 80 ? fault[..80] + "…" : fault;
            sb.AppendLine($"| `{label}` | {cnt} | — |");
        }
        if (r.TermFaultCounts.Count == 0)
            sb.AppendLine("| (no balance faults detected) | — | — |");
        sb.AppendLine();

        // Top delta buckets
        sb.AppendLine("## Balance Delta Buckets (actual − expected)");
        sb.AppendLine();
        sb.AppendLine("> Same delta across many cases = single root cause.");
        sb.AppendLine();
        sb.AppendLine("| Delta | Count | Example Cases |");
        sb.AppendLine("| ----: | ----: | :------------ |");
        foreach (var bucket in r.TopDeltaBuckets)
        {
            var sign = bucket.Delta >= 0 ? "+" : "";
            var examples = string.Join(", ", bucket.Examples.Take(2));
            sb.AppendLine($"| `{sign}{bucket.Delta}` | {bucket.Count} | {examples} |");
        }
        if (r.TopDeltaBuckets.Count == 0)
            sb.AppendLine("| (no data) | — | — |");
        sb.AppendLine();

        // Per-case ledger table (first 50 rows to avoid truncation)
        var displayRows = r.LedgerRows.Take(50).ToList();
        sb.AppendLine($"## Per-Case Ledger Breakdown (first {displayRows.Count} of {r.LedgerRows.Count})");
        sb.AppendLine();
        sb.AppendLine("| Case ID | TxType | EffPrice | GasLimit | GasUsed | Δ (actual−expected) | Faulted Term |");
        sb.AppendLine("| :------ | -----: | -------: | -------: | ------: | ------------------: | :----------- |");
        foreach (var row in displayRows)
        {
            var delta = row.ActualBalance - row.ExpectedBalance;
            var sign  = delta >= 0 ? "+" : "";
            var fault = row.FaultedTerms.FirstOrDefault() ?? "—";
            if (fault.Length > 60) fault = fault[..60] + "…";
            sb.AppendLine(
                $"| `{row.CaseId}` | {row.TxType} | {row.EffectiveGasPrice}" +
                $" | {row.GasLimit:N0} | {row.TotalGasUsed:N0}" +
                $" | `{sign}{delta}` | {fault} |");
        }
        sb.AppendLine();

        // Detailed breakdown for top 5 failing cases
        sb.AppendLine("## Detailed Ledger for Top 5 Cases");
        sb.AppendLine();
        foreach (var row in r.LedgerRows.Take(5))
        {
            sb.AppendLine($"### `{row.CaseId}`");
            sb.AppendLine();
            sb.AppendLine($"- Fixture  : `{row.FixturePath}`");
            sb.AppendLine($"- Address  : `{row.Address}`");
            sb.AppendLine($"- TxType   : {row.TxType}");
            sb.AppendLine($"- Success  : {row.ExecutionSucceeded}");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine($"  pre_balance            = {row.PreBalance} ({EelsHex.ToCanonicalHex(row.PreBalance)})");
            sb.AppendLine($"  effectiveGasPrice      = {row.EffectiveGasPrice}");
            sb.AppendLine($"  gasLimit               = {row.GasLimit:N0}");
            sb.AppendLine($"  totalGasUsed           = {row.TotalGasUsed:N0}");
            sb.AppendLine($"  value                  = {row.ValueTransfer}");
            sb.AppendLine();
            sb.AppendLine($"  [T1] - upfront gas     = -{row.Term1_UpfrontGas}");
            sb.AppendLine($"  [T2] - value           = -{row.Term2_Value}");
            sb.AppendLine($"  [T3] + unused gas refund= +{row.Term3_UnusedGasRefund}");
            sb.AppendLine($"  [T4] + EIP-3529 refund = +{row.Term4_ExactStorageRefund}  (counter={row.Term4_RawRefundCounter}, capped={row.Term4_CappedGas} gas)");
            sb.AppendLine($"  [T5] + value restore   = +{row.Term5_ValueRestore}  (0 if success)");
            sb.AppendLine();
            sb.AppendLine($"  reconstructed (T0+T1+T2+T3+T4+T5) = {row.Reconstructed}");
            sb.AppendLine($"  fixture expects               = {row.ExpectedBalance}");
            sb.AppendLine($"  schlieren actual                = {row.ActualBalance}");
            sb.AppendLine($"  Δ (actual − expected)         = {row.ActualBalance - row.ExpectedBalance:+#;-#;0}");
            sb.AppendLine($"  reconstruction error (T0..T5) = {row.ReconstructionDelta:+#;-#;0}");
            sb.AppendLine("```");
            sb.AppendLine();
            if (row.FaultedTerms.Count > 0)
            {
                sb.AppendLine("**Fault diagnosis:**");
                foreach (var f in row.FaultedTerms)
                    sb.AppendLine($"- {f}");
            }
            sb.AppendLine();
        }

        // Next steps
        sb.AppendLine("## Recommended Next Steps");
        sb.AppendLine();
        if (r.LedgerRows.Count == 0)
        {
            sb.AppendLine("✅ **No balance failures.** All ledger terms check out.");
        }
        else
        {
            var topFault = r.TermFaultCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            sb.AppendLine($"1. **Most common fault**: `{topFault.Key}` ({topFault.Value} occurrences).");
            if (r.TopDeltaBuckets.Count > 0 && r.TopDeltaBuckets[0].Count > 1)
            {
                sb.AppendLine($"2. **Consistent delta `{r.TopDeltaBuckets[0].Delta:+#;-#;0}`** appears in {r.TopDeltaBuckets[0].Count} cases — strong single-root-cause signal.");
            }
            sb.AppendLine("3. **Get the step trace** for one of the top-delta cases:");
            sb.AppendLine("   ```powershell");
            var example = r.LedgerRows.FirstOrDefault();
            if (example is not null)
            {
                sb.AppendLine($"   $env:EELS_FIXTURES_ROOT = \"{Path.GetDirectoryName(example.FixturePath)}\"");
                sb.AppendLine($"   $env:EELS_CASE_FILTER   = \"{example.CaseId}\"");
            }
            sb.AppendLine("   dotnet test Schlieren.EELS.Tests/Schlieren.EELS.Tests.csproj --filter \"SingleCaseTrace\"");
            sb.AppendLine("   ```");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static BigInteger? ParseHexFromMismatch(string mismatch, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            mismatch, key + @"=(\S+)");
        if (!m.Success) return null;
        return TryParseHex(m.Groups[1].Value.TrimEnd(',', ';', '.'));
    }

    private static string? ParseAddressFromMismatch(string mismatch)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            mismatch, @"for (0x[0-9a-fA-F]{20,40})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Address? TryParseAddress(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return Address.FromHex(s); } catch { return null; }
    }

    private static BigInteger? TryParseHex(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return BigInteger.Parse("0" + s[2..], System.Globalization.NumberStyles.HexNumber);
            return BigInteger.Parse(s);
        }
        catch { return null; }
    }
}

// ---------------------------------------------------------------------------
// Report models
// ---------------------------------------------------------------------------

public sealed record LedgerAuditRow(
    string CaseId,
    string FixturePath,
    string Address,
    BigInteger PreBalance,
    BigInteger ExpectedBalance,
    BigInteger ActualBalance,
    BigInteger EffectiveGasPrice,
    ulong GasLimit,
    ulong TotalGasUsed,
    BigInteger ValueTransfer,
    BigInteger Term1_UpfrontGas,
    BigInteger Term2_Value,
    BigInteger Term3_UnusedGasRefund,
    /// <summary>Exact EIP-3529 refund amount = min(RawRefundCounter, GasUsed/5) × price.</summary>
    BigInteger Term4_ExactStorageRefund,
    /// <summary>Raw GasRefundCounter from the EVM before the gasUsed/5 cap is applied.</summary>
    long Term4_RawRefundCounter,
    /// <summary>Capped gas units = min(RawRefundCounter, GasUsed/5).</summary>
    BigInteger Term4_CappedGas,
    BigInteger Term5_ValueRestore,
    BigInteger Reconstructed,
    BigInteger ReconstructionDelta,
    IReadOnlyList<string> FaultedTerms,
    bool ExecutionSucceeded,
    byte TxType);

public sealed record DeltaBucket(
    BigInteger Delta,
    int Count,
    IReadOnlyList<string> Examples);

public sealed record BalanceAuditReport(
    string FixturesRoot,
    string Fork,
    int TotalCases,
    int BalanceMismatchCases,
    IReadOnlyList<LedgerAuditRow> LedgerRows,
    IReadOnlyDictionary<string, int> TermFaultCounts,
    IReadOnlyList<DeltaBucket> TopDeltaBuckets);
