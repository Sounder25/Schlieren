using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Five basic integration cases to validate adapter before unleashing full matrix.
/// </summary>
public class BasicCallSemanticTests
{
    private readonly ITestOutputHelper _output;

    public BasicCallSemanticTests(ITestOutputHelper _output)
    {
        this._output = _output;
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
    public async Task Basic01_CALL_Success_NoOp()
    {
        // CALL to child that just STOPs
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
                       c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
                       c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NoOp);

        var result = await ExecuteTestCase(testCase);

        Assert.True(result.Success, $"CALL+STOP should succeed: {result.RawTrace.Error}");
        Assert.Equal(2, result.Fingerprint.FrameTree.Max(f => f.Depth));
    }

    [Fact]
    public async Task Basic02_CALL_Success_WithReturn()
    {
        // CALL to child that does SLOAD and RETURNs 32 bytes
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
                       c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
                       c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.SLoad &&
                       c.ReturnSize == CallSemanticsMatrixGenerator.ReturnDataSize.ThirtyTwo);

        var result = await ExecuteTestCase(testCase);

        Assert.True(result.Success, $"CALL+SLOAD+RETURN should succeed: {result.RawTrace.Error}");
        Assert.Equal(2, result.Fingerprint.FrameTree.Max(f => f.Depth));
    }

    [Fact]
    public async Task Basic03_CALL_Revert()
    {
        // CALL to child that REVERTs
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
                       c.Result == CallSemanticsMatrixGenerator.ChildResult.Revert &&
                       c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NoOp);

        var result = await ExecuteTestCase(testCase);

        // Parent succeeds even though child reverts (CALL semantics)
        Assert.True(result.Success, "Parent should succeed even when child reverts");
        Assert.Equal(2, result.Fingerprint.FrameTree.Max(f => f.Depth));
    }

    [Fact]
    public async Task Basic04_DELEGATECALL_Success()
    {
        // DELEGATECALL to child
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.Type == CallSemanticsMatrixGenerator.CallType.DelegateCall &&
                       c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
                       c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.SLoad);

        var result = await ExecuteTestCase(testCase);

        Assert.True(result.Success, $"DELEGATECALL should succeed: {result.RawTrace.Error}");
        Assert.Equal(2, result.Fingerprint.FrameTree.Max(f => f.Depth));
    }

    [Fact]
    public async Task Basic05_STATICCALL_Success()
    {
        // STATICCALL to child (read-only)
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.Type == CallSemanticsMatrixGenerator.CallType.StaticCall &&
                       c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
                       c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.SLoad);

        var result = await ExecuteTestCase(testCase);

        Assert.True(result.Success, $"STATICCALL should succeed: {result.RawTrace.Error}");
        Assert.Equal(2, result.Fingerprint.FrameTree.Max(f => f.Depth));
    }

    private async Task<CampaignExecutionResult> ExecuteTestCase(CallSemanticsMatrixGenerator.CallTestCase testCase)
    {
        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

        _output.WriteLine($"Test: {testCase.CaseId}");
        _output.WriteLine($"Parent: {parentCode}");
        _output.WriteLine($"Child: {childCode}");

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

        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        var result = await harness.ExecuteAsync(request);

        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"GasUsed: {result.GasUsed}");
        _output.WriteLine($"Frames: {result.Fingerprint.FrameTree.Count}");

        return result;
    }
}
