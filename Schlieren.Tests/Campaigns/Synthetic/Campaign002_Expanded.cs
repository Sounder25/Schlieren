using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Campaign 002 — expanded semantic surface (~2,000 cases).
///
/// Adds: CREATE/CREATE2 lifecycle, nested revert/rollback, warm/cold transitions,
/// value/balance boundaries, all returndata sizes, LOG0–LOG4, depth stress,
/// and interaction pairs (STATICCALL→SSTORE, DELEGATECALL→SSTORE, etc.)
/// </summary>
public sealed class Campaign002_Expanded
{
    private readonly ITestOutputHelper _out;
    public Campaign002_Expanded(ITestOutputHelper output) => _out = output;

    private static SchlierenExecutionHarness BuildSchlieren()
    {
        var machine = new Core.Execution.EvmMachine(
            typeof(Core.Execution.IOpcode).Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } &&
                            typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
                .Select(t => (Core.Execution.IOpcode)Activator.CreateInstance(t)!)
                .ToList());
        return new SchlierenExecutionHarness(new Core.Execution.StateTransition(machine));
    }

    private static RevmExecutionHarness? TryBuildRevm()
    {
        var path = RevmExecutionHarness.DefaultBinaryPath();
        return File.Exists(path) ? new RevmExecutionHarness(path) : null;
    }

    [Fact]
    public async Task Campaign002_Run()
    {
        var cases  = Campaign002Generator.Generate();
        var revm   = TryBuildRevm();
        var runner = new SyntheticDifferentialRunner(BuildSchlieren(), revm);
        var result = await runner.RunAsync(cases);
        var outPath = CampaignResultPersister.Persist(result, $"c002-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        PrintDashboard(result, outPath, revm != null, cases.Count);

        Assert.True(result.Total > 0);
        Assert.Equal(result.Total, result.Passed + result.Failed);
    }

    private void PrintDashboard(SyntheticCampaignResult r, string outPath, bool hasOracle, int total)
    {
        _out.WriteLine("");
        _out.WriteLine("╔══════════════════════════════════════════════════════╗");
        _out.WriteLine("║         SYNTHETIC HARDENING — CAMPAIGN 002           ║");
        _out.WriteLine("╚══════════════════════════════════════════════════════╝");
        _out.WriteLine($"  Cases                          : {r.Total}");
        _out.WriteLine($"  Passed                         : {r.Passed}");
        _out.WriteLine($"  Structural invariant failures  : {r.InvariantFailureCount}");
        _out.WriteLine($"  REVM execution divergences     : {(hasOracle ? r.DifferentialFailureCount.ToString() : "oracle not available")}");
        _out.WriteLine($"  Unique failure families        : {r.UniqueFailureFamilies}");
        _out.WriteLine($"  Results                        : {outPath}");
        _out.WriteLine("");

        if (r.Clusters.Count == 0)
        {
            _out.WriteLine(hasOracle
                ? $"  ✅ {r.Total}/{r.Total} agreement with REVM."
                : $"  ✅ {r.Total}/{r.Total} structural invariants passed.");
            return;
        }

        _out.WriteLine("  Failure families (ranked by size):");
        foreach (var cl in r.Clusters.Take(15))
        {
            _out.WriteLine($"    {cl.FamilyId,-38} {cl.Count,5} cases");
            _out.WriteLine($"      mismatch  : {cl.Signature.DifferenceKind}");
            if (cl.Signature.FirstDivergentOpcode != null)
                _out.WriteLine($"      opcode    : {cl.Signature.FirstDivergentOpcode}");
            if (cl.CallKinds.Length <= 4)
                _out.WriteLine($"      call kinds: {string.Join(", ", cl.CallKinds)}");
            if (cl.Depths.Length > 0)
                _out.WriteLine($"      depths    : {cl.Depths.Min()}-{cl.Depths.Max()}");
            _out.WriteLine($"      examples  : {string.Join(", ", cl.Cases.Take(3).Select(c => c.Case.CaseId))}");
            _out.WriteLine("");
        }
    }
}
