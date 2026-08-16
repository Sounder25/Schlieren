using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Orchestrates a synthetic campaign: generate → execute → compare → cluster → persist.
///
/// Never throws on a divergence. A divergence is data.
///
/// Layer 1 — structural invariants (no oracle needed)
/// Layer 2 — REVM differential (consensus fields: success, gas, returndata, storage, logs)
/// </summary>
public sealed class SyntheticDifferentialRunner
{
    private readonly IEvmExecutionHarness  _schlieren;
    private readonly IEvmExecutionHarness? _oracle;

    public SyntheticDifferentialRunner(
        IEvmExecutionHarness schlieren,
        IEvmExecutionHarness? oracle = null)
    {
        _schlieren = schlieren;
        _oracle    = oracle;
    }

    public async Task<SyntheticCampaignResult> RunAsync(
        IReadOnlyList<SyntheticCase> cases,
        CancellationToken ct = default)
    {
        var invariantFailures  = new List<SyntheticFailureRecord>();
        var differentialFails  = new List<SyntheticFailureRecord>();
        int passes = 0;

        foreach (var syntheticCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var request   = SyntheticCaseMaterializer.Materialize(syntheticCase);
                var schlieren = await _schlieren.ExecuteAsync(request, ct);

                // Layer 1: structural invariants — always run
                var violations = InvariantChecker.Check(syntheticCase, schlieren);
                foreach (var v in violations)
                {
                    invariantFailures.Add(new SyntheticFailureRecord
                    {
                        Case      = syntheticCase,
                        Request   = request,
                        Schlieren = schlieren,
                        Signature = FailureSignatureBuilder.FromInvariant(syntheticCase, schlieren, v),
                        Exception = v,
                    });
                }

                // Layer 2: REVM differential — only when oracle available
                if (_oracle != null)
                {
                    var oracle = await _oracle.ExecuteAsync(request, ct);
                    var diff   = ExecutionComparator.Compare(schlieren, oracle);

                    if (!diff.IsMatch)
                    {
                        differentialFails.Add(new SyntheticFailureRecord
                        {
                            Case      = syntheticCase,
                            Request   = request,
                            Schlieren = schlieren,
                            Oracle    = oracle,
                            ExecutionDiff = diff,
                            Signature = FailureSignatureBuilder.FromDiff(syntheticCase, schlieren, diff),
                        });
                    }
                }

                if (violations.Count == 0 && (_oracle == null || differentialFails.LastOrDefault()?.Case.CaseId != syntheticCase.CaseId))
                    passes++;
            }
            catch (Exception ex)
            {
                invariantFailures.Add(new SyntheticFailureRecord
                {
                    Case      = syntheticCase,
                    Signature = FailureSignature.Infrastructure(syntheticCase, ex),
                    Exception = $"{ex.GetType().Name}: {ex.Message}",
                });
            }
        }

        var allFailures = invariantFailures.Concat(differentialFails).ToList();

        return new SyntheticCampaignResult
        {
            Total                    = cases.Count,
            Passed                   = passes,
            InvariantFailureCount    = invariantFailures.Count,
            DifferentialFailureCount = differentialFails.Count,
            Failures                 = allFailures,
            Clusters                 = FailureClusterer.Cluster(allFailures),
        };
    }
}

// ── Invariant checker (no-oracle mode) ────────────────────────────────────────

