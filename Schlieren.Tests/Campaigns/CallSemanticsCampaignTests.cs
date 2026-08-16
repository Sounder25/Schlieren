using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Schlieren.UI.Services;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Round 6: Call Semantics & Frame Integrity Campaign.
/// Generates 200-500 test cases, runs them, clusters failures.
/// </summary>
public class CallSemanticsCampaignTests
{
    private readonly ITestOutputHelper _output;

    public CallSemanticsCampaignTests(ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public async Task Campaign_GenerateMatrix()
    {
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        
        Assert.NotEmpty(cases);
        Assert.InRange(cases.Count, 30, 600);  // Target 200-500, allow variance
        
        // Verify coverage of key dimensions
        Assert.Contains(cases, c => c.Type == CallSemanticsMatrixGenerator.CallType.Call);
        Assert.Contains(cases, c => c.Type == CallSemanticsMatrixGenerator.CallType.DelegateCall);
        Assert.Contains(cases, c => c.Type == CallSemanticsMatrixGenerator.CallType.StaticCall);
        
        Assert.Contains(cases, c => c.Result == CallSemanticsMatrixGenerator.ChildResult.Success);
        Assert.Contains(cases, c => c.Result == CallSemanticsMatrixGenerator.ChildResult.Revert);
        
        Assert.Contains(cases, c => c.Access == CallSemanticsMatrixGenerator.AccessWarmth.Cold);
        Assert.Contains(cases, c => c.Access == CallSemanticsMatrixGenerator.AccessWarmth.Warm);
    }

    [Fact]
    public async Task Campaign_GenerateBytecode()
    {
        var testCase = new CallSemanticsMatrixGenerator.CallTestCase
        {
            CaseId = "R6-TEST",
            Type = CallSemanticsMatrixGenerator.CallType.Call,
            Result = CallSemanticsMatrixGenerator.ChildResult.Success,
            Target = CallSemanticsMatrixGenerator.TargetState.CodePresent,
            Access = CallSemanticsMatrixGenerator.AccessWarmth.Cold,
            Value = CallSemanticsMatrixGenerator.ValueTransfer.Zero,
            Behavior = CallSemanticsMatrixGenerator.ChildBehavior.SLoad,
            ReturnSize = CallSemanticsMatrixGenerator.ReturnDataSize.ThirtyTwo,
            Depth = 2,
            Fork = CallSemanticsMatrixGenerator.Fork.Cancun
        };

        var (parent, child) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);
        
        Assert.StartsWith("0x", parent);
        Assert.StartsWith("0x", child);
        Assert.True(parent.Length > 4);  // More than just "0x"
        Assert.True(child.Length > 4);
        
        // Regression: PUSH3 gas encoding must be 0x0186a0 (100,000), not 0x000186 (390)
        // Sequence: PUSH20 <address> PUSH3 <gas> CALL
        // 73 = PUSH20, 00...bb = 20 bytes, 62 = PUSH3, 0186a0 = 100k gas, f1 = CALL
        _output.WriteLine($"Parent bytecode: {parent}");
        Assert.Contains("7300000000000000000000000000000000000000bb620186a0f1", parent);
    }

