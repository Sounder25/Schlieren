using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Campaign 003 — fork-local semantic deltas + new-feature interaction matrix.
///
/// 003A: Berlin/London/Shanghai/Cancun/Prague × core behaviors
/// 003B: DEFERRED — activation-boundary tests require blockchain harness
/// 003C: TLOAD/TSTORE, MCOPY, PUSH0, SELFDESTRUCT EIP-6780, CREATE/EIP-6780, LOG × forks
/// </summary>
public sealed class Campaign003_ForkSemanticsAndFeatures
{
    private readonly ITestOutputHelper _out;
    public Campaign003_ForkSemanticsAndFeatures(ITestOutputHelper output) => _out = output;

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
    public async Task Campaign003A_ForkLocalDeltas()
    {
        var cases  = Campaign003Generator.Generate003A();
        var result = await RunAndPrint(cases, "003A — Fork-local semantic deltas");
        Assert.Equal(0, result.InvariantFailureCount);
    }

    [Fact]
    public void Campaign003B_ActivationBoundary_Deferred()
    {
        // 003B requires blockchain-level block number control.
        // State-test harness executes with a fixed IForkRules — no auto-transition by block.
        // Deferred until a blockchain test harness is available.
        _out.WriteLine("003B DEFERRED: activation-boundary tests require blockchain harness.");
        _out.WriteLine("  The state-test harness uses a fixed IForkRules with no block-number transition.");
        _out.WriteLine("  These tests will be added when BlockchainTestHarness is implemented.");
    }

    [Fact]
    public async Task Campaign003C_NewFeatureInteractions()
    {
        var cases  = Campaign003Generator.Generate003C();
        var result = await RunAndPrint(cases, "003C — New-feature interactions");
        Assert.Equal(0, result.InvariantFailureCount);
    }

    private async Task<SyntheticCampaignResult> RunAndPrint(
        System.Collections.Generic.List<SyntheticCase> cases, string label)
    {
        var revm   = TryBuildRevm();
        var runner = new SyntheticDifferentialRunner(BuildSchlieren(), revm);
        var result = await runner.RunAsync(cases);
        var outPath = CampaignResultPersister.Persist(result,
            $"c003-{label.Substring(0,4).ToLower().Replace(" ","")}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        _out.WriteLine($"\n╔══════════════════════════════════════════════════════╗");
        _out.WriteLine($"║  {label,-52}║");
        _out.WriteLine($"╚══════════════════════════════════════════════════════╝");
        _out.WriteLine($"  Cases        : {result.Total}");
        _out.WriteLine($"  Passed       : {result.Passed}");
        _out.WriteLine($"  Invariants   : {result.InvariantFailureCount}");
        _out.WriteLine($"  REVM delta   : {(revm != null ? result.DifferentialFailureCount.ToString() : "no oracle")}");
        _out.WriteLine($"  Families     : {result.UniqueFailureFamilies}");
        _out.WriteLine($"  Results      : {outPath}");

        if (result.Clusters.Count > 0)
        {
            _out.WriteLine("\n  Failure families:");
            foreach (var cl in result.Clusters.Take(10))
            {
                _out.WriteLine($"    {cl.FamilyId,-36} {cl.Count,5} cases  [{string.Join(", ", cl.Forks.Take(3))}]");
                _out.WriteLine($"      {cl.Signature.DifferenceKind}  ops:{cl.Signature.FirstDivergentOpcode ?? "—"}  calls:{string.Join(",", cl.CallKinds)}");
                _out.WriteLine($"      e.g. {string.Join(", ", cl.Cases.Take(2).Select(c => c.Case.CaseId))}");
            }
        }
        else
        {
            _out.WriteLine(revm != null
                ? $"\n  ✅ {result.Total}/{result.Total} REVM agreement."
                : $"\n  ✅ {result.Total}/{result.Total} structural invariants passed.");
        }

        return result;
    }
}
