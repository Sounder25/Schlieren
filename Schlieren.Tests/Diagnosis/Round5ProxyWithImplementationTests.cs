using Schlieren.UI.Services;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Schlieren.Tests.Diagnosis;

/// <summary>
/// Round 5: EIP-1967 proxy with real implementation state.
/// Tests nested DELEGATECALL execution (depth 2) and validates no false diagnostic.
/// </summary>
public class Round5ProxyWithImplementationTests
{
    [Fact]
    public async Task ProxyWithImplementation_ExecutesNestedCall_NoUnresolvedDiagnostic()
    {
        // Use the exact proxy runtime from Round 4 that we know works
        const string proxyCode = "6080604052600436106100345760003560e01c806301ffc9a7146100395780633659cfe61461006c575b610037610074565b005b34801561004557600080fd5b50610056600480360361005f565b6100a0565b6040516100639190610100565b60405180910390f35b61007461007c565b005b61007c6100e0565b565b61008d6000604051806100bb565b905090565b600080600090506100d4565b600081905090565b6000809050806100df565b90565b6100e8610150565b73ffffffffffffffffffffffffffffffffffffffff163660008037600080366000845af43d6000803e806000811461013e573d6000f35b3d6000fd5b565b6000610156610158565b905090565b60007f360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc60001b905080549150509056";
        
        // Simple implementation: returns 42
        const string implCode = "602a60005260206000f3";

        const string proxyAddr = "0x00000000000000000000000000000000000000aa";
        const string implAddr = "0x00000000000000000000000000000000000000bb";
        const string eip1967Slot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";

        var run = await BytecodeExecutionService.RunAsync(proxyCode, new BytecodeRunOptions
        {
            ForkLabel = "Cancun",
            ContractHex = proxyAddr,
            CallDataHex = "",
            ExtraAccounts =
            [
                new WorkbenchAccountSeed
                {
                    AddressHex = proxyAddr,
                    CodeHex = proxyCode,
                    StorageHex = new Dictionary<string, string>
                    {
                        [eip1967Slot] = "0x00000000000000000000000000000000000000000000000000000000000000bb"
                    }
                },
                new WorkbenchAccountSeed
                {
                    AddressHex = implAddr,
                    CodeHex = implCode
                }
            ]
        });

        // Assertions
        Assert.NotNull(run);
        Assert.True(run!.Result.IsSuccess, 
            $"Expected success, got: {run.Result.Error}");
        
        var trace = run.Result.TraceSteps;
        Assert.NotNull(trace);
        Assert.True(trace.Count > 5, $"Expected significant trace, got {trace.Count} steps");
        
        // Check for nested execution (depth 2 from DELEGATECALL)
        var depth2Steps = trace.Where(s => s.Depth == 2).ToList();
        Assert.True(depth2Steps.Count > 0, 
            $"Expected depth-2 execution (DELEGATECALL), found {depth2Steps.Count} steps at max depth {trace.Max(s => s.Depth)}");
        
        // Check that DELEGATECALL happened
        var delegatecall = trace.FirstOrDefault(s => s.Op == "DELEGATECALL");
        Assert.NotNull(delegatecall);
        
        // Check implementation slot was read
        var eip1967Read = trace.FirstOrDefault(s => s.Op == "SLOAD");
        Assert.NotNull(eip1967Read);
        
        // Run diagnostic detector
        var diagnostic = ProxyImplementationUnresolvedDetector.Analyze(trace);
        
        // CRITICAL: Should NOT report "unresolved" because implementation exists and was called
        Assert.Null(diagnostic);
    }
}