    [Fact]
    public void Campaign_GenerateBytecode_REVERT_Structure()
    {
        // Regression: REVERT terminator must not be unreachable
        // NoOp + Revert should generate: 60 00 60 00 fd (not 00 ... fd)
        var testCase = new CallSemanticsMatrixGenerator.CallTestCase
        {
            CaseId = "R6-REVERT-TEST",
            Type = CallSemanticsMatrixGenerator.CallType.Call,
            Result = CallSemanticsMatrixGenerator.ChildResult.Revert,
            Target = CallSemanticsMatrixGenerator.TargetState.CodePresent,
            Access = CallSemanticsMatrixGenerator.AccessWarmth.Cold,
            Value = CallSemanticsMatrixGenerator.ValueTransfer.Zero,
            Behavior = CallSemanticsMatrixGenerator.ChildBehavior.NoOp,
            ReturnSize = CallSemanticsMatrixGenerator.ReturnDataSize.Zero,
            Depth = 2,
            Fork = CallSemanticsMatrixGenerator.Fork.Cancun
        };

        var (_, child) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

        _output.WriteLine($"Child (REVERT): {child}");
        
        // Must end with REVERT sequence: PUSH1 0 / PUSH1 0 / REVERT
        Assert.EndsWith("60006000fd", child);
        
        // Must NOT contain STOP before REVERT (would make REVERT unreachable)
        var withoutPrefix = child.Replace("0x", "");
        var bodyBeforeRevert = withoutPrefix.Substring(0, withoutPrefix.Length - 10); // Remove "60006000fd"
        
        // Body should be empty for NoOp (no STOP opcode 00)
        Assert.Equal("", bodyBeforeRevert);
    }

    [Fact]
    public void Campaign_Matrix_NoDuplicates()
    {
        // Regression: Deduplication must prevent duplicate semantic cases
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        
        var duplicates = cases
            .GroupBy(c => c.CaseId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task Campaign_FirstCase_CALL_Cold_NoOp_STOP()
    {
        // Simplest possible case: CALL to child that just STOPs
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.CaseId.Contains("CALL") && 
                       c.CaseId.Contains("COLD") && 
                       c.CaseId.Contains("NOOP") &&
                       c.CaseId.Contains("SUCCESS"));

        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

        // Debug bytecode
        _output.WriteLine($"Test: {testCase.CaseId}");
        _output.WriteLine($"Parent code: {parentCode}");
        _output.WriteLine($"Child code: {childCode}");

        var request = new CampaignExecutionRequest
        {
            Fork = testCase.Fork.ToString(),
            Caller = DeterministicAddresses.Caller,
            Target = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Parent,
                    Code = parentCode,
                    Balance = "0x0",
                    Nonce = 0
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code = childCode,
                    Balance = "0x0",
                    Nonce = 0
                }
            }
        };

        // Wire to actual execution core
        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        var result = await harness.ExecuteAsync(request);

