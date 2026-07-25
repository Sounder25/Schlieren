using Scrutor.EELS.Tests.Harness;
using Scrutor.Core.Execution;
using Scrutor.Core.State;
using Xunit;
using Xunit.Abstractions;

namespace Scrutor.EELS.Tests.Diagnostics;

public class GasTraceExtractor
{
    private readonly ITestOutputHelper _output;

    public GasTraceExtractor(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Extract_TLOAD_BasicAfterStore_Gas_Trace()
    {
        // Load the basic_tload_after_store fixture manually
        var options = EelsHarnessOptions.FromEnvironment() with
        {
            IncludeSubdirectories = true,
            MaxCases = int.MaxValue
        };

        var loader = new EelsStateFixtureLoader();
        var allCases = loader.LoadCases(options);
        var testCase = allCases.FirstOrDefault(c => c.CaseId.Contains("basic_tload_after_store"));

        if (testCase == null)
        {
            _output.WriteLine("basic_tload_after_store fixture not found");
            return;
        }

        // Execute with tracing enabled
        var globalState = new GlobalState();
        foreach (var (address, account) in testCase.PreState)
        {
            globalState.SetBalance(address, account.Balance);
            globalState.SetNonce(address, account.Nonce);
            globalState.SetCode(address, account.Code);
            foreach (var (slot, value) in account.Storage)
            {
                globalState.SetStorageAt(address, slot, value);
            }
        }

        // Set EnableTracing on the transaction
        testCase.Transaction.EnableTracing = true;
        
        var opcodes = OpcodeCatalog.CreateAll();
        var stateTransition = new StateTransition(new EvmMachine(opcodes));
        var result = await stateTransition.ApplyTransactionAsync(testCase.Transaction, globalState, testCase.BlockContext, commit: true);

        _output.WriteLine($"=== SCRUTOR GAS TRACE: basic_tload_after_store ===");
        _output.WriteLine($"Case ID: {testCase.CaseId}");
        _output.WriteLine($"Result.GasUsed: {result.GasUsed}");
        _output.WriteLine("");
        _output.WriteLine("PC  | Opcode       | Cost  | Cumulative");
        _output.WriteLine("----|--------------|-------|------------");

        ulong cumulative = 21000; // intrinsic gas
        foreach (var step in result.TraceSteps)
        {
            var gasCost = Convert.ToUInt64(step.GasCost.TrimStart('0', 'x'), 16);
            cumulative += gasCost;
            _output.WriteLine($"{step.Pc,3} | {step.Op,-12} | {gasCost,5} | {cumulative,10}");
        }
        
        _output.WriteLine("");
        _output.WriteLine($"Final cumulative (with intrinsic): {cumulative}");
        _output.WriteLine($"Result.GasUsed: {result.GasUsed}");
    }
}

