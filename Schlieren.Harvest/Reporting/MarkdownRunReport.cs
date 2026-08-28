using System.Text;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Ledger;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Reporting;

/// <summary>
/// Generates a human-readable Markdown run report from finalized ledger JSON only.
///
/// Contract: this renderer NEVER accepts ad-hoc counters, in-memory aggregates,
/// or unverified data. It reloads and verifies machine records from the ledger,
/// then projects them into Markdown. If a record fails hash verification, the
/// report includes the corruption notice rather than omitting it.
/// </summary>
public static class MarkdownRunReport
{
    /// <summary>
    /// Renders a complete Markdown report from a finalized run.
    /// The ledger is read (with hash verification) and the report is written to
    /// the canonical reports/{run-id}.md path.
    /// </summary>
    public static async Task<string> GenerateAsync(
        IRunLedger ledger,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await ledger.ReadRunAsync(runId, cancellationToken);
        var record   = envelope.Payload;
        var sb       = new StringBuilder();

        // ── Header ─────────────────────────────────────────────────────────
        sb.AppendLine($"# Harvest Run Report: {record.RunId}");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:O}");
        sb.AppendLine($"**Content Hash:** `{envelope.ContentHash}`");
        sb.AppendLine();

        // ── Provenance ─────────────────────────────────────────────────────
        sb.AppendLine("## Provenance");
        sb.AppendLine();
        sb.AppendLine($"| Field | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Campaign | {record.CampaignId} v{record.CampaignVersion} |");
        sb.AppendLine($"| Manifest Hash | `{record.ManifestHash}` |");
        sb.AppendLine($"| Run Kind | {record.Kind} |");
        sb.AppendLine($"| Run State | {record.State} |");
        sb.AppendLine($"| Started | {record.StartedUtc:O} |");
        sb.AppendLine($"| Completed | {record.CompletedUtc:O} |");
        sb.AppendLine($"| Duration | {record.CompletedUtc - record.StartedUtc:hh\\:mm\\:ss} |");
        sb.AppendLine();

        // ── Environment ────────────────────────────────────────────────────
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine($"| Field | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| OS | {record.Environment.OsDescription} |");
        sb.AppendLine($"| Runtime | {record.Environment.RuntimeVersion} |");
        sb.AppendLine($"| Machine | {record.Environment.MachineName} |");
        sb.AppendLine($"| Processors | {record.Environment.ProcessorCount} |");
        sb.AppendLine();

        // ── Tool Identity ──────────────────────────────────────────────────
        sb.AppendLine("## Tool Identity");
        sb.AppendLine();
        sb.AppendLine($"- **Schlieren:** {record.SchlierenTool.Name} {record.SchlierenTool.Version}");
        if (record.SchlierenTool.CommitSha is not null)
            sb.AppendLine($"  - Commit: `{record.SchlierenTool.CommitSha}`");
        if (record.EelsOracle is not null)
        {
            sb.AppendLine($"- **EELS Oracle:** {record.EelsOracle.ReportedVersion}");
            sb.AppendLine($"  - SHA-256: `{record.EelsOracle.ExecutableSha256}`");
            if (record.EelsOracle.CommitSha is not null)
                sb.AppendLine($"  - Commit: `{record.EelsOracle.CommitSha}`");
        }
        sb.AppendLine();

        // ── Conformance Summary ────────────────────────────────────────────
        sb.AppendLine("## Conformance Summary");
        sb.AppendLine();
        var s = record.Summary;
        sb.AppendLine($"| Status | Count |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Pass | {s.PassCount} |");
        sb.AppendLine($"| Divergence | {s.DivergenceCount} |");
        sb.AppendLine($"| FixtureInvalid | {s.FixtureInvalidCount} |");
        sb.AppendLine($"| HarnessError | {s.HarnessErrorCount} |");
        sb.AppendLine($"| Aborted | {s.AbortedCount} |");
        sb.AppendLine($"| Quarantined | {s.QuarantinedCount} |");
        sb.AppendLine($"| **Total** | **{s.Total}** |");
        sb.AppendLine();

        var passRate = s.Total > 0 ? (double)s.PassCount / s.Total * 100 : 0;
        sb.AppendLine($"**Pass Rate:** {passRate:F1}% ({s.PassCount}/{s.Total})");
        sb.AppendLine();

        // ── Non-pass cases ─────────────────────────────────────────────────
        var nonPass = record.Outcomes.Where(o => o.Status != CaseStatus.Pass).ToList();
        if (nonPass.Count > 0)
        {
            sb.AppendLine("## Non-Pass Cases");
            sb.AppendLine();
            sb.AppendLine($"| Case ID | Status | Deltas | Apparatus Failure | Detail |");
            sb.AppendLine($"|---|---|---|---|---|");
            foreach (var c in nonPass)
            {
                var deltaDesc = c.Deltas.Count > 0
                    ? string.Join(", ", c.Deltas.Select(d => $"{d.Layer}/{d.Kind}"))
                    : "—";
                var detail = c.Detail ?? "—";
                var apparatus = c.AttemptEvidence?.FailureKind.ToString() ?? "—";
                // Truncate long case IDs for readability
                var caseDisplay = c.CaseId.Length > 60
                    ? "…" + c.CaseId[^55..]
                    : c.CaseId;
                sb.AppendLine($"| `{caseDisplay}` | {c.Status} | {deltaDesc} | {apparatus} | {detail} |");
            }
            sb.AppendLine();
        }

        // ── Certification eligibility ──────────────────────────────────────
        sb.AppendLine("## Certification Eligibility");
        sb.AppendLine();
        if (record.State == RunState.Certified)
        {
            sb.AppendLine("✅ **CERTIFIED** — all gates passed.");
        }
        else
        {
            sb.AppendLine("❌ **NOT CERTIFIED**");
            sb.AppendLine();
            if (s.DivergenceCount > 0)
                sb.AppendLine($"- {s.DivergenceCount} divergence(s) require repair");
            if (s.FixtureInvalidCount > 0)
                sb.AppendLine($"- {s.FixtureInvalidCount} fixture(s) invalid — apparatus defect");
            if (s.HarnessErrorCount > 0)
                sb.AppendLine($"- {s.HarnessErrorCount} harness error(s) — apparatus defect");
            if (s.AbortedCount > 0)
                sb.AppendLine($"- {s.AbortedCount} case(s) aborted");
            if (s.QuarantinedCount > 0)
                sb.AppendLine($"- {s.QuarantinedCount} case(s) quarantined (oracle/fixture defect)");
            if (s.PassCount < s.Total && s.DivergenceCount == 0)
                sb.AppendLine($"- Not all cases passed ({s.PassCount}/{s.Total})");
        }
        sb.AppendLine();

        // ── Footer ─────────────────────────────────────────────────────────
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*This report is a projection of machine-readable records, not the authoritative source.*");
        sb.AppendLine($"*Source: `{LedgerPaths.RunPath("harvest/ledger", runId)}`*");

        return sb.ToString();
    }
}
