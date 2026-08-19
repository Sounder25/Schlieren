using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Schlieren.UI.Services;

namespace Schlieren.Tests.Regression;

/// <summary>
/// Golden corpus: hand-picked "known hard" contracts that exposed real bugs.
/// Every case here caught an actual defect and serves as permanent regression.
/// 
/// STATUS: Test framework ready, needs real bytecode from muscle/ traces.
/// </summary>
public class GoldenCorpusTests
{
    [Fact(Skip = "Needs actual TokenLib bytecode from Round 1 trace")]
    public async Task Round1_LibraryGuard_NoFalsePositiveStorage()
    {
        // TokenLib mainnet contract (0x4EBF...254a)
        // Bug discovered: false-positive storage recommendation when no SLOAD/SSTORE
        // TODO: Extract bytecode from muscle/round1-tokenlib-trace.json
        var testCase = new RegressionCase
        {
            Name = "Round1_LibraryGuard",
            ContractCode = "0x...", // TODO: Real bytecode
            ContractAddress = "0x00000000000000000000000000000000000000aa",
            Calldata = "",
            Fork = "Cancun",
            ExpectedSuccess = false, // Library guard reverts on CALL
            ExpectedGas = 21043,
            ExpectedDiagnosticCount = 1, // Library guard detected
            ExpectedReentrancyCount = 0
        };

        var result = await DifferentialRegressionRunner.RunCaseAsync(testCase);
        
        Assert.Equal(RegressionStatus.Pass, result.Status);
    }

    [Fact(Skip = "Needs actual proxy bytecode from Round 4 trace")]
    public async Task Round4_ProxyUnresolved_DiagnosticNotVulnerability()
    {
        // Proxy with EIP-1967 slot = 0x0, DELEGATECALL to address(0)
        // Bug discovered: Should be Diagnostic (context), not SecurityFinding (vulnerability)
        // TODO: Extract bytecode from muscle/proxy-empty-calldata.json
        var testCase = new RegressionCase
        {
            Name = "Round4_ProxyUnresolved",
            ContractCode = "0x...", // TODO: Real bytecode
            ContractAddress = "0x00000000000000000000000000000000000000aa",
            Calldata = "",
            Fork = "Cancun",
            ExpectedSuccess = false,
            ExpectedGas = 28161,
            ExpectedDiagnosticCount = 1, // Proxy unresolved
            ExpectedReentrancyCount = 0
        };

        var result = await DifferentialRegressionRunner.RunCaseAsync(testCase);
        
        Assert.Equal(RegressionStatus.Pass, result.Status);
    }

    [Fact(Skip = "Needs actual proxy + implementation bytecode from Round 5 pre-state")]
    public async Task Round5_SuccessfulDelegatecall_NoGasDoubleCount_NoReentrancyFalsePositive()
    {
        // Real proxy → implementation execution (depth 2)
        // Bug #3: Audit double-counted child gas (+2,279)
        // Bug #4: DELEGATECALL falsely classified as reentrancy (33 findings)
        // TODO: Extract from muscle/round5-weth-proxy-prestate.json + successful trace
        const string proxyCode = "0x..."; // TODO
        const string implCode = "0x..."; // TODO
        const string proxyAddr = "0x00000000000000000000000000000000000000aa";
        const string implAddr = "0x00000000000000000000000000000000000000bb";
        const string eip1967Slot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";

        var testCase = new RegressionCase
        {
            Name = "Round5_SuccessfulDelegatecall",
            ContractCode = proxyCode,
            ContractAddress = proxyAddr,
            Calldata = "0x0c55699c", // Corrected selector
            Fork = "Cancun",
            PreState = new List<WorkbenchAccountSeed>
            {
                new()
                {
                    AddressHex = proxyAddr,
                    CodeHex = proxyCode,
                    StorageHex = new Dictionary<string, string>
                    {
                        [eip1967Slot] = "0x00000000000000000000000000000000000000000000000000000000000000bb"
                    }
                },
                new()
                {
                    AddressHex = implAddr,
                    CodeHex = implCode
                }
            },
            ExpectedSuccess = true,
            ExpectedGas = 28279, // After Bug #3 fix
            ExpectedDiagnosticCount = 0, // Implementation exists
            ExpectedReentrancyCount = 0  // After Bug #4 fix
        };

        var result = await DifferentialRegressionRunner.RunCaseAsync(testCase);
        
        Assert.Equal(RegressionStatus.Pass, result.Status);
        
        // Explicit invariant checks
        Assert.NotNull(result.ActualGas);
        Assert.Equal(28279UL, result.ActualGas.Value); // Trace = audit
    }
    
    /// <summary>
    /// Smoke test: validates the regression framework itself with minimal bytecode.
    /// </summary>
    [Fact]
    public async Task Smoke_MinimalReturn()
    {
        // PUSH1 42 PUSH1 0 MSTORE PUSH1 32 PUSH1 0 RETURN
        var testCase = new RegressionCase
        {
            Name = "Smoke_MinimalReturn",
            ContractCode = "0x602a60005260206000f3",
            ContractAddress = "0x00000000000000000000000000000000000000aa",
            Calldata = "",
            Fork = "Cancun",
            ExpectedSuccess = true,
            ExpectedGas = 21018, // 21000 intrinsic + PUSH1(3)+PUSH1(3)+MSTORE(3+3 mem)+PUSH1(3)+PUSH1(3)+RETURN(0)
            ExpectedReentrancyCount = 0
        };

        var result = await DifferentialRegressionRunner.RunCaseAsync(testCase);
        
        Assert.Equal(RegressionStatus.Pass, result.Status);
        Assert.Equal(21018UL, result.ActualGas);
    }
}
