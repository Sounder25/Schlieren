using Schlieren.Core.Execution.Causal;

namespace Schlieren.Core.Execution.Inspect;

/// <summary>
/// The flagship output format for Schlieren protocol-level explanation.
/// Renders execution analysis as the structured diagnosis format:
///
///   First divergence: depth-3 DELEGATECALL
///   Gas delta: +2,600
///   Expected access: warm
///   Actual access: cold
///   Likely subsystem: transaction access-set propagation
///   Confidence: high
///
/// Built from CausalDiagnosisEngine.Report + TraceDivergence + ExecutionFingerprint.
/// </summary>
public sealed record ProtocolDiagnosisReport
{
    /// <summary>Location of the first meaningful divergence, e.g. "depth-3 DELEGATECALL"</summary>
    public required string FirstDivergence { get; init; }

    /// <summary>Gas delta in units, e.g. +2600</summary>
    public long? GasDelta { get; init; }

    /// <summary>What the specification expected, e.g. "warm"</summary>
    public string? ExpectedState { get; init; }

    /// <summary>What actually happened, e.g. "cold"</summary>
    public string? ActualState { get; init; }

    /// <summary>Which protocol subsystem is implicated, e.g. "transaction access-set propagation"</summary>
    public required string LikelySubsystem { get; init; }

    /// <summary>Confidence level: "high", "medium", "low"</summary>
    public required string Confidence { get; init; }

    /// <summary>The EIP or protocol rule responsible</summary>
    public string? ProtocolRule { get; init; }

    /// <summary>Fingerprint for clustering identical failures</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Why this divergence occurred (human-readable)</summary>
    public string? Why { get; init; }

    /// <summary>Formal proof of the diagnosis (formula match, delta arithmetic)</summary>
    public string? Proof { get; init; }

    /// <summary>Where in source to look for the fix</summary>
    public string? CodeBoundary { get; init; }

    /// <summary>Causal consequence chain</summary>
    public string? Consequences { get; init; }

    /// <summary>Suggested fix</summary>
    public string? LikelyFix { get; init; }

    /// <summary>Supporting evidence lines</summary>
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    /// <summary>Additional ranked hypotheses</summary>
    public IReadOnlyList<ProtocolDiagnosisCandidate> AlternateCandidates { get; init; } = Array.Empty<ProtocolDiagnosisCandidate>();

