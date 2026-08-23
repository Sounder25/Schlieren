using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.EELS.Tests.Harness;

namespace Schlieren.EELS.Tests.Conformance;

/// <summary>
/// Phase 2 bridge: feed typed EELS discrepancies into Layer 1 (<see cref="DivergenceDiagnostics"/>)
/// and Layer 2 (<see cref="StructuralPatternRules"/>) for taxonomy / UI / product surfaces.
/// </summary>
public static class Layer1DiagnosisBridge
{
    /// <summary>
    /// Well-known EELS fixture coinbase used in many state tests.
    /// </summary>
    public const string EelsFixtureCoinbase = "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba";

    /// <summary>
    /// Run Layer 1 + Layer 2 rules against one failed case's typed discrepancies.
    /// </summary>
    public static IReadOnlyList<DivergenceDiagnostics.Diagnosis> DiagnoseCase(
        EelsStateCase testCase,
        EelsCaseExecutionReport report)
        => DiagnoseCausal(testCase, report).Diagnoses;

    public sealed record CausalDiagnosisBundle(
        IReadOnlyList<DivergenceDiagnostics.Diagnosis> Diagnoses,
        string Fingerprint,
        string Title,
        string Grade,
        string Phase,
        string RuleId,
        string InspectorBody);

    /// <summary>Causal fingerprint + ranked diagnoses for Case Inspector / clusters.</summary>
    public static CausalDiagnosisBundle DiagnoseCausal(
        EelsStateCase testCase,
        EelsCaseExecutionReport report)
    {
        var results = new List<DivergenceDiagnostics.Diagnosis>();
        var discrepancies = report.Discrepancies ?? Array.Empty<StateDiscrepancy>();
        if (discrepancies.Count == 0)
            return new(results, "none", "", "POSSIBLE", "FINAL", "none", "");

        var ev = FailureEvidenceFactory.From(
            report.CaseId,
            testCase.ForkName ?? "",
            testCase.FixturePath ?? "",
            testCase.Transaction,
            testCase.Sender,
            testCase.BlockContext.Coinbase,
            report.GasUsed,
            report.GasRefundCounter,
            report.ExecutionSucceeded,
            report.Error,
            report.LastOpcode,
            report.LastPc,
            testCase.ExpectedException,
            testCase.ExpectedReceiptStatus,
            discrepancies);

        var causal = CausalDiagnosisEngine.Analyze(ev);
        foreach (var d in causal.Ranked)
            results.Add(ToDiagnosis(d));

        var body = FormatInspector(causal.Root);

        if (causal.Root.Grade is DiagnosisGrade.Proven or DiagnosisGrade.Strong)
        {
            return new CausalDiagnosisBundle(
                results.Take(3).ToList(), causal.Fingerprint, causal.Root.Title,
                causal.Root.Grade.ToString().ToUpperInvariant(),
                causal.FirstPhase.ToLabel(), causal.Root.RuleId, body);
        }

        var gasPrice = ResolveEffectiveGasPrice(testCase);
        var eipFolder = ExtractEipFolder(testCase.FixturePath);

        bool hasMissingAccount = discrepancies.Any(item => item.Kind == DiscrepancyKind.MissingAccount);
        bool hasNonceMismatch = discrepancies.Any(item => item.Kind == DiscrepancyKind.Nonce);
        bool hasCodeMismatch = discrepancies.Any(item => item.Kind == DiscrepancyKind.Code);
        bool hasBalanceMismatch = discrepancies.Any(item => item.Kind == DiscrepancyKind.Balance);
        bool hasStorageMismatch = discrepancies.Any(item => item.Kind == DiscrepancyKind.Storage);
        bool hasReceiptMismatch = discrepancies.Any(item => item.Kind == DiscrepancyKind.ReceiptStatus);
        bool hasUnexpectedAccount = discrepancies.Any(item => item.Kind == DiscrepancyKind.UnexpectedAccount);
        bool hasStorageWriteWhenExpectedEmpty = discrepancies.Any(item =>
            item.Kind == DiscrepancyKind.Storage && item.ExpectedNumber == BigInteger.Zero && item.ActualNumber != BigInteger.Zero);
        bool hasStorageEmptyWhenExpectedNonZero = discrepancies.Any(item =>
            item.Kind == DiscrepancyKind.Storage && item.ActualNumber == BigInteger.Zero && item.ExpectedNumber != BigInteger.Zero);

        // ── Layer 1: balance gas constants ──────────────────────────────────
        bool hasBalanceUndercharge = false;
        bool hasBalanceOvercharge = false;
        long? primaryDeltaGas = null;
        bool touchesCoinbase = false;

        foreach (var discrepancy in discrepancies.Where(item => item.Kind == DiscrepancyKind.Balance))
        {
            if (discrepancy.Address is not { } address ||
                discrepancy.ExpectedNumber is not { } expected ||
                discrepancy.ActualNumber is not { } actual)
                continue;

            if (address == testCase.BlockContext.Coinbase)
                touchesCoinbase = true;

            var deltaWei = actual - expected;
            if (deltaWei < 0) hasBalanceUndercharge = true;
            if (deltaWei > 0) hasBalanceOvercharge = true;

            if (gasPrice > 0)
            {
                results.AddRange(DivergenceDiagnostics.DiagnoseBalanceDelta(deltaWei, gasPrice));
                try
                {
                    var dg = (long)(deltaWei / gasPrice);
                    // Prefer sender residual as primary when multiple balances diverge
                    bool isSender = address == testCase.Sender;
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
        foreach (var discrepancy in discrepancies.Where(item => item.Kind == DiscrepancyKind.ReceiptStatus))
        {
            if (discrepancy.ExpectedBoolean is not { } expectedSuccess ||
                discrepancy.ActualBoolean is not { } actualSuccess)
                continue;
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
        foreach (var discrepancy in discrepancies.Where(item => item.Kind == DiscrepancyKind.Nonce))
        {
            if (discrepancy.Address is not { } address ||
                discrepancy.ExpectedNumber is not { } expected ||
                discrepancy.ActualNumber is not { } actual)
                continue;

            bool isSender = address == testCase.Sender;

            if (isSender && actual - expected == 1)
                senderNoncePlusOne = true;
            if (!isSender && expected == 1 && actual == 0)
                contractNonceZeroWhenExpectedOne = true;

            if (expected < long.MinValue || expected > long.MaxValue || actual < long.MinValue || actual > long.MaxValue)
                continue;
            var nonceDx = DivergenceDiagnostics.DiagnoseNonceDelta((long)expected, (long)actual, isSender);
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

        return new CausalDiagnosisBundle(
            results, causal.Fingerprint, causal.Root.Title,
            causal.Root.Grade.ToString().ToUpperInvariant(),
            causal.FirstPhase.ToLabel(), causal.Root.RuleId, body);
    }

    private static DivergenceDiagnostics.Diagnosis ToDiagnosis(ScoredDiagnosis d)
    {
        var conf = d.Grade switch
        {
            DiagnosisGrade.Proven => DivergenceDiagnostics.Confidence.Certain,
            DiagnosisGrade.Strong => DivergenceDiagnostics.Confidence.High,
            _ => DivergenceDiagnostics.Confidence.Medium
        };
        return new DivergenceDiagnostics.Diagnosis(
            Category: d.RuleId,
            Summary: d.Title,
            ProtocolRule: d.ProtocolRule,
            CodeBoundary: d.CodeBoundary,
            Confidence: conf,
            Evidence: $"[{d.Grade.ToString().ToUpperInvariant()} {d.Score}] {d.Proof}");
    }

    private static string FormatInspector(ScoredDiagnosis d) =>
        $"""
        ROOT CAUSE — {d.Grade.ToString().ToUpperInvariant()}  ({d.Score})

        Rule:
        {d.RuleId}

        Phase:
        {d.Phase.ToLabel()}

        Why:
        {d.Why}

        Proof:
        {d.Proof}

        Consequences:
        {d.Consequences}

        Likely fix:
        {d.LikelyFix}

        Implementation:
        {d.CodeBoundary}

        Fingerprint:
        {d.Fingerprint}
        """;

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
        "PROVEN" or "Proven" or "Certain" => 5,
        "STRONG" or "Strong" or "High" => 4,
        "POSSIBLE" or "Possible" or "Medium" => 2,
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
