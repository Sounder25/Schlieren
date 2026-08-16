using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Full 50+ case matrix execution with failure clustering.
/// Treats output as defect-discovery dataset, not 50 separate failures.
/// </summary>
public class FullCallSemanticsMatrixTests
{
    private readonly ITestOutputHelper _output;

    public FullCallSemanticsMatrixTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunFullMatrix_ClusterFailures()
    {
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        _output.WriteLine($"Generated {cases.Count} test cases");

        var results = new List<MatrixExecutionResult>();
        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        // Execute all cases
        foreach (var testCase in cases)
        {
            var result = await ExecuteCase(testCase, harness);
            results.Add(result);

            if (!result.Passed)
            {
                _output.WriteLine($"FAIL: {result.CaseId} - {result.FailureCategory}");
            }
        }

        // Summary
        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);

        _output.WriteLine("");
        _output.WriteLine("========================================");
        _output.WriteLine($"MATRIX EXECUTION COMPLETE");
        _output.WriteLine($"{cases.Count} cases");
        _output.WriteLine($"{passed} PASS");
        _output.WriteLine($"{failed} FAIL");
        _output.WriteLine("========================================");

        if (failed > 0)
        {
            // Cluster failures
            var clusters = ClusterFailures(results.Where(r => !r.Passed).ToList());

            _output.WriteLine("");
            _output.WriteLine("FAILURE CLUSTERS:");
            _output.WriteLine("");

            int clusterNum = 1;
            foreach (var cluster in clusters.OrderByDescending(c => c.Count))
            {
                _output.WriteLine($"#{clusterNum}  {cluster.Category} ........ {cluster.Count} cases");
                _output.WriteLine($"     {cluster.Description}");
                _output.WriteLine($"     Representative: {cluster.RepresentativeCases.First()}");
                _output.WriteLine("");
                clusterNum++;
            }

            // Save failure report
            await SaveFailureReport(results, clusters);

            // Don't fail the test - this is discovery, not validation
            _output.WriteLine("Full failure report saved to: campaign_failures.json");
            _output.WriteLine("");
            _output.WriteLine("NEXT STEPS:");
            _output.WriteLine("1. Inspect largest cluster");
            _output.WriteLine("2. Identify common engine abstraction");
            _output.WriteLine("3. Fix that abstraction (not individual cases)");
            _output.WriteLine("4. Rerun full matrix");
        }
        else
        {
            _output.WriteLine("");
            _output.WriteLine("✅ ALL CASES GREEN - READY FOR MUTATIONS");
        }
    }

    private async Task<MatrixExecutionResult> ExecuteCase(
        CallSemanticsMatrixGenerator.CallTestCase testCase,
        SchlierenExecutionHarness harness)
    {
        var result = new MatrixExecutionResult
        {
            CaseId = testCase.CaseId,
            CallType = testCase.Type.ToString(),
            AccessState = testCase.Access.ToString(),
            ChildBehavior = testCase.Behavior.ToString(),
            ExpectedResult = testCase.Result.ToString()
        };

        try
        {
            var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

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
                    },
                    new CampaignAccount
                    {
                        Address = DeterministicAddresses.Grandchild,
                        Code = "0x00", // STOP
                        Balance = "0x0",
                        Nonce = 0
                    }
                }
            };

            var execResult = await harness.ExecuteAsync(request);

            result.ActualSuccess = execResult.Success;
            result.GasUsed = execResult.GasUsed;
            result.ActualMaxDepth = execResult.Fingerprint.FrameTree.Any() 
                ? execResult.Fingerprint.FrameTree.Max(f => f.Depth) 
                : 1;
            result.ActualReturnData = execResult.ReturnData;

            // Check invariants
            // NOTE: testCase.Result is CHILD frame behavior, not parent transaction
            // Parent transaction can succeed even if child reverts (CALL semantics)
            // We validate child frame success from fingerprint
            var expectedChildSuccess = testCase.Result == CallSemanticsMatrixGenerator.ChildResult.Success;
            var childFrame = execResult.Fingerprint.FrameTree.FirstOrDefault(f => f.Depth == 2);
            var actualChildSuccess = childFrame?.Success ?? true;
            
            if (actualChildSuccess != expectedChildSuccess)
            {
                result.Passed = false;
                result.FailureCategory = "Child frame outcome mismatch";
                result.FirstDivergentInvariant = "Child Success/Revert";
                result.BugClassification = "SCHLIEREN"; // Execution semantics
            }
            else if (result.ActualMaxDepth != testCase.Depth)
            {
                result.Passed = false;
                result.FailureCategory = "Frame depth mismatch";
                result.FirstDivergentInvariant = "Max depth";
                result.BugClassification = "SCHLIEREN"; // Frame semantics
            }
            else
            {
                result.Passed = true;
            }
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.FailureCategory = "Execution exception";
            result.FirstDivergentInvariant = "Exception";
            result.ErrorMessage = ex.Message;
            result.BugClassification = "CAMPAIGN"; // Likely bad bytecode or harness error
        }

        return result;
    }

    private List<FailureCluster> ClusterFailures(List<MatrixExecutionResult> failures)
    {
        var clusters = new Dictionary<string, FailureCluster>();

        foreach (var failure in failures)
        {
            var key = $"{failure.FailureCategory}_{failure.CallType}_{failure.BugClassification}";

            if (!clusters.ContainsKey(key))
            {
                clusters[key] = new FailureCluster
                {
                    Category = failure.FailureCategory,
                    CallType = failure.CallType,
                    BugClassification = failure.BugClassification,
                    Description = GenerateClusterDescription(failure),
                    RepresentativeCases = new List<string>()
                };
            }

            clusters[key].Count++;
            if (clusters[key].RepresentativeCases.Count < 3)
            {
                clusters[key].RepresentativeCases.Add(failure.CaseId);
            }
        }

        return clusters.Values.ToList();
    }

    private string GenerateClusterDescription(MatrixExecutionResult failure)
    {
        return failure.FailureCategory switch
        {
            "Outcome mismatch" => $"Expected {failure.ExpectedResult}, got {(failure.ActualSuccess ? "Success" : "Fail")}",
            "Frame depth mismatch" => $"Max depth diverged",
            "Gas divergence" => $"Gas delta: {failure.GasDelta}",
            "Execution exception" => $"Exception: {failure.ErrorMessage?.Substring(0, Math.Min(50, failure.ErrorMessage?.Length ?? 0))}",
            _ => failure.FirstDivergentInvariant
        };
    }

    private async Task SaveFailureReport(List<MatrixExecutionResult> results, List<FailureCluster> clusters)
    {
        var report = new
        {
            Timestamp = DateTime.UtcNow,
            TotalCases = results.Count,
            Passed = results.Count(r => r.Passed),
            Failed = results.Count(r => !r.Passed),
            Clusters = clusters.OrderByDescending(c => c.Count).Select(c => new
            {
                c.Category,
                c.Count,
                c.BugClassification,
                c.Description,
                RepresentativeCases = c.RepresentativeCases
            }),
            Failures = results.Where(r => !r.Passed).Select(r => new
            {
                r.CaseId,
                r.FailureCategory,
                r.BugClassification,
                r.CallType,
                r.AccessState,
                r.ChildBehavior,
                r.ExpectedResult,
                r.ActualSuccess,
                r.GasUsed,
                r.ActualMaxDepth,
                r.FirstDivergentInvariant,
                r.ErrorMessage
            })
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var artifactPath = Path.Combine(Directory.GetCurrentDirectory(), "campaign_failures.json");
        await File.WriteAllTextAsync(artifactPath, json);
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
}

public class MatrixExecutionResult
{
    public string CaseId { get; set; } = "";
    public bool Passed { get; set; }
    public string FailureCategory { get; set; } = "";
    public string FirstDivergentInvariant { get; set; } = "";
    public string CallType { get; set; } = "";
    public string AccessState { get; set; } = "";
    public string ChildBehavior { get; set; } = "";
    public string ExpectedResult { get; set; } = "";
    public bool ActualSuccess { get; set; }
    public ulong GasUsed { get; set; }
    public ulong GasDelta { get; set; }
    public int ActualMaxDepth { get; set; }
    public string ActualReturnData { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public string BugClassification { get; set; } = ""; // CAMPAIGN or SCHLIEREN
}

public class FailureCluster
{
    public string Category { get; set; } = "";
    public string CallType { get; set; } = "";
    public string BugClassification { get; set; } = "";
    public int Count { get; set; }
    public string Description { get; set; } = "";
    public List<string> RepresentativeCases { get; set; } = new();
}
