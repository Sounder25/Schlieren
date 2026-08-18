using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Schlieren.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// Verify storage diffs: 0→X, X→Y, X→0.
/// Uses the generator's proven bytecode to avoid hand-encoding errors.
/// Also checks that pre-state storage is respected by GlobalState.
/// </summary>
public class StorageTransitionTests
{
    private readonly ITestOutputHelper _output;

    public StorageTransitionTests(ITestOutputHelper output)
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

    /// <summary>
    /// Use a MultipleWrites case from the matrix: child writes slots 0/1/2 from 0 → 0xAA/0xBB/0xCC.
    /// Pre-state has no storage. Verify all three appear in StateDiff as "0x0 → 0xAA" etc.
    /// </summary>
    [Fact]
    public async Task Storage_ZeroToNonzero_ShowsDiff()
    {
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        var tc = cases.First(c =>
            c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
            c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
            c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.MultipleWrites);

        _output.WriteLine($"Case: {tc.CaseId}");

        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(tc);
        _output.WriteLine($"Parent: {parentCode}");
        _output.WriteLine($"Child:  {childCode}");

        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        var result = await harness.ExecuteAsync(new CampaignExecutionRequest
        {
            Fork = tc.Fork.ToString(),
            Caller = DeterministicAddresses.Caller,
            Target = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount { Address = DeterministicAddresses.Parent, Code = parentCode, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount { Address = DeterministicAddresses.Child,  Code = childCode,  Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount { Address = DeterministicAddresses.Caller,                    Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            }
        });

        _output.WriteLine($"\nSuccess: {result.Success}  GasUsed: {result.GasUsed}");
        _output.WriteLine("StateDiff:");
        foreach (var kv in result.Fingerprint.StateDiff)
            _output.WriteLine($"  {kv.Key} = {kv.Value}");

        // Every write should show 0x0 → nonzero
        Assert.True(result.Success);
        Assert.NotEmpty(result.Fingerprint.StateDiff);
        foreach (var kv in result.Fingerprint.StateDiff)
            Assert.StartsWith("0x0 →", kv.Value);
    }

    /// <summary>
    /// Pre-state: child slot 0 = 0xAA.  Child bytecode: PUSH1 0 PUSH1 0 SSTORE (clear slot 0).
    /// Diff must show 0xAA → 0x0 (nonzero cleared to zero).
    /// </summary>
    [Fact]
    public async Task Storage_NonzeroToZero_ShowsDiff()
    {
        // Child: PUSH1 0x00, PUSH1 0x00, SSTORE, STOP  →  60 00 60 00 55 00
        var childCode = "0x60006000 5500".Replace(" ", "");

        // Use the generator's parent code for a CALL-child-success case
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        var tc = cases.First(c =>
            c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
            c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
            c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NoOp);
        var (parentCode, _) = CallSemanticsMatrixGenerator.GenerateBytecode(tc);

        _output.WriteLine($"Parent: {parentCode}");
        _output.WriteLine($"Child:  {childCode}  (clears slot 0)");
        _output.WriteLine($"Pre-state child slot 0 = 0xAA");

        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        var result = await harness.ExecuteAsync(new CampaignExecutionRequest
        {
            Fork = tc.Fork.ToString(),
            Caller = DeterministicAddresses.Caller,
            Target = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount { Address = DeterministicAddresses.Parent, Code = parentCode, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code = childCode,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0,
                    Storage = new Dictionary<string, string> { { "0", "0xAA" } }   // pre-state slot 0 = 0xAA
                },
                new CampaignAccount { Address = DeterministicAddresses.Caller, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            }
        });

        _output.WriteLine($"\nSuccess: {result.Success}  GasUsed: {result.GasUsed}");
        _output.WriteLine("StateDiff:");
        foreach (var kv in result.Fingerprint.StateDiff)
            _output.WriteLine($"  {kv.Key} = {kv.Value}");

        Assert.True(result.Success);
        Assert.NotEmpty(result.Fingerprint.StateDiff);
        // Exactly one slot changed; it must show nonzero → 0x0
        Assert.Single(result.Fingerprint.StateDiff);
        Assert.Contains("→ 0x0", result.Fingerprint.StateDiff.Single().Value);
    }

    /// <summary>
    /// Pre-state: child slot 0 = 0xAA.  Child writes slot 0 = 0xBB.
    /// Diff must show 0xAA → 0xBB.
    /// </summary>
    [Fact]
    public async Task Storage_NonzeroToDifferentNonzero_ShowsDiff()
    {
        // Child: PUSH1 0xBB, PUSH1 0x00, SSTORE, STOP  →  60 bb 60 00 55 00
        var childCode = "0x60bb60005500";

        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        var tc = cases.First(c =>
            c.Type == CallSemanticsMatrixGenerator.CallType.Call &&
            c.Result == CallSemanticsMatrixGenerator.ChildResult.Success &&
            c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.NoOp);
        var (parentCode, _) = CallSemanticsMatrixGenerator.GenerateBytecode(tc);

        _output.WriteLine($"Parent: {parentCode}");
        _output.WriteLine($"Child:  {childCode}  (slot 0: 0xAA → 0xBB)");
        _output.WriteLine($"Pre-state child slot 0 = 0xAA");

        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        var result = await harness.ExecuteAsync(new CampaignExecutionRequest
        {
            Fork = tc.Fork.ToString(),
            Caller = DeterministicAddresses.Caller,
            Target = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount { Address = DeterministicAddresses.Parent, Code = parentCode, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code = childCode,
                    Balance = "0xDE0B6B3A7640000",
                    Nonce = 0,
                    Storage = new Dictionary<string, string> { { "0", "0xAA" } }   // pre-state slot 0 = 0xAA
                },
                new CampaignAccount { Address = DeterministicAddresses.Caller, Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            }
        });

        _output.WriteLine($"\nSuccess: {result.Success}  GasUsed: {result.GasUsed}");
        _output.WriteLine("StateDiff:");
        foreach (var kv in result.Fingerprint.StateDiff)
            _output.WriteLine($"  {kv.Key} = {kv.Value}");

        Assert.True(result.Success);
        Assert.NotEmpty(result.Fingerprint.StateDiff);
        Assert.Single(result.Fingerprint.StateDiff);
        Assert.Contains("0xAA →", result.Fingerprint.StateDiff.Single().Value);
        Assert.Contains("→ 0xBB", result.Fingerprint.StateDiff.Single().Value);
    }
}
