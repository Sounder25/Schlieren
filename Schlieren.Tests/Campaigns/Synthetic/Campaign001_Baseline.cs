using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// FROZEN BASELINE — Campaign 001
///
/// 357 cases. REVM-agreement corpus established 2026-08-16.
/// These cases MUST NOT be modified. They are the regression anchor.
///
/// History:
///   56 initial divergences (10 families)
///    3 infrastructure corrections
///      - Comparator: before→after vs post-value normalization
///      - Log propagation: 9 SubCall sites missing Logs.AddRange
///      - Hex parsing: BigInteger signed vs unsigned (balance + storage)
///   Final result: 357/357 agreement with REVM
/// </summary>
public sealed class Campaign001_Baseline
{
    private readonly ITestOutputHelper _out;
    public Campaign001_Baseline(ITestOutputHelper output) => _out = output;

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
    public async Task Baseline001_357Cases_Revm_Agreement()
    {
        var cases  = SyntheticCaseGenerator.GenerateCallStateInteractions();
        var revm   = TryBuildRevm();
        var runner = new SyntheticDifferentialRunner(BuildSchlieren(), revm);
        var result = await runner.RunAsync(cases);

        _out.WriteLine($"Baseline 001: {result.Total} cases");
        _out.WriteLine($"  Structural failures : {result.InvariantFailureCount}");
        _out.WriteLine($"  REVM divergences    : {(revm != null ? result.DifferentialFailureCount.ToString() : "oracle not available")}");

        if (result.Clusters.Count > 0)
        {
            _out.WriteLine("\nREGRESSION — previously-fixed families reappeared:");
            foreach (var cl in result.Clusters)
                _out.WriteLine($"  {cl.FamilyId}  {cl.Count} cases");
        }

        Assert.Equal(0, result.InvariantFailureCount);
        if (revm != null)
            Assert.Equal(0, result.DifferentialFailureCount);
    }
}
