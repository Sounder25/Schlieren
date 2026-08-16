using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Show me what Schlieren is actually producing so I can see if it's wrong.
/// </summary>
public class ShowMeOutputs
{
    private readonly ITestOutputHelper _output;
    
    public ShowMeOutputs(ITestOutputHelper output)
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
    public async Task Show_Storage_Behavior()
    {
        // Find a MultipleWrites case
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        var testCase = cases.First(c => c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.MultipleWrites &&
                                        c.Type == CallSemanticsMatrixGenerator.CallType.Call);
        
        _output.WriteLine($"=== {testCase.CaseId} ===\n");
        
        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);
        _output.WriteLine($"Child code: {childCode}");
        _output.WriteLine($"Expected: 3 SSTOREs (slot 0=0xAA, slot 1=0xBB, slot 2=0xCC)\n");
        
        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);
        
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
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code = childCode,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Caller,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0
                }
            }
        };
        
        var result = await harness.ExecuteAsync(request);
        
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"Gas Used: {result.GasUsed}");
        _output.WriteLine($"\nState changes:");
        foreach (var kv in result.Fingerprint.StateDiff)
        {
            _output.WriteLine($"  {kv.Key} = {kv.Value}");
        }
        
        _output.WriteLine($"\nFrame tree:");
        foreach (var frame in result.Fingerprint.FrameTree)
        {
            _output.WriteLine($"  Depth {frame.Depth}: {frame.CallType} to {frame.CodeAddress}");
            _output.WriteLine($"    Success: {frame.Success}, Gas: {frame.GasProvided} provided / {frame.GasConsumed} consumed");
        }
    }
    
    [Fact]
    public async Task Show_Revert_Behavior()
    {
        // Find a revert case
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        var testCase = cases.First(c => c.Result == CallSemanticsMatrixGenerator.ChildResult.Revert &&
                                        c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
                                        c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NoOp);
        
        _output.WriteLine($"=== {testCase.CaseId} ===\n");
        
        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);
        _output.WriteLine($"Child code: {childCode}");
        _output.WriteLine($"Expected: Child reverts, parent succeeds\n");
        
        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);
        
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
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code = childCode,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Caller,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0
                }
            }
        };
        
        var result = await harness.ExecuteAsync(request);
        
        _output.WriteLine($"Parent Success: {result.Success}");
        _output.WriteLine($"Gas Used: {result.GasUsed}");
        _output.WriteLine($"\nFrame tree:");
        foreach (var frame in result.Fingerprint.FrameTree)
        {
            _output.WriteLine($"  Depth {frame.Depth}: {frame.CallType}");
            _output.WriteLine($"    Success: {frame.Success}");
            _output.WriteLine($"    ReturnData: {frame.ReturnData}");
        }
    }
}
