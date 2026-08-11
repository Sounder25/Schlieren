using System.Numerics;
using System.Text.RegularExpressions;
using Scrutor.Core.Execution;
using Scrutor.EELS.Tests.Harness;

namespace Scrutor.EELS.Tests.Conformance;

/// <summary>
/// Phase 2 bridge: feed EELS mismatch strings into Layer 1 (<see cref="DivergenceDiagnostics"/>)
/// and Layer 2 (<see cref="StructuralPatternRules"/>) for taxonomy / UI / product surfaces.
/// </summary>
public static class Layer1DiagnosisBridge
{
    /// <summary>
    /// Well-known EELS fixture coinbase used in many state tests.
    /// </summary>
    public const string EelsFixtureCoinbase = "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba";

    /// <summary>
    /// Run Layer 1 + Layer 2 rules against one failed case's mismatch list.
    /// </summary>
    public static IReadOnlyList<DivergenceDiagnostics.Diagnosis> DiagnoseCase(
        EelsStateCase testCase,
        EelsCaseExecutionReport report)
    {
        var results = new List<DivergenceDiagnostics.Diagnosis>();
        if (report.Mismatches.Count == 0)
            return results;

        var mismatches = report.Mismatches;
        var gasPrice = ResolveEffectiveGasPrice(testCase);
        var eipFolder = ExtractEipFolder(testCase.FixturePath);

        bool hasMissingAccount = mismatches.Any(m =>
            m.StartsWith("missing account", StringComparison.Ordinal));
        bool hasNonceMismatch = mismatches.Any(m =>
            m.StartsWith("nonce mismatch", StringComparison.Ordinal));
        bool hasCodeMismatch = mismatches.Any(m =>
            m.StartsWith("code mismatch", StringComparison.Ordinal));
        bool hasBalanceMismatch = mismatches.Any(m =>
            m.StartsWith("balance mismatch", StringComparison.Ordinal));
        bool hasStorageMismatch = mismatches.Any(m =>
            m.StartsWith("storage mismatch", StringComparison.Ordinal));
        bool hasReceiptMismatch = mismatches.Any(m =>
            m.StartsWith("receipt.status mismatch", StringComparison.Ordinal));
        bool hasUnexpectedAccount = mismatches.Any(m =>
            m.StartsWith("unexpected account", StringComparison.Ordinal));
        bool hasStorageWriteWhenExpectedEmpty = mismatches.Any(m =>
            m.StartsWith("storage mismatch", StringComparison.Ordinal) &&
            m.Contains("expected=0x0", StringComparison.OrdinalIgnoreCase));
        bool hasStorageEmptyWhenExpectedNonZero = mismatches.Any(m =>
            m.StartsWith("storage mismatch", StringComparison.Ordinal) &&
            m.Contains("actual=0x0", StringComparison.OrdinalIgnoreCase) &&
            !m.Contains("expected=0x0", StringComparison.OrdinalIgnoreCase));

        // ── Layer 1: balance gas constants ──────────────────────────────────
        bool hasBalanceUndercharge = false;
        bool hasBalanceOvercharge = false;
        long? primaryDeltaGas = null;
        bool touchesCoinbase = false;

        foreach (var line in mismatches)
        {
            if (!line.StartsWith("balance mismatch", StringComparison.Ordinal))
                continue;

            var addr = ExtractAddress(line);
            if (addr is not null &&
                addr.Equals(EelsFixtureCoinbase, StringComparison.OrdinalIgnoreCase))
                touchesCoinbase = true;

            var (exp, act) = ParseExpectedActualBigInt(line);
            if (exp is null || act is null)
                continue;

            var deltaWei = act.Value - exp.Value;
            if (deltaWei < 0) hasBalanceUndercharge = true;
            if (deltaWei > 0) hasBalanceOvercharge = true;

            if (gasPrice > 0)
            {
                results.AddRange(DivergenceDiagnostics.DiagnoseBalanceDelta(deltaWei, gasPrice));
                try
                {
                    var dg = (long)(deltaWei / gasPrice);
                    // Prefer sender residual as primary when multiple balances diverge
                    bool isSender = addr is not null &&
                        addr.Equals(testCase.Sender.ToString(), StringComparison.OrdinalIgnoreCase);
                    if (primaryDeltaGas is null || isSender)
                        primaryDeltaGas = dg;
                }
                catch
                {
                    /* overflow — skip */
                }
            }
        }

        // ── Layer 1: precompile invalid-success ─────────────────────────────
        var precompileDx = DivergenceDiagnostics.DiagnosePrecompileInvalidSuccess(
            hasStorageWriteWhenExpectedEmpty,
            hasBalanceUndercharge,
            eipFolder);
        if (precompileDx is not null)
            results.Add(precompileDx);

        // ── Layer 1: receipt / fork-gate + Layer 2 receipt flags ────────────
        bool receiptExpectedFailActualSuccess = false;
        bool receiptExpectedSuccessActualFail = false;
        foreach (var line in mismatches)
        {
            if (!line.StartsWith("receipt.status mismatch", StringComparison.Ordinal))
                continue;

            var expM = Regex.Match(line, @"expected=(True|False)", RegexOptions.IgnoreCase);
            var actM = Regex.Match(line, @"actual=(True|False)", RegexOptions.IgnoreCase);
            if (!expM.Success || !actM.Success)
                continue;

            bool expectedSuccess = expM.Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
            bool actualSuccess = actM.Groups[1].Value.Equals("True", StringComparison.OrdinalIgnoreCase);
            if (!expectedSuccess && actualSuccess) receiptExpectedFailActualSuccess = true;
            if (expectedSuccess && !actualSuccess) receiptExpectedSuccessActualFail = true;

            var forkDx = DivergenceDiagnostics.DiagnoseMissingForkGate(
                receiptExpectedFail: !expectedSuccess,
                receiptActualSuccess: actualSuccess,
                testCase.ForkName,
                testCase.FixturePath);
            if (forkDx is not null)
                results.Add(forkDx);
        }

        // ── Layer 1: nonce deltas ───────────────────────────────────────────
        bool senderNoncePlusOne = false;
        bool contractNonceZeroWhenExpectedOne = false;
        foreach (var line in mismatches)
        {
            if (!line.StartsWith("nonce mismatch", StringComparison.Ordinal))
                continue;

            var addr = ExtractAddress(line);
            var (exp, act) = ParseExpectedActualLong(line);
            if (exp is null || act is null)
                continue;

            bool isSender = addr is not null &&
                addr.Equals(testCase.Sender.ToString(), StringComparison.OrdinalIgnoreCase);

            if (isSender && act.Value - exp.Value == 1)
                senderNoncePlusOne = true;
            if (!isSender && exp.Value == 1 && act.Value == 0)
                contractNonceZeroWhenExpectedOne = true;

            var nonceDx = DivergenceDiagnostics.DiagnoseNonceDelta(exp.Value, act.Value, isSender);
            if (nonceDx is not null)
                results.Add(nonceDx);
        }

        // ── Layer 1: CREATE lifecycle cluster ───────────────────────────────
        var createDx = DivergenceDiagnostics.DiagnoseCreateLifecycleFailure(
            hasMissingAccount,
            hasNonceMismatch,
            hasCodeMismatch);
        if (createDx is not null)
            results.Add(createDx);

        // ── Layer 2: multi-signal structural rules ──────────────────────────
        var fork = testCase.ForkName ?? string.Empty;
        var ctx = new MismatchContext(
            ForkName: fork,
            FixturePath: testCase.FixturePath ?? string.Empty,
            EipFolder: eipFolder,
            GasUsed: report.GasUsed,
            GasRefundCounter: report.GasRefundCounter,
            HasBalanceMismatch: hasBalanceMismatch,
            HasStorageMismatch: hasStorageMismatch,
            HasNonceMismatch: hasNonceMismatch,
            HasCodeMismatch: hasCodeMismatch,
            HasReceiptMismatch: hasReceiptMismatch,
            HasMissingAccount: hasMissingAccount,
            HasUnexpectedAccount: hasUnexpectedAccount,
            StorageWriteWhenExpectedEmpty: hasStorageWriteWhenExpectedEmpty,
            StorageEmptyWhenExpectedNonZero: hasStorageEmptyWhenExpectedNonZero,
            BalanceActualBelowExpected: hasBalanceUndercharge,
            BalanceActualAboveExpected: hasBalanceOvercharge,
            PrimaryBalanceDeltaGas: primaryDeltaGas,
            ReceiptExpectedFailActualSuccess: receiptExpectedFailActualSuccess,
            ReceiptExpectedSuccessActualFail: receiptExpectedSuccessActualFail,
            SenderNoncePlusOne: senderNoncePlusOne,
            ContractNonceZeroWhenExpectedOne: contractNonceZeroWhenExpectedOne,
            TouchesCoinbaseBalance: touchesCoinbase,
            IsOsakaOrLater: IsForkAtLeast(fork, "Osaka"),
            IsPragueOrLater: IsForkAtLeast(fork, "Prague") || IsForkAtLeast(fork, "Osaka"));

        results.AddRange(StructuralPatternRules.Evaluate(ctx));

        return results;
    }

