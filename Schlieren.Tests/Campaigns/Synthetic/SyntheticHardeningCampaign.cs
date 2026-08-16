using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Entry point for the synthetic hardening campaign.
/// Runs the full batch, never fails on divergence, prints the family dashboard.
/// </summary>
public sealed class SyntheticHardeningCampaign
{
    private readonly ITestOutputHelper _out;

    public SyntheticHardeningCampaign(ITestOutputHelper output) => _out = output;

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

    [Fact]
    public async Task Run_CallState_Interactions()
    {
        var cases   = SyntheticCaseGenerator.GenerateCallStateInteractions();
        var runner  = new SyntheticDifferentialRunner(BuildSchlieren());
        var result  = await runner.RunAsync(cases);
        var outPath = CampaignResultPersister.Persist(result);

        PrintDashboard(result, outPath);

        // The test does NOT assert on failure count — divergences are data, not assertions.
        // Assert only that the campaign itself completed without infrastructure failure.
        Assert.True(result.Total > 0, "Expected at least one case to run");
        Assert.True(result.Failed + result.Passed == result.Total,
            "Every case must be accounted for as pass or fail");
    }

    private void PrintDashboard(SyntheticCampaignResult r, string outPath)
    {
        _out.WriteLine("");
        _out.WriteLine("╔══════════════════════════════════════════════════════╗");
        _out.WriteLine("║         SYNTHETIC HARDENING CAMPAIGN RESULTS         ║");
        _out.WriteLine("╚══════════════════════════════════════════════════════╝");
        _out.WriteLine($"  Cases executed      : {r.Total}");
        _out.WriteLine($"  Agreement           : {r.Passed}");
        _out.WriteLine($"  Raw divergences     : {r.Failed}");
        _out.WriteLine($"  Failure families    : {r.UniqueFailureFamilies}");
        _out.WriteLine($"  Results persisted   : {outPath}");
        _out.WriteLine("");

        if (r.Clusters.Count == 0)
        {
            _out.WriteLine("  ✅ No failures found.");
            return;
        }

        foreach (var cl in r.Clusters)
        {
            _out.WriteLine($"  {cl.FamilyId,-35} {cl.Count,4} cases");

            if (cl.Signature.FirstDivergentOpcode != null)
                _out.WriteLine($"    first divergence : {cl.Signature.FirstDivergentOpcode}");

            _out.WriteLine($"    mismatch         : {cl.Signature.DifferenceKind}");

            if (cl.CallKinds.Length > 0)
                _out.WriteLine($"    call kinds       : {string.Join(", ", cl.CallKinds)}");

            if (cl.ChildBehaviors.Length <= 4)
                _out.WriteLine($"    child behaviors  : {string.Join(", ", cl.ChildBehaviors)}");

            if (cl.Depths.Length > 0)
            {
                var depthStr = cl.Depths.Length == 1
                    ? cl.Depths[0].ToString()
                    : $"{cl.Depths.Min()}-{cl.Depths.Max()}";
                _out.WriteLine($"    depths           : {depthStr}");
            }

            if (cl.Forks.Length > 0)
                _out.WriteLine($"    forks            : {string.Join(", ", cl.Forks)}");

            // Print up to 3 example case IDs
            var examples = cl.Cases.Take(3).Select(c => c.Case.CaseId);
            _out.WriteLine($"    examples         : {string.Join(", ", examples)}");
            _out.WriteLine("");
        }
    }
}