public static class InvariantChecker
{
    public static List<string> Check(SyntheticCase c, CampaignExecutionResult r)
    {
        var violations = new List<string>();
        var child = r.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2);
        var root  = r.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 1);

        // Gas: child consumed ≤ provided
        if (child != null && child.GasConsumed > child.GasProvided && child.GasProvided > 0)
            violations.Add($"GasOverrun:child consumed {child.GasConsumed} > provided {child.GasProvided}");

        // Gas: root consumed ≤ provided
        if (root != null && root.GasConsumed > root.GasProvided && root.GasProvided > 0)
            violations.Add($"GasOverrun:root consumed {root.GasConsumed} > provided {root.GasProvided}");

        // REVERT must roll back all storage.
        // RevertMode governs the terminal: if set, the child's writes must not persist.
        // Also applies to self-terminating revert behaviors.
        bool childReverts = c.RevertMode != RevertMode.None
                            || c.ChildBehavior is ChildBehavior.Revert
                                               or ChildBehavior.SStoreRevert
                                               or ChildBehavior.LogRevert;
        if (childReverts && r.Fingerprint.StateDiff.Count > 0)
            violations.Add($"RollbackFailure:REVERT with {r.Fingerprint.StateDiff.Count} uncommitted writes");

        // SSTORE + success: verify the expected state transition actually happened.
        // Derive expected pre/post from the case dimensions, not just "any StateDiff".
        // DELEGATECALL/CALLCODE execute in the CALLER's storage context (parent = 0xaa).
        if (c.ChildBehavior == ChildBehavior.SStore
            && c.CallKind   != CallKind.StaticCall
            && c.TargetKind == TargetKind.ExistingCode
            && c.GasClass   == GasClass.High
            && !childReverts
            && r.Success && child?.Success == true)
        {
            var (writeVal, writeSlot) = SyntheticCaseMaterializer.StorageWritePublic(c.StoragePattern);
            var preVal   = SyntheticCaseMaterializer.PreStorageValue(c.StoragePattern, writeSlot);

            // DELEGATECALL/CALLCODE: write lands on caller's (parent's) storage
            var storageOwner = c.CallKind is CallKind.DelegateCall or CallKind.CallCode
                ? DeterministicAddresses.Parent
                : DeterministicAddresses.Child;
            var slotKey = $"{storageOwner}:0x{writeSlot:X}";

            if (writeVal == preVal)
            {
                // Same-value write: StateDiff must NOT contain this slot
                if (r.Fingerprint.StateDiff.ContainsKey(slotKey))
                    violations.Add($"SpuriousDiff:same-value SSTORE slot {writeSlot} appeared in StateDiff");
            }
            else
            {
                // Different value: StateDiff must show the transition
                if (!r.Fingerprint.StateDiff.ContainsKey(slotKey))
                    violations.Add($"MissingWrite:SSTORE slot {writeSlot} pre=0x{preVal:X} write=0x{writeVal:X} not in StateDiff for {storageOwner}");
            }
        }

        // STATICCALL must never produce state changes
        if (c.CallKind == CallKind.StaticCall && r.Fingerprint.StateDiff.Count > 0)
            violations.Add($"StaticViolation:STATICCALL produced {r.Fingerprint.StateDiff.Count} state changes");

        // OOG child must not commit state
        if (c.GasClass == GasClass.Minimal && child?.Success == false
            && r.Fingerprint.StateDiff.Count > 0)
            violations.Add($"OOGCommit:OOG child committed {r.Fingerprint.StateDiff.Count} state changes");

        return violations;
    }
}

// ── Failure signature builder ─────────────────────────────────────────────────

public static class FailureSignatureBuilder
{
    public static FailureSignature Build(
        SyntheticCase c,
        CampaignExecutionResult schlieren,
        CampaignExecutionResult oracle,
        DivergenceAnalyzer.Divergence diff)
    {
        var category = $"{c.CallKind}-{c.ChildBehavior}";
        return new FailureSignature(
            Category:             category,
            DifferenceKind:       diff.Category.ToString(),
            FirstDivergentOpcode: InferOpcode(diff),
            FrameType:            schlieren.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2)?.CallType,
            SuccessMismatch:      diff.Category == DivergenceAnalyzer.DivergenceCategory.OutcomeMismatch,
            StateMismatch:        diff.Category == DivergenceAnalyzer.DivergenceCategory.StateMismatch,
            GasMismatch:          diff.Category == DivergenceAnalyzer.DivergenceCategory.GasMismatch,
            ReturnDataMismatch:   diff.Category == DivergenceAnalyzer.DivergenceCategory.ReturnDataMismatch,
            LogsMismatch:         diff.Category == DivergenceAnalyzer.DivergenceCategory.LogMismatch);
    }

    public static FailureSignature FromDiff(
        SyntheticCase c,
        CampaignExecutionResult schlieren,
        ExecutionComparator.ExecutionDiff diff)
    {
        var category = $"{c.CallKind}-{c.ChildBehavior}";
        return new FailureSignature(
            Category:             category,
            DifferenceKind:       diff.Category,
            FirstDivergentOpcode: diff.FirstDivergentField,
            FrameType:            schlieren.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2)?.CallType,
            SuccessMismatch:      diff.SuccessMismatch,
            StateMismatch:        diff.StorageMismatch,
            GasMismatch:          diff.GasMismatch,
            ReturnDataMismatch:   diff.ReturnDataMismatch,
            LogsMismatch:         diff.LogsMismatch);
    }

    public static FailureSignature FromInvariant(
        SyntheticCase c,
        CampaignExecutionResult r,
        string violation)
    {
        var kind = violation.Split(':')[0];
        var category = $"{c.CallKind}-{c.ChildBehavior}";
        return new FailureSignature(
            Category:             category,
            DifferenceKind:       kind,
            FirstDivergentOpcode: kind.Contains("SSTORE") || kind.Contains("Write") ? "SSTORE"
                                : kind.Contains("Gas")   ? "CALL"
                                : null,
            FrameType:            r.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2)?.CallType,
            SuccessMismatch:      false,
            StateMismatch:        kind.Contains("Rollback") || kind.Contains("Write") || kind.Contains("Static") || kind.Contains("OOG"),
            GasMismatch:          kind.Contains("Gas"),
            ReturnDataMismatch:   false,
            LogsMismatch:         false);
    }

    private static string? InferOpcode(DivergenceAnalyzer.Divergence diff) =>
        diff.Category switch
        {
            DivergenceAnalyzer.DivergenceCategory.StateMismatch      => "SSTORE",
            DivergenceAnalyzer.DivergenceCategory.ReturnDataMismatch => "RETURN",
            DivergenceAnalyzer.DivergenceCategory.GasMismatch        => "CALL",
            DivergenceAnalyzer.DivergenceCategory.LogMismatch        => "LOG",
            _                                                         => null,
        };
}

