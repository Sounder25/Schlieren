using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Runs pathological cases through Schlieren and classifies results.
///
/// The single invariant:
///   Allowed outcomes: SUCCESS / REVERT / OOG / INVALID / STACK_UNDERFLOW /
///                     STACK_OVERFLOW / BAD_JUMP_DEST / RETURNDATA_OOB /
///                     STATIC_VIOLATION / DEPTH_LIMIT
///
///   Forbidden outcomes: any .NET exception escaping the engine
///     (OverflowException, ArgumentOutOfRangeException,
///      IndexOutOfRangeException, OutOfMemoryException,
///      NullReferenceException, unhandled engine exception)
///
/// No oracle required — the invariant does not depend on gas or state values.
/// A pass means Schlieren produced any EVM-legal outcome.
/// A defect means Schlieren threw .NET at the caller.
/// </summary>
public sealed class PathologicalDifferentialRunner
{
    private readonly Campaigns.SchlierenExecutionHarness _schlieren;

    public PathologicalDifferentialRunner(Campaigns.SchlierenExecutionHarness schlieren)
    {
        _schlieren = schlieren;
    }

    public async Task<PathologicalCampaignResult> RunAsync(
        IReadOnlyList<PathologicalCase> cases,
        CancellationToken ct = default)
    {
        var results = new List<PathologicalResult>(cases.Count);

        foreach (var c in cases)
        {
            ct.ThrowIfCancellationRequested();
            var r = await RunOneAsync(c, ct);
            results.Add(r);
        }

        var defects  = results.Where(r => r.IsDefect).ToList();
        var passes   = results.Count - defects.Count;
        var clusters = PathologicalClusterer.Cluster(defects);

        return new PathologicalCampaignResult
        {
            Total      = results.Count,
            Passed     = passes,
            Defects    = defects.Count,
            AllResults = results,
            Clusters   = clusters,
        };
    }

    private async Task<PathologicalResult> RunOneAsync(
        PathologicalCase c,
        CancellationToken ct)
    {
        try
        {
            var request = PathologicalMaterializer.Materialize(c);
            var result  = await _schlieren.ExecuteAsync(request, ct);

            // If we got here the engine returned normally (success or EVM-defined halt)
            return new PathologicalResult
            {
                Case        = c,
                Outcome     = result.Success ? PathologicalOutcome.Success : PathologicalOutcome.Revert,
                IsDefect    = false,
                EngineSuccess = result.Success,
                GasUsed     = result.GasUsed,
                ReturnData  = result.ReturnData,
            };
        }
        catch (OperationCanceledException)
        {
            throw; // propagate cancellation, never swallow
        }
        catch (Exception ex)
        {
            return new PathologicalResult
            {
                Case             = c,
                Outcome          = PathologicalOutcome.DotNetException,
                IsDefect         = true,
                ExceptionType    = ex.GetType().FullName,
                ExceptionMessage = ex.Message,
                StackTrace       = ex.StackTrace,
            };
        }
    }
}

// ── Clusterer ─────────────────────────────────────────────────────────────────

public static class PathologicalClusterer
{
    public static IReadOnlyList<PathologicalCluster> Cluster(
        IEnumerable<PathologicalResult> defects)
    {
        return defects
            .GroupBy(r => new
            {
                r.Case.FamilyId,
                ExceptionType = r.ExceptionType ?? "Unknown",
            })
            .Select(g => new PathologicalCluster
            {
                FamilyId      = $"{g.Key.FamilyId}/{g.Key.ExceptionType?.Split('.').Last() ?? "?"}",
                Count         = g.Count(),
                ExceptionType = g.Key.ExceptionType ?? "Unknown",
                Cases         = g.ToList(),
            })
            .OrderByDescending(x => x.Count)
            .ToList();
    }
}

// ── Result persister ──────────────────────────────────────────────────────────

public static class PathologicalResultPersister
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented  = true,
        Converters     = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Persist(PathologicalCampaignResult result, string? label = null)
    {
        var lbl  = label ?? System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var root = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "PathologicalResults", $"run-{lbl}");
        System.IO.Directory.CreateDirectory(root);

        // summary.json
        var summary = new
        {
            result.Total, result.Passed, result.Defects,
            ClusterCount = result.Clusters.Count,
            RunLabel     = lbl,
            GeneratedUtc = System.DateTime.UtcNow,
            Clusters     = result.Clusters.Select(cl => new
            {
                cl.FamilyId, cl.Count, cl.ExceptionType,
                Examples = cl.Cases.Take(3).Select(r => new
                {
                    r.Case.CaseId,
                    r.Case.Label,
                    r.ExceptionType,
                    r.ExceptionMessage,
                }),
            }),
        };
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(root, "summary.json"),
            JsonSerializer.Serialize(summary, _json));

        // Defects only — one file per cluster
        foreach (var cluster in result.Clusters)
        {
            var dir = System.IO.Path.Combine(root, SanitizePath(cluster.FamilyId));
            System.IO.Directory.CreateDirectory(dir);

            foreach (var r in cluster.Cases)
            {
                var payload = new
                {
                    r.Case.CaseId,
                    r.Case.Label,
                    r.Case.Fork,
                    r.Case.Family,
                    r.Case.Opcode,
                    r.Case.FamilyId,
                    r.ExceptionType,
                    r.ExceptionMessage,
                    StackTrace = r.StackTrace?.Split('\n').Take(10),
                };
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, $"{r.Case.CaseId}.json"),
                    JsonSerializer.Serialize(payload, _json));
            }
        }

        return root;
    }

    private static string SanitizePath(string s) =>
        string.Join("_", s.Split(System.IO.Path.GetInvalidFileNameChars()));
}
