namespace Schlieren.Core.Execution.Causal;

/// <summary>
/// Unified failure clustering service. Groups failures by their causal fingerprint
/// (phase + ruleId + fork) so that N failures caused by the same root defect are
/// reported as one defect family affecting N cases.
///
/// Replaces the three separate clustering implementations:
///   - Schlieren.Tests/Campaigns/Synthetic/SyntheticDifferentialRunner.cs → FailureClusterer
///   - Schlieren.Tests/Campaigns/PathologicalExecution/PathologicalDifferentialRunner.cs → PathologicalClusterer
///   - Schlieren.EELS.Tests/Conformance/EelsTaxonomyAnalyzer.cs → inline bucketing
/// </summary>
public static class FailureClusteringService
{
    /// <summary>
    /// A cluster of failures sharing the same causal fingerprint.
    /// </summary>
    public sealed record FailureCluster
    {
        /// <summary>Human-readable family identifier, e.g. "GAS / CREATE.INITCODE_WORD / Frontier"</summary>
        public required string FamilyId { get; init; }

        /// <summary>Causal fingerprint (phase::ruleId::fork)</summary>
        public required string Fingerprint { get; init; }

        /// <summary>Root diagnosis rule ID</summary>
        public required string RuleId { get; init; }

        /// <summary>Execution phase where the first divergence occurs</summary>
        public required ExecutionPhase Phase { get; init; }

        /// <summary>Diagnosis grade of the root cause</summary>
        public required DiagnosisGrade Grade { get; init; }

        /// <summary>Number of cases in this cluster</summary>
        public required int Count { get; init; }

        /// <summary>Gas delta (if consistent across cases)</summary>
        public long? GasDelta { get; init; }

        /// <summary>Set of fork names present in this cluster</summary>
        public required IReadOnlyList<string> Forks { get; init; }

        /// <summary>Individual case IDs in this cluster</summary>
        public required IReadOnlyList<string> CaseIds { get; init; }

        /// <summary>Root diagnosis title (from the highest-scoring member)</summary>
        public required string Title { get; init; }

        /// <summary>Suggested fix (from the highest-scoring member)</summary>
        public string? LikelyFix { get; init; }

        /// <summary>Code boundary (from the highest-scoring member)</summary>
        public string? CodeBoundary { get; init; }
    }

    /// <summary>
    /// A single failure with its case ID and causal diagnosis.
    /// </summary>
    public sealed record FailureEntry(
        string CaseId,
        string ForkName,
        CausalDiagnosisEngine.Report Report);

    /// <summary>
    /// Cluster a set of failure entries by causal fingerprint.
    /// Returns clusters ordered by count descending (largest defect families first).
    /// </summary>
    public static IReadOnlyList<FailureCluster> Cluster(IEnumerable<FailureEntry> entries)
    {
        return entries
            .GroupBy(e => e.Report.Fingerprint, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.OrderByDescending(e => e.Report.Root.Score).First();
                var root = first.Report.Root;
                var forks = g.Select(e => e.ForkName).Distinct().OrderBy(f => f).ToList();
                var caseIds = g.Select(e => e.CaseId).ToList();
                var gasDelta = g.All(e => e.Report.Root.GasDelta == root.GasDelta)
                    ? root.GasDelta
                    : null;

                return new FailureCluster
                {
                    FamilyId = $"{root.Phase.ToLabel()} / {root.RuleId} ({g.Count()} cases)",
                    Fingerprint = first.Report.Fingerprint,
                    RuleId = root.RuleId,
                    Phase = root.Phase,
                    Grade = root.Grade,
                    Count = g.Count(),
                    GasDelta = gasDelta,
                    Forks = forks,
                    CaseIds = caseIds,
                    Title = root.Title,
                    LikelyFix = root.LikelyFix,
                    CodeBoundary = root.CodeBoundary
                };
            })
            .OrderByDescending(c => c.Count)
            .ThenByDescending(c => c.Grade)
            .ThenBy(c => c.Phase)
            .ToList();
    }

    /// <summary>
    /// Convenience: cluster from FailureEvidence instances directly.
    /// Runs CausalDiagnosisEngine.Analyze on each entry.
    /// </summary>
    public static IReadOnlyList<FailureCluster> ClusterFromEvidence(
        IEnumerable<(string CaseId, FailureEvidence Evidence)> entries)
    {
        var analyzed = entries.Select(e =>
        {
            var report = CausalDiagnosisEngine.Analyze(e.Evidence);
            return new FailureEntry(e.CaseId, e.Evidence.ForkName, report);
        });

        return Cluster(analyzed);
    }

    /// <summary>
    /// Render a cluster summary as human-readable text.
    /// </summary>
    public static string RenderSummary(IReadOnlyList<FailureCluster> clusters)
    {
        if (clusters.Count == 0)
            return "No failure clusters.";

        var totalCases = clusters.Sum(c => c.Count);
        var lines = new List<string>
        {
            $"{totalCases} failures → {clusters.Count} root cause{(clusters.Count == 1 ? "" : "s")}",
            ""
        };

        for (int i = 0; i < clusters.Count; i++)
        {
            var cl = clusters[i];
            var fix = cl.LikelyFix != null ? $"  Fix: {cl.LikelyFix}" : "";
            lines.Add($"#{i + 1}  {cl.Title} {"." + new string('.', Math.Max(1, 50 - cl.Title.Length))} {cl.Count} cases");
            lines.Add($"     Rule: {cl.RuleId}  Phase: {cl.Phase.ToLabel()}  Grade: {cl.Grade}");
            lines.Add($"     Forks: {string.Join(", ", cl.Forks)}");
            if (cl.GasDelta.HasValue)
                lines.Add($"     Gas delta: {cl.GasDelta.Value:+#;-#;0}");
            if (!string.IsNullOrEmpty(fix))
                lines.Add($"    {fix}");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
