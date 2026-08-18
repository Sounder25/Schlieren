using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Maps a PathologicalResult to the canonical FAM-* family identifier
/// and derives sub-classification from the exception type.
///
/// The family taxonomy:
///
///   FAM-OVERFLOW-MODEXP-GAS       — ModExp declared lengths overflow gas formula
///   FAM-OVERFLOW-MEMORY-OFFSET    — BigInteger→int/ulong narrowing in memory ops
///   FAM-COPY-RANGE                — Copy opcode offset/size arithmetic overflow
///   FAM-STACK-LIMIT               — Stack push/pop beyond 1024
///   FAM-DEPTH-LIMIT               — Call depth beyond 1024
///   FAM-PRECOMPILE-MALFORMED      — Precompile input malformed / pathological
///   FAM-CREATE-LIFECYCLE          — CREATE/CREATE2 pathological inputs
///   FAM-RETURNDATA-RANGE          — RETURNDATACOPY offset/size outside buffer
///   FAM-UNHANDLED-ENGINE-EXCEPTION — Catch-all for escaping .NET exceptions
///   FAM-ARITHMETIC-BOUNDARY       — Arithmetic ops at ±2^255, 2^256-1, div-by-0
///   FAM-EXCEPTIONAL-HALT          — OOG, INVALID, stack under/overflow, bad jump
/// </summary>
public static class FailureClassifier
{
    // ── Exception type → family ───────────────────────────────────────────────

    private static readonly Dictionary<string, string> ExceptionFamilyMap = new()
    {
        ["OverflowException"]            = FailureFamily.OverflowMemoryOffset,
        ["OverflowException/ModExp"]     = FailureFamily.OverflowModexpGas,
        ["ArgumentOutOfRangeException"]  = FailureFamily.OverflowMemoryOffset,
        ["IndexOutOfRangeException"]     = FailureFamily.CopyRange,
        ["OutOfMemoryException"]         = FailureFamily.OverflowMemoryOffset,
        ["NullReferenceException"]       = FailureFamily.UnhandledEngineException,
        ["InvalidOperationException"]    = FailureFamily.UnhandledEngineException,
        ["DivideByZeroException"]        = FailureFamily.ArithmeticBoundary,
        ["StackOverflowException"]       = FailureFamily.StackLimit,
    };

    // ── Primary classification ────────────────────────────────────────────────

    /// <summary>
    /// Classify a defect result into a FAM-* family.
    /// Uses both the case's declared FamilyId and the actual exception type
    /// for highest precision.
    /// </summary>
    public static string Classify(PathologicalResult r)
    {
        if (!r.IsDefect) return "PASS";

        var exType = ShortExceptionName(r.ExceptionType);

        // Exception-type-first overrides (most specific)
        if (exType == "IndexOutOfRangeException") return FailureFamily.CopyRange;
        if (exType == "OutOfMemoryException")     return FailureFamily.OverflowMemoryOffset;
        if (exType == "StackOverflowException")   return FailureFamily.StackLimit;
        if (exType == "NullReferenceException")   return FailureFamily.UnhandledEngineException;

        // Arithmetic exception inside ModExp → separate family
        if (exType == "OverflowException"
            && r.Case.Family == PathFamily.PrecompilePathological
            && r.Case.ModexpKind.HasValue)
            return FailureFamily.OverflowModexpGas;

        if (exType == "OverflowException")
            return FailureFamily.OverflowMemoryOffset;

        if (exType == "ArgumentOutOfRangeException")
        {
            if (r.Case.CopySource == CopySource.Returndata
                || r.Case.CopyKind.HasValue)
                return FailureFamily.ReturndataRange;
            return FailureFamily.OverflowMemoryOffset;
        }

        // Fall back to the declared family
        return r.Case.FamilyId;
    }

    // ── Sub-classification ────────────────────────────────────────────────────

    /// <summary>
    /// One-line human diagnostic combining family + exception + opcode context.
    /// Used for dashboard output and test failure messages.
    /// </summary>
    public static string Diagnose(PathologicalResult r)
    {
        if (!r.IsDefect) return "PASS";

        var family = Classify(r);
        var exType = ShortExceptionName(r.ExceptionType);
        var opcode = r.Case.Opcode.ToString().ToUpperInvariant();

        return $"{family} — {exType} in {opcode} [{r.Case.CaseId}: {r.Case.Label}]";
    }

    // ── Severity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Crash-class severity:
    ///   Critical — OutOfMemory, StackOverflow, unhandled exception (process-level risk)
    ///   High     — Overflow, IndexOutOfRange (data corruption risk)
    ///   Medium   — ArgumentOutOfRange (wrong result risk)
    /// </summary>
    public static DefectSeverity Severity(PathologicalResult r)
    {
        if (!r.IsDefect) return DefectSeverity.None;
        return ShortExceptionName(r.ExceptionType) switch
        {
            "OutOfMemoryException"   => DefectSeverity.Critical,
            "StackOverflowException" => DefectSeverity.Critical,
            "NullReferenceException" => DefectSeverity.High,
            "OverflowException"      => DefectSeverity.High,
            "IndexOutOfRangeException" => DefectSeverity.High,
            "ArgumentOutOfRangeException" => DefectSeverity.Medium,
            _                         => DefectSeverity.Medium,
        };
    }

    // ── Report builder ────────────────────────────────────────────────────────

    /// <summary>Renders a structured defect report grouped by family and severity.</summary>
    public static string BuildReport(PathologicalCampaignResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║             PATHOLOGICAL EXECUTION — DEFECT REPORT           ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine($"  Total cases   : {result.Total}");
        sb.AppendLine($"  Passed (EVM)  : {result.Passed}");
        sb.AppendLine($"  Defects (.NET): {result.Defects}");
        sb.AppendLine($"  Clusters      : {result.Clusters.Count}");
        sb.AppendLine();

        if (result.Defects == 0)
        {
            sb.AppendLine("  ✅ No .NET exceptions escaped the EVM engine.");
            sb.AppendLine("     Schlieren fails like an EVM, not like a .NET program.");
            return sb.ToString();
        }

        sb.AppendLine("  ⛔  DEFECT CLUSTERS (ordered by count):");
        sb.AppendLine();

        // Group by severity
        var critical = result.Clusters
            .Where(cl => cl.Cases.Any(r => Severity(r) == DefectSeverity.Critical))
            .ToList();
        var high = result.Clusters
            .Where(cl => !critical.Contains(cl) && cl.Cases.Any(r => Severity(r) == DefectSeverity.High))
            .ToList();
        var medium = result.Clusters
            .Where(cl => !critical.Contains(cl) && !high.Contains(cl))
            .ToList();

        foreach (var (sev, clusters) in new[]
        {
            (DefectSeverity.Critical, critical),
            (DefectSeverity.High,     high),
            (DefectSeverity.Medium,   medium),
        })
        {
            if (clusters.Count == 0) continue;
            sb.AppendLine($"  [{sev.ToString().ToUpperInvariant()}]");
            foreach (var cl in clusters.OrderByDescending(x => x.Count))
            {
                sb.AppendLine($"    {cl.FamilyId,-55} {cl.Count,4} cases");
                sb.AppendLine($"      exception : {cl.ExceptionType?.Split('.').Last()}");
                sb.AppendLine($"      examples  : {string.Join(", ", cl.Cases.Take(3).Select(r => r.Case.CaseId))}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ShortExceptionName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return "UnknownException";
        var dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName[(dot + 1)..] : fullName;
    }
}

public enum DefectSeverity
{
    None,
    Medium,
    High,
    Critical,
}