    /// <summary>
    /// Render as human-readable text block.
    /// </summary>
    public string ToText()
    {
        var lines = new List<string>
        {
            $"First divergence: {FirstDivergence}"
        };

        if (GasDelta.HasValue)
            lines.Add($"Gas delta: {GasDelta.Value:+#;-#;0}");
        if (ExpectedState != null)
            lines.Add($"Expected access: {ExpectedState}");
        if (ActualState != null)
            lines.Add($"Actual access: {ActualState}");
        lines.Add($"Likely subsystem: {LikelySubsystem}");
        lines.Add($"Confidence: {Confidence}");

        if (ProtocolRule != null)
            lines.Add($"Protocol rule: {ProtocolRule}");
        if (Why != null)
            lines.Add($"Why: {Why}");
        if (Proof != null)
            lines.Add($"Proof: {Proof}");
        if (Consequences != null)
            lines.Add($"Consequences: {Consequences}");
        if (LikelyFix != null)
            lines.Add($"Suggested fix: {LikelyFix}");
        if (CodeBoundary != null)
            lines.Add($"Code boundary: {CodeBoundary}");

        if (Evidence.Count > 0)
        {
            lines.Add("Evidence:");
            foreach (var e in Evidence)
                lines.Add($"  • {e}");
        }

        if (AlternateCandidates.Count > 0)
        {
            lines.Add($"Alternate hypotheses ({AlternateCandidates.Count}):");
            foreach (var c in AlternateCandidates)
                lines.Add($"  • [{c.Confidence}] {c.Title} ({c.RuleId})");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A ranked alternate hypothesis.
/// </summary>
public sealed record ProtocolDiagnosisCandidate(
    string RuleId,
    string Title,
    string Confidence,
    int Score,
    string Phase);

/// <summary>
/// Builds a <see cref="ProtocolDiagnosisReport"/> from available analysis artifacts.
/// </summary>
public static class ProtocolDiagnosisReportBuilder
{
    /// <summary>
    /// Build from a CausalDiagnosisEngine report (EELS fixture analysis).
    /// </summary>
    public static ProtocolDiagnosisReport FromCausalReport(CausalDiagnosisEngine.Report report)
    {
        var root = report.Root;
        var phase = report.FirstPhase;

        var firstDiv = $"{phase.ToLabel()}";
        if (root.GasDelta.HasValue)
            firstDiv += $" (gas delta: {root.GasDelta.Value:+#;-#;0})";

        var confidence = root.Grade switch
        {
            DiagnosisGrade.Proven => "high",
            DiagnosisGrade.Strong => "high",
            DiagnosisGrade.Possible => "medium",
            _ => "low"
        };

        var (expectedState, actualState) = InferAccessState(root);

        var alternates = report.Ranked
            .Skip(1) // Skip root, already used
            .Select(d => new ProtocolDiagnosisCandidate(
                RuleId: d.RuleId,
                Title: d.Title,
                Confidence: d.Grade switch
                {
                    DiagnosisGrade.Proven => "high",
                    DiagnosisGrade.Strong => "high",
                    _ => "medium"
                },
                Score: d.Score,
                Phase: d.Phase.ToLabel()))
            .ToList();

        return new ProtocolDiagnosisReport
        {
            FirstDivergence = firstDiv,
            GasDelta = root.GasDelta,
            ExpectedState = expectedState,
            ActualState = actualState,
            LikelySubsystem = InferSubsystem(root),
            Confidence = confidence,
            ProtocolRule = root.ProtocolRule,
            Fingerprint = report.Fingerprint,
            Why = root.Why,
            Proof = root.Proof,
            CodeBoundary = root.CodeBoundary,
            Consequences = root.Consequences,
            LikelyFix = root.LikelyFix,
            Evidence = new List<string>
            {
                $"RuleId: {root.RuleId}",
                $"Phase: {root.Phase.ToLabel()}",
                $"Score: {root.Score}",
                root.Proof
            },
            AlternateCandidates = alternates
        };
    }

    /// <summary>
    /// Build from a TraceDivergenceLocator result (step-level trace comparison).
    /// </summary>
    public static ProtocolDiagnosisReport FromTraceDivergence(TraceDivergenceLocator.TraceDivergence div)
    {
        if (div.IsMatch)
        {
            return new ProtocolDiagnosisReport
            {
                FirstDivergence = "None — traces match",
                LikelySubsystem = "N/A",
                Confidence = "high",
                Evidence = new[] { "Traces are identical step-by-step." }
            };
        }

        var firstDiv = div.DivergentDepth.HasValue && div.DivergentOpcode != null
            ? $"depth-{div.DivergentDepth} {div.DivergentOpcode}"
            : $"step {div.DivergentStepIndex}";

        if (div.DivergentOpcode != null && div.ExpectedOpcode != null && div.DivergentOpcode != div.ExpectedOpcode)
            firstDiv += $" (expected {div.ExpectedOpcode})";

        var (expected, actual) = InferAccessStateFromGas(div.GasDelta);

        return new ProtocolDiagnosisReport
        {
            FirstDivergence = firstDiv,
            GasDelta = div.GasDelta,
            ExpectedState = expected,
            ActualState = actual,
            LikelySubsystem = div.LikelySubsystem,
            Confidence = div.GasDelta.HasValue ? "high" : "medium",
            Evidence = new[] { div.Detail }
        };
    }

    /// <summary>
    /// Combine causal and trace-level analysis into one report.
    /// Causal analysis provides the protocol-level "why"; trace provides the "where".
    /// </summary>
    public static ProtocolDiagnosisReport Combined(
        CausalDiagnosisEngine.Report? causal,
        TraceDivergenceLocator.TraceDivergence? trace)
    {
        if (causal == null && trace == null)
            return new ProtocolDiagnosisReport
            {
                FirstDivergence = "Unknown",
                LikelySubsystem = "Unknown",
                Confidence = "low"
            };

        // If we have both, enrich the causal report with trace-level location
        if (causal != null && trace != null && !trace.IsMatch)
        {
            var causalReport = FromCausalReport(causal);

            var traceLoc = trace.DivergentDepth.HasValue && trace.DivergentOpcode != null
                ? $"depth-{trace.DivergentDepth} {trace.DivergentOpcode} (step {trace.DivergentStepIndex}, PC={trace.DivergentPc})"
                : $"step {trace.DivergentStepIndex}";

            var evidence = new List<string>(causalReport.Evidence)
            {
                $"Trace-level first divergence: {traceLoc}",
                trace.Detail
            };

            return causalReport with
            {
                FirstDivergence = traceLoc,
                GasDelta = trace.GasDelta ?? causalReport.GasDelta,
                Evidence = evidence
            };
        }

        if (causal != null) return FromCausalReport(causal);
        return FromTraceDivergence(trace!);
    }

    private static string InferSubsystem(ScoredDiagnosis root) => root.RuleId switch
    {
        var r when r.StartsWith("ACCESS") => "transaction access-set propagation",
        var r when r.StartsWith("SSTORE") => "storage gas metering (EIP-2200)",
        var r when r.StartsWith("TX.") => "transaction validation / intrinsic gas",
        var r when r.StartsWith("CREATE") => "CREATE lifecycle / initcode metering",
        var r when r.StartsWith("HALT") => "opcode activation / exceptional halt",
        var r when r.StartsWith("PRECOMPILE") => "precompile gas schedule",
        var r when r.StartsWith("SELFDESTRUCT") => "SELFDESTRUCT refund / new-account",
        var r when r.StartsWith("SETTLE") => "settlement / refund cap",
        var r when r.StartsWith("STATE") => "final state commit",
        _ => root.Title
    };

    private static (string? expected, string? actual) InferAccessState(ScoredDiagnosis root)
    {
        if (root.GasDelta == 2600) return ("warm", "cold");
        if (root.GasDelta == -2600) return ("cold", "warm");
        if (root.GasDelta == 2100) return ("warm (SLOAD)", "cold (SLOAD)");
        if (root.GasDelta == -2100) return ("cold (SLOAD)", "warm (SLOAD)");
        if (root.GasDelta == 2500) return ("warm (100)", "cold (2600)");
        return (null, null);
    }

    /// <summary>
    /// Map extra-cost <paramref name="gasDelta"/> (actualCost − expectedCost, or
    /// expectedRemaining − actualRemaining) to warm/cold labels.
    /// Positive: Schlieren overcharged → actual cold, expected warm.
    /// Negative: Schlieren undercharged → actual warm, expected cold.
    /// </summary>
    private static (string? expected, string? actual) InferAccessStateFromGas(long? gasDelta)
    {
        if (!gasDelta.HasValue) return (null, null);
        var abs = Math.Abs(gasDelta.Value);
        return abs switch
        {
            2600 => gasDelta > 0 ? ("warm", "cold") : ("cold", "warm"),
            2100 => gasDelta > 0 ? ("warm (SLOAD)", "cold (SLOAD)") : ("cold (SLOAD)", "warm (SLOAD)"),
            2500 => gasDelta > 0 ? ("warm (100)", "cold (2600)") : ("cold (2600)", "warm (100)"),
            _ => (null, null)
        };
    }
}
