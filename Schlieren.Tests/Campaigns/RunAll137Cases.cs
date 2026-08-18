using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Run all 137 generated cases against Schlieren. Find bugs.
/// </summary>
public class RunAll137Cases
{
    private readonly ITestOutputHelper _output;
    
    public RunAll137Cases(ITestOutputHelper output)
    {
        _output = output;
    }
    
    private static Core.Execution.EvmMachine BuildEvmMachine()
    {
        var opcodeInstances = typeof(Core.Execution.IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && 
                       typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)System.Activator.CreateInstance(t)!)
            .ToList();

        return new Core.Execution.EvmMachine(opcodeInstances);
    }
    
    [Fact]
    public async Task Run_All_137_Generated_Cases()
    {
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        _output.WriteLine($"Running {cases.Count} cases...\n");
        
        var failures = new List<string>();
        var passed = 0;
        
        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);
        
        foreach (var testCase in cases)
        {
            try
            {
                var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);
                
                var accounts = new List<CampaignAccount>
                {
                    new CampaignAccount
                    {
                        Address = DeterministicAddresses.Parent,
                        Code = parentCode,
                        Balance = "0xDE0B6B3A7640000", // 1 ETH
                        Nonce = 0
                    },
                    new CampaignAccount
                    {
                        Address = DeterministicAddresses.Child,
                        Code = childCode,
                        Balance = "0xDE0B6B3A7640000", // 1 ETH
                        Nonce = 0
                    },
                    new CampaignAccount
                    {
                        Address = DeterministicAddresses.Caller,
                        Balance = "0xDE0B6B3A7640000", // 1 ETH
                        Nonce = 0
                    }
                };
                
                // Add grandchild for nested call cases
                if (testCase.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NestedCall || testCase.Depth > 2)
                {
                    // Grandchild is a leaf: just STOP (0x00)
                    var grandchildCode = "0x00";
                    
                    accounts.Add(new CampaignAccount
                    {
                        Address = DeterministicAddresses.Grandchild,
                        Code = grandchildCode,
                        Balance = "0xDE0B6B3A7640000", // 1 ETH
                        Nonce = 0
                    });
                }
                
                var request = new CampaignExecutionRequest
                {
                    Fork = testCase.Fork.ToString(),
                    Caller = DeterministicAddresses.Caller,
                    Target = DeterministicAddresses.Parent,
                    Calldata = "0x",
                    Value = 0,
                    GasLimit = testCase.GasLimit ?? 10_000_000,
                    Prestate = accounts.ToArray()
                };
                
                var result = await harness.ExecuteAsync(request);
                
                // Verify execution semantics match expectations
                bool expectedSuccess = testCase.Result switch
                {
                    CallSemanticsMatrixGenerator.ChildResult.Success => true,
                    CallSemanticsMatrixGenerator.ChildResult.Revert => true, // Parent succeeds even if child reverts (CALL semantics)
                    CallSemanticsMatrixGenerator.ChildResult.OutOfGas when testCase.GasLimit < 10000 => false, // Too little gas for parent itself
                    CallSemanticsMatrixGenerator.ChildResult.OutOfGas => true, // Parent succeeds, child OOGs
                    _ => true
                };
                
                if (!result.Success && expectedSuccess)
                {
                    failures.Add($"{testCase.CaseId}: Expected success but got failure - {result.RawTrace.Error}");
                    _output.WriteLine($"FAIL: {testCase.CaseId}");
                    _output.WriteLine($"  Expected success, got: {result.RawTrace.Error}");
                }
                else if (result.Success)
                {
                    // Verify frame depth for nested calls
                    var maxDepth = result.Fingerprint.FrameTree.Any() ? result.Fingerprint.FrameTree.Max(f => f.Depth) : 1;
                    var expectedDepth = testCase.Depth;
                    
                    if (maxDepth != expectedDepth && testCase.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NestedCall)
                    {
                        failures.Add($"{testCase.CaseId}: Expected depth {expectedDepth}, got {maxDepth}");
                        _output.WriteLine($"FAIL: {testCase.CaseId}");
                        _output.WriteLine($"  Expected depth {expectedDepth}, got {maxDepth}");
                    }
                    else
                    {
                        passed++;
                    }
                }
                else
                {
                    passed++;
                }
            }
            catch (System.Exception ex)
            {
                failures.Add($"{testCase.CaseId}: {ex.GetType().Name} - {ex.Message}");
                _output.WriteLine($"FAIL: {testCase.CaseId}");
                _output.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                _output.WriteLine($"  Type: {testCase.Type}, Behavior: {testCase.Behavior}, Result: {testCase.Result}");
                if (testCase.PrecompileTarget.HasValue)
                {
                    _output.WriteLine($"  Precompile: {testCase.PrecompileTarget.Value}");
                }
                _output.WriteLine("");
            }
        }
        
        _output.WriteLine($"\n=== SUMMARY ===");
        _output.WriteLine($"Passed: {passed}/{cases.Count}");
        _output.WriteLine($"Failed: {failures.Count}/{cases.Count}");
        
        if (failures.Any())
        {
            _output.WriteLine($"\n=== FAILURES ===");
            foreach (var failure in failures.Take(20))
            {
                _output.WriteLine(failure);
            }
            if (failures.Count > 20)
            {
                _output.WriteLine($"... and {failures.Count - 20} more");
            }
        }
        
        Assert.True(failures.Count == 0, $"{failures.Count} cases failed. See output for details.");
    }
}