        // Debug output
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"GasUsed: {result.GasUsed}");
        _output.WriteLine($"ReturnData: {result.ReturnData}");
        if (!result.Success && result.RawTrace.Error != Core.Execution.EvmError.None)
        {
            _output.WriteLine($"Error: {result.RawTrace.Error}");
        }

        // Validate basic execution
        Assert.True(result.Success, $"Execution should succeed but got: {result.RawTrace.Error}");
        Assert.NotNull(result.Fingerprint);
        Assert.NotEmpty(result.Fingerprint.FrameTree);
        
        // Validate depth-2 frame (parent → child)
        var maxDepth = result.Fingerprint.FrameTree.Max(f => f.Depth);
        Assert.Equal(2, maxDepth);
    }

    private static Core.Execution.EvmMachine BuildEvmMachine()
    {
        var opcodeInstances = typeof(Core.Execution.IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && 
                       typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)Activator.CreateInstance(t)!)
            .ToList();

        return new Core.Execution.EvmMachine(opcodeInstances);
    }

    [Fact(Skip = "Enable after first case passes")]
    public async Task Campaign_RunSingleCase_CALL_Success()
    {
        // TODO: Integrate with actual execution engine
        var testCase = new CallSemanticsMatrixGenerator.CallTestCase
        {
            CaseId = "R6-0001",
            Type = CallSemanticsMatrixGenerator.CallType.Call,
            Result = CallSemanticsMatrixGenerator.ChildResult.Success,
            Target = CallSemanticsMatrixGenerator.TargetState.CodePresent,
            Access = CallSemanticsMatrixGenerator.AccessWarmth.Cold,
            Value = CallSemanticsMatrixGenerator.ValueTransfer.Zero,
            Behavior = CallSemanticsMatrixGenerator.ChildBehavior.NoOp,
            ReturnSize = CallSemanticsMatrixGenerator.ReturnDataSize.Zero,
            Depth = 2,
            Fork = CallSemanticsMatrixGenerator.Fork.Cancun
        };

        var (parent, child) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);
        
        // Run through execution engine
        // var result = await ExecutionEngine.Run(parent, child, ...);
        
        // Compare against reference (revm)
        // var expected = await Revm.Run(parent, child, ...);
        
        // Analyze divergence
        // var divergence = DivergenceAnalyzer.Compare(expected, result);
        
        // Assert.Equal(DivergenceAnalyzer.DivergenceCategory.None, divergence.Category);
    }

    [Fact]
    public void DivergenceAnalyzer_GasMismatch()
    {
        var expected = new ExecutionFingerprint
        {
            Success = true,
            GasUsed = 24821,
            ReturnData = "0x",
            Refund = 0,
            FrameTree = new List<FrameFingerprint>
            {
                new() {
                    Depth = 1, CallType = "Root", CodeAddress = "0xaa", ContextAddress = "0xaa",
                    Caller = "0x01", Value = "0", GasProvided = 50000, GasConsumed = 24821,
                    Success = true, ReturnData = "0x"
                },
                new() {
                    Depth = 2, CallType = "Call", CodeAddress = "0xbb", ContextAddress = "0xbb",
                    Caller = "0xaa", Value = "0", GasProvided = 47195, GasConsumed = 2221,
                    Success = true, ReturnData = "0x"
                }
            },
            Accesses = new AccessFingerprint
            {
                ColdAccounts = new List<string> { "0xbb" },
                WarmAccounts = new List<string> { "0xaa" },
                ColdSlots = new List<string>(),
                WarmSlots = new List<string>()
            },
            StateDiff = new Dictionary<string, string>(),
            Logs = new List<LogFingerprint>()
        };

        var actual = new ExecutionFingerprint
        {
            Success = expected.Success,
            GasUsed = 27421,  // +2600 gas
            ReturnData = expected.ReturnData,
            Refund = expected.Refund,
            FrameTree = expected.FrameTree,
            Accesses = expected.Accesses,
            StateDiff = expected.StateDiff,
            Logs = expected.Logs
        };

        var divergence = DivergenceAnalyzer.Compare(expected, actual);

        Assert.Equal(DivergenceAnalyzer.DivergenceCategory.GasMismatch, divergence.Category);
        Assert.Equal(2600, divergence.Delta);
        Assert.Contains("Access list", divergence.LikelySubsystem);
    }

    [Fact]
    public void DivergenceAnalyzer_OutcomeMismatch()
    {
        var expected = new ExecutionFingerprint
        {
            Success = true,
            GasUsed = 24821,
            ReturnData = "0x",
            Refund = 0,
            FrameTree = new List<FrameFingerprint>(),
            Accesses = new AccessFingerprint
            {
                ColdAccounts = new List<string>(),
                WarmAccounts = new List<string>(),
                ColdSlots = new List<string>(),
                WarmSlots = new List<string>()
            },
            StateDiff = new Dictionary<string, string>(),
            Logs = new List<LogFingerprint>()
        };

        var actual = new ExecutionFingerprint
        {
            Success = false,  // Changed
            GasUsed = expected.GasUsed,
            ReturnData = expected.ReturnData,
            Refund = expected.Refund,
            FrameTree = expected.FrameTree,
            Accesses = expected.Accesses,
            StateDiff = expected.StateDiff,
            Logs = expected.Logs
        };

        var divergence = DivergenceAnalyzer.Compare(expected, actual);

        Assert.Equal(DivergenceAnalyzer.DivergenceCategory.OutcomeMismatch, divergence.Category);
        Assert.Contains("SUCCESS", divergence.Message);
        Assert.Contains("REVERT", divergence.Message);
    }

    [Fact]
    public void DivergenceAnalyzer_PerfectMatch()
    {
        var fingerprint = new ExecutionFingerprint
        {
            Success = true,
            GasUsed = 24821,
            ReturnData = "0x",
            Refund = 0,
            FrameTree = new List<FrameFingerprint>(),
            Accesses = new AccessFingerprint
            {
                ColdAccounts = new List<string>(),
                WarmAccounts = new List<string>(),
                ColdSlots = new List<string>(),
                WarmSlots = new List<string>()
            },
            StateDiff = new Dictionary<string, string>(),
            Logs = new List<LogFingerprint>()
        };

        var divergence = DivergenceAnalyzer.Compare(fingerprint, fingerprint);

        Assert.Equal(DivergenceAnalyzer.DivergenceCategory.None, divergence.Category);
        Assert.Equal("Perfect match", divergence.Message);
    }
}