    private static bool IsForkAtLeast(string fork, string name) =>
        fork.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (name.Equals("Prague", StringComparison.OrdinalIgnoreCase) &&
            fork.Equals("Osaka", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Aggregate diagnoses across many cases: group by category+summary, rank by frequency.
    /// </summary>
    public static IReadOnlyList<Layer1DiagnosisBucket> Aggregate(
        IEnumerable<(string CaseId, DivergenceDiagnostics.Diagnosis Diagnosis)> hits,
        int maxBuckets = 25,
        int maxExamplesPerBucket = 5)
    {
        return hits
            .GroupBy(
                h => (h.Diagnosis.Category, h.Diagnosis.Summary, h.Diagnosis.ProtocolRule,
                      h.Diagnosis.CodeBoundary, h.Diagnosis.Confidence),
                h => h)
            .Select(g =>
            {
                var first = g.First().Diagnosis;
                var caseIds = g.Select(x => x.CaseId).Distinct(StringComparer.Ordinal).Take(maxExamplesPerBucket).ToList();
                return new Layer1DiagnosisBucket(
                    Category: first.Category,
                    Summary: first.Summary,
                    ProtocolRule: first.ProtocolRule,
                    CodeBoundary: first.CodeBoundary,
                    Confidence: first.Confidence.ToString(),
                    Occurrences: g.Count(),
                    SampleEvidence: first.Evidence,
                    SampleCaseIds: caseIds);
            })
            // Prefer stronger protocol hits for next-steps ranking, then volume.
            .OrderByDescending(b => ConfidenceRank(b.Confidence))
            .ThenByDescending(b => b.Occurrences)
            .ThenBy(b => b.Category, StringComparer.Ordinal)
            .Take(maxBuckets)
            .ToList();
    }

    private static int ConfidenceRank(string confidence) => confidence switch
    {
        "Certain" => 4,
        "High" => 3,
        "Medium" => 2,
        "Low" => 1,
        _ => 0
    };

    public static BigInteger ResolveEffectiveGasPrice(EelsStateCase testCase)
    {
        var tx = testCase.Transaction;
        var baseFee = new BigInteger(testCase.BlockContext.BaseFeePerGas);

        if (tx.TxType >= 2 && tx.MaxFeePerGas > BigInteger.Zero)
        {
            var priority = tx.MaxPriorityFeePerGas;
            return BigInteger.Min(tx.MaxFeePerGas, baseFee + priority);
        }

        if (tx.GasPrice > 0)
            return tx.GasPrice;

        // Some fixtures use gas_price=0; Layer 1 balance→gas conversion needs a positive divisor.
        // Fall back to 1 wei so constant-matching still works when delta is already in gas units.
        return BigInteger.One;
    }

    private static string ExtractEipFolder(string fixturePath)
    {
        // .../state_tests/osaka/eip7951_p256verify/test_foo.json → eip7951_p256verify
        var parts = fixturePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].StartsWith("eip", StringComparison.OrdinalIgnoreCase))
                return parts[i].ToLowerInvariant();
        }
        // fall back to parent folder name
        return parts.Length >= 2 ? parts[^2].ToLowerInvariant() : fixturePath;
    }

    private static string? ExtractAddress(string mismatch)
    {
        var m = Regex.Match(mismatch, @"for (0x[0-9a-fA-F]{20,40})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static (BigInteger? exp, BigInteger? act) ParseExpectedActualBigInt(string mismatch)
    {
        var expM = Regex.Match(mismatch, @"expected=(\S+)");
        var actM = Regex.Match(mismatch, @"actual=(\S+)");
        if (!expM.Success || !actM.Success) return (null, null);
        return (TryParseBigInt(expM.Groups[1].Value), TryParseBigInt(actM.Groups[1].Value));
    }

    private static (long? exp, long? act) ParseExpectedActualLong(string mismatch)
    {
        var expM = Regex.Match(mismatch, @"expected=(\d+)");
        var actM = Regex.Match(mismatch, @"actual=(\d+)");
        if (!expM.Success || !actM.Success) return (null, null);
        if (!long.TryParse(expM.Groups[1].Value, out var exp)) return (null, null);
        if (!long.TryParse(actM.Groups[1].Value, out var act)) return (null, null);
        return (exp, act);
    }

    private static BigInteger? TryParseBigInt(string s)
    {
        s = s.TrimEnd(',', ';', '.');
        try
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return BigInteger.Parse("0" + s[2..], System.Globalization.NumberStyles.HexNumber);
            return BigInteger.Parse(s);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Aggregated Layer 1 hit for taxonomy / UI / RPC surfaces.</summary>
public sealed record Layer1DiagnosisBucket(
    string Category,
    string Summary,
    string ProtocolRule,
    string CodeBoundary,
    string Confidence,
    int Occurrences,
    string SampleEvidence,
    IReadOnlyList<string> SampleCaseIds);
