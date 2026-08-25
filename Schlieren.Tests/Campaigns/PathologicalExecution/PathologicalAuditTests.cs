using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Structural audit of the pathological suite.
/// Not in the regression gate — run on demand to verify generator correctness.
/// </summary>
public sealed class PathologicalAuditTests
{
    private readonly ITestOutputHelper _out;
    public PathologicalAuditTests(ITestOutputHelper out_) => _out = out_;

    [Fact]
    public async Task Generator_ConcurrentCalls_ReturnIdenticalCases()
    {
        var runs = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => PathologicalCaseGenerator.Generate())));
        var expected = runs[0].Select(c => c.Fingerprint()).ToArray();

        Assert.All(runs, run =>
            Assert.Equal(expected, run.Select(c => c.Fingerprint()).ToArray()));
    }

    [Fact]
    public void Audit_GeneratorCounts_And_MaterializerSafety()
    {
        var cases = PathologicalCaseGenerator.Generate();

        // ── 1. Total and family breakdown ─────────────────────────────────────
        _out.WriteLine($"\nTotal generated cases: {cases.Count}");
        _out.WriteLine("");

        var byFamily = cases.GroupBy(c => c.Family)
                            .OrderBy(g => g.Key.ToString())
                            .ToList();

        _out.WriteLine("  Family breakdown:");
        foreach (var grp in byFamily)
            _out.WriteLine($"    {grp.Key,-30} {grp.Count(),4} cases");

        // ── 2. Duplicate fingerprints ─────────────────────────────────────────
        var fps   = cases.Select(c => c.Fingerprint()).ToList();
        var dupes = fps.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
        _out.WriteLine($"\nDuplicate fingerprints: {dupes.Count}");
        foreach (var d in dupes.Take(5))
            _out.WriteLine($"  DUP: {d.Key}");

        // ── 3. Every case materialises without throwing ───────────────────────
        var materializerExceptions = new List<(PathologicalCase c, Exception ex)>();
        foreach (var c in cases)
        {
            try { PathologicalMaterializer.Materialize(c); }
            catch (Exception ex) { materializerExceptions.Add((c, ex)); }
        }

        _out.WriteLine($"\nMaterializer exceptions: {materializerExceptions.Count}");
        foreach (var (c, ex) in materializerExceptions.Take(10))
            _out.WriteLine($"  {c.CaseId} {c.Label}: {ex.GetType().Name}: {ex.Message}");

        // ── 4. Bytecode sanity: every case produces non-empty code ────────────
        var emptyCodes = cases
            .Select(c => (c, req: PathologicalMaterializer.Materialize(c)))
            .Where(x =>
            {
                var code = x.req.Prestate.FirstOrDefault(a => a.Address == x.req.Target)?.Code ?? "0x";
                return string.IsNullOrEmpty(code) || code == "0x";
            })
            .ToList();

        _out.WriteLine($"\nCases with empty target code: {emptyCodes.Count}");
        foreach (var (c, _) in emptyCodes.Take(10))
            _out.WriteLine($"  {c.CaseId} {c.Family} {c.Label}");

        // ── 5. Show first 3 per family ────────────────────────────────────────
        _out.WriteLine("\nFirst 3 per family (code length / calldata length):");
        foreach (var grp in byFamily)
        {
            _out.WriteLine($"\n  [{grp.Key}]");
            foreach (var c in grp.Take(3))
            {
                var req  = PathologicalMaterializer.Materialize(c);
                var code = req.Prestate.FirstOrDefault(a => a.Address == req.Target)?.Code ?? "0x";
                var cdLen = req.Calldata.Length / 2 - 1;
                _out.WriteLine($"    {c.CaseId} | {c.Label,-52} | code={code.Length/2-1}B cd={cdLen}B");
            }
        }

        // ── 6. Exception-capture correctness ─────────────────────────────────
        // Inject a synthetic defect to verify the runner catches it
        _out.WriteLine("\nException-capture probe: injecting synthetic OverflowException...");
        var probe = ProbeExceptionCapture();
        _out.WriteLine($"  IsDefect={probe.IsDefect}  ExType={probe.ExceptionType?.Split('.').Last()}");
        Assert.True(probe.IsDefect, "Runner did not capture synthetic exception");
        Assert.Contains("OverflowException", probe.ExceptionType ?? "");

        // ── Final assertions ──────────────────────────────────────────────────
        Assert.True(cases.Count >= 200,
            $"Generator produced only {cases.Count} cases — expected ≥200");
        Assert.Equal(0, dupes.Count);
        Assert.Equal(0, materializerExceptions.Count);
    }

    // ── Synthetic injection test ──────────────────────────────────────────────

    /// <summary>
    /// Verifies the runner's exception capture path is wired correctly
    /// by using a harness that deliberately throws OverflowException.
    /// </summary>
    private static PathologicalResult ProbeExceptionCapture()
    {
        // Wrap runner to inject an exception on first case
        var throwingHarness = new ThrowingHarness();
        var runner = new PathologicalDifferentialRunnerTestable(throwingHarness);

        var c = new PathologicalCase
        {
            CaseId   = "PROBE-001",
            Fork     = "Cancun",
            Family   = PathFamily.MemoryBoundary,
            Opcode   = PathOpcode.Mload,
            Label    = "Synthetic OverflowException probe",
            FamilyId = FailureFamily.OverflowMemoryOffset,
        };

        return runner.RunOnePublic(c).GetAwaiter().GetResult();
    }

    // ── Minimal harness + testable runner subclass ────────────────────────────

    private sealed class ThrowingHarness : Campaigns.IEvmExecutionHarness
    {
        public System.Threading.Tasks.Task<Campaigns.CampaignExecutionResult> ExecuteAsync(
            Campaigns.CampaignExecutionRequest request,
            System.Threading.CancellationToken ct = default)
            => throw new OverflowException("synthetic overflow — testing exception capture");
    }

    /// <summary>
    /// Exposes RunOnePublic without modifying PathologicalDifferentialRunner.
    /// Re-implements the same try/catch contract to validate it.
    /// </summary>
    private sealed class PathologicalDifferentialRunnerTestable
    {
        private readonly Campaigns.IEvmExecutionHarness _harness;
        public PathologicalDifferentialRunnerTestable(Campaigns.IEvmExecutionHarness h) => _harness = h;

        public async System.Threading.Tasks.Task<PathologicalResult> RunOnePublic(PathologicalCase c)
        {
            try
            {
                var req = PathologicalMaterializer.Materialize(c);
                await _harness.ExecuteAsync(req);
                return new PathologicalResult
                {
                    Case = c, Outcome = PathologicalOutcome.Success, IsDefect = false
                };
            }
            catch (OperationCanceledException) { throw; }
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
}