// ── Clusterer ─────────────────────────────────────────────────────────────────

public static class FailureClusterer
{
    public static IReadOnlyList<FailureCluster> Cluster(
        IReadOnlyList<SyntheticFailureRecord> failures)
    {
        return failures
            .GroupBy(f => new
            {
                f.Signature.Category,
                f.Signature.DifferenceKind,
                f.Signature.FirstDivergentOpcode,
                f.Signature.FrameType,
                f.Signature.SuccessMismatch,
                f.Signature.StateMismatch,
                f.Signature.GasMismatch,
                f.Signature.ReturnDataMismatch,
                f.Signature.LogsMismatch,
            })
            .Select((g, idx) => new FailureCluster
            {
                FamilyId       = BuildFamilyId(g.First().Signature, idx + 1),
                Count          = g.Count(),
                Signature      = g.First().Signature,
                Cases          = g.ToList(),
                Forks          = g.Select(x => x.Case.Fork).Distinct().OrderBy(x => x).ToArray(),
                Depths         = g.Select(x => x.Case.Depth).Distinct().OrderBy(x => x).ToArray(),
                CallKinds      = g.Select(x => x.Case.CallKind).Distinct().ToArray(),
                ChildBehaviors = g.Select(x => x.Case.ChildBehavior).Distinct().ToArray(),
            })
            .OrderByDescending(x => x.Count)
            .ToArray();
    }

    private static string BuildFamilyId(FailureSignature sig, int idx)
    {
        var parts = new List<string> { "FAM" };
        if (sig.FirstDivergentOpcode != null) parts.Add(sig.FirstDivergentOpcode);
        parts.Add(sig.DifferenceKind.ToUpperInvariant().Replace("MISMATCH", "").TrimEnd('-', '_'));
        parts.Add($"{idx:D3}");
        return string.Join("-", parts);
    }
}

// ── Result persister ──────────────────────────────────────────────────────────

public static class CampaignResultPersister
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Persist(SyntheticCampaignResult result, string? runLabel = null)
    {
        var label   = runLabel ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var root    = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "SyntheticResults", $"run-{label}");
        Directory.CreateDirectory(root);

        // summary.json
        var summary = new
        {
            result.Total, result.Passed, result.Failed,
            result.UniqueFailureFamilies,
            RunLabel = label,
            GeneratedUtc = DateTime.UtcNow,
            Clusters = result.Clusters.Select(cl => new
            {
                cl.FamilyId, cl.Count,
                cl.Signature.DifferenceKind,
                cl.Signature.FirstDivergentOpcode,
                cl.Depths, cl.CallKinds, cl.ChildBehaviors, cl.Forks,
            }),
        };
        File.WriteAllText(
            Path.Combine(root, "summary.json"),
            JsonSerializer.Serialize(summary, _json));

        // One directory per family; one JSON per case
        foreach (var cluster in result.Clusters)
        {
            var dir = Path.Combine(root, cluster.FamilyId);
            Directory.CreateDirectory(dir);

            File.WriteAllText(
                Path.Combine(dir, "family.json"),
                JsonSerializer.Serialize(new
                {
                    cluster.FamilyId, cluster.Count,
                    cluster.Signature, cluster.Depths,
                    cluster.CallKinds, cluster.ChildBehaviors, cluster.Forks,
                }, _json));

            foreach (var record in cluster.Cases)
            {
                var payload = new
                {
                    record.Case,
                    Schlieren = new
                    {
                        Success  = record.Schlieren?.Success,
                        GasUsed  = record.Schlieren?.GasUsed,
                        ReturnData = record.Schlieren?.ReturnData,
                        StateDiff  = record.Schlieren?.Fingerprint.StateDiff,
                        Logs      = record.Schlieren?.Fingerprint.Logs.Select(l => new { l.Address, l.Topics, l.Data }),
                    },
                    Oracle = record.Oracle == null ? null : new
                    {
                        Success  = record.Oracle.Success,
                        GasUsed  = record.Oracle.GasUsed,
                        ReturnData = record.Oracle.ReturnData,
                        StateDiff  = record.Oracle.Fingerprint.StateDiff,
                        Logs      = record.Oracle.Fingerprint.Logs.Select(l => new { l.Address, l.Topics, l.Data }),
                    },
                    Diff = record.ExecutionDiff == null ? null : new
                    {
                        record.ExecutionDiff.Category,
                        record.ExecutionDiff.Layer,
                        record.ExecutionDiff.Detail,
                        record.ExecutionDiff.GasDelta,
                    },
                    InvariantViolation = record.Exception,
                };
                File.WriteAllText(
                    Path.Combine(dir, $"{record.Case.CaseId}.json"),
                    JsonSerializer.Serialize(payload, _json));
            }
        }

        return root;
    }
}
