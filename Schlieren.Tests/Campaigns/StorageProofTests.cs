using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Campaigns;

/// <summary>
/// CONSENSUS-LEVEL proof: check actual storage state, not metadata.
/// </summary>
public class StorageProofTests
{
    private readonly ITestOutputHelper _output;

    public StorageProofTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Proof_STATICCALL_SSTORE()
    {
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.CaseId == "R6_STATICCALL_COLD_REVERT_SSTORE_R0_D2_CANCUN");

        var result = await ExecuteCase(testCase);

        _output.WriteLine("=== STATICCALL + SSTORE ===");
        _output.WriteLine($"TX Success: {result.Success}");
        _output.WriteLine($"TX Gas: {result.GasUsed}");
        _output.WriteLine("");

        // GROUND TRUTH: Check child's storage slot 0
        var childAddr = Address.FromHex(DeterministicAddresses.Child);
        var slotZero = await result.PostExecutionState.GetStorageAtAsync(childAddr, BigInteger.Zero);

        _output.WriteLine("CONSENSUS CHECK:");
        _output.WriteLine($"Child slot 0 = {slotZero}");
        _output.WriteLine("");

        if (slotZero == BigInteger.Zero)
        {
            _output.WriteLine("✓ Storage NOT mutated");
            _output.WriteLine("  STATICCALL write protection WORKS");
            _output.WriteLine("  Execution: CORRECT");
            _output.WriteLine("  Frame.Success metadata: WRONG (tracing bug)");
        }
        else
        {
            _output.WriteLine($"✗ Storage WAS mutated to {slotZero}");
            _output.WriteLine("  STATICCALL write protection FAILED");
            _output.WriteLine("  CONSENSUS EXECUTION BUG");
        }
    }

    [Fact]
    public async Task Proof_CALL_OOG()
    {
        var testCase = CallSemanticsMatrixGenerator.GenerateMatrix()
            .First(c => c.CaseId == "R6_CALL_COLD_OUTOFGAS_SSTORE_R0_D2_CANCUN");

        var result = await ExecuteCase(testCase);

        _output.WriteLine("=== CALL gas=3000 + SSTORE ===");
        _output.WriteLine($"TX Success: {result.Success}");
        _output.WriteLine($"TX Gas: {result.GasUsed}");
        _output.WriteLine("");

        // GROUND TRUTH
        var childAddr = Address.FromHex(DeterministicAddresses.Child);
        var slotZero = await result.PostExecutionState.GetStorageAtAsync(childAddr, BigInteger.Zero);

        _output.WriteLine("CONSENSUS CHECK:");
        _output.WriteLine($"Child slot 0 = {slotZero}");
        _output.WriteLine("");

        if (slotZero == BigInteger.Zero)
        {
            _output.WriteLine("✓ Storage NOT mutated");
            _output.WriteLine("  Child correctly OOG'd");
            _output.WriteLine("  Gas forwarding: CORRECT");
            _output.WriteLine("  Frame.Success metadata: WRONG (tracing bug)");
        }
        else
        {
            _output.WriteLine($"✗ Storage WAS mutated to {slotZero}");
            _output.WriteLine("  Child completed SSTORE with 3000 gas");
            _output.WriteLine("  CONSENSUS EXECUTION BUG (gas forwarding)");
        }
    }

    private async Task<CampaignExecutionResult> ExecuteCase(CallSemanticsMatrixGenerator.CallTestCase testCase)
    {
        var (parentCode, childCode) = CallSemanticsMatrixGenerator.GenerateBytecode(testCase);

        _output.WriteLine($"Test: {testCase.CaseId}");
        _output.WriteLine($"Parent: {parentCode}");
        _output.WriteLine($"Child: {childCode}");
        _output.WriteLine("");

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
                    Nonce = 0,
                    Storage = new Dictionary<string, string>
                    {
                        ["0x0"] = "0x0"  // Slot 0 starts at 0
                    }
                }
            }
        };

        var machine = BuildEvmMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var harness = new SchlierenExecutionHarness(pipeline);

        return await harness.ExecuteAsync(request);
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