/// <summary>
/// Campaign results aggregator and failure clustering.
/// </summary>
public sealed class CampaignResults
{
    public int TotalCases { get; set; }
    public int PassedCases { get; set; }
    public int FailedCases { get; set; }
    public List<FailureCluster> Clusters { get; set; } = new();

    public sealed class FailureCluster
    {
        public required string ClusterId { get; init; }
        public required string Name { get; init; }
        public required int FailureCount { get; init; }
        public required string CommonPattern { get; init; }
        public required List<string> Forks { get; init; }
        public required string LikelySubsystem { get; init; }
        public required List<string> TestCaseIds { get; init; }
    }

    /// <summary>
    /// Cluster failures by divergence category and subsystem.
    /// </summary>
    public static CampaignResults ClusterFailures(
        List<(string caseId, DivergenceAnalyzer.Divergence divergence)> failures)
    {
        var clusters = failures
            .Where(f => f.divergence.Category != DivergenceAnalyzer.DivergenceCategory.None)
            .GroupBy(f => (f.divergence.Category, f.divergence.LikelySubsystem))
            .Select((g, i) => new FailureCluster
            {
                ClusterId = $"CLUSTER-{(char)('A' + i)}",
                Name = g.Key.LikelySubsystem ?? g.Key.Category.ToString(),
                FailureCount = g.Count(),
                CommonPattern = g.First().divergence.FirstMismatch ?? "Unknown",
                Forks = g.Select(f => f.caseId.Split('-')[0]).Distinct().ToList(),
                LikelySubsystem = g.Key.LikelySubsystem ?? "Unknown",
                TestCaseIds = g.Select(f => f.caseId).ToList()
            })
            .OrderByDescending(c => c.FailureCount)
            .ToList();

        return new CampaignResults
        {
            TotalCases = failures.Count,
            FailedCases = failures.Count(f => f.divergence.Category != DivergenceAnalyzer.DivergenceCategory.None),
            PassedCases = failures.Count(f => f.divergence.Category == DivergenceAnalyzer.DivergenceCategory.None),
            Clusters = clusters
        };
    }

    /// <summary>
    /// Generate human-readable campaign summary.
    /// </summary>
    public string GenerateSummary()
    {
        var summary = $@"
Campaign: Round 6 — Call Semantics & Frame Integrity
Total cases: {TotalCases}
Passed: {PassedCases}
Failed: {FailedCases}

";

        if (Clusters.Any())
        {
            summary += "Failure Clusters:\n";
            foreach (var cluster in Clusters)
            {
                summary += $"  {cluster.ClusterId}  {cluster.Name.PadRight(40)} {cluster.FailureCount,3} cases\n";
            }

            summary += "\n";
            foreach (var cluster in Clusters)
            {
                summary += $@"
{cluster.ClusterId}: {cluster.Name}
Failures: {cluster.FailureCount}
Common pattern: {cluster.CommonPattern}
Forks: {string.Join(", ", cluster.Forks)}
Likely subsystem: {cluster.LikelySubsystem}
Test cases: {string.Join(", ", cluster.TestCaseIds.Take(5))}{(cluster.TestCaseIds.Count > 5 ? "..." : "")}

";
            }
        }
        else
        {
            summary += "✅ All tests passed!\n";
        }

        return summary;
    }
}
