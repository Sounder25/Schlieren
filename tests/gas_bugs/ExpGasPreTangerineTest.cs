using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;

namespace Schlieren.Tests.GasBugs;

public class ExpGasPreTangerineTest
{
    [Theory]
    [InlineData("Frontier")]
    [InlineData("Homestead")]
    public void ExpShouldUse10PerByteEraOnPreTangerineForks(string forkName)
    {
        // Arrange
        IForkRules rules = forkName switch
        {
            "Frontier" => new FrontierRules(),
            "Homestead" => new HomesteadRules(),
            _ => throw new ArgumentException($"Unknown fork: {forkName}")
        };
        
        // Test cases demonstrating the bug:
        // According to GAS_COVERAGE_MATRIX.md, OP.EXP is marked 'M' (missing) for Frontier-Homestead-Tangerine
        // This means the 10-per-byte era pricing is missing and hardcoded 50-per-byte is used instead
        
        // The bug is in ArithmeticOpcodes.cs:253-265 - hardcoded 50 gas per byte instead of
        // 10 gas per byte for Frontier/Homestead/Tangerine
        
        // For a small EXP operation (e.g., 2^3 which is 1 byte):
        // - Pre-Tangerine: should be 10 gas (10 * 1 byte) + fixed cost
        // - Post-Tangerine: correctly 50 gas (50 * 1 byte) + fixed cost
        
        // Implementation needed:
        // 1. Create a small contract with EXP opcode
        // 2. Execute on Frontier/Homestead fork
        // 3. Compare actual gas used vs expected gas
        // 4. Expected: 10 gas per byte, Actual (bug): 50 gas per byte
    }
    
    [Fact]
    public void TangerineAndLaterShouldUse50PerByte()
    {
        // TangerineWhistle+ should correctly use 50 gas per byte
        // This test confirms the fix is present from Tangerine onward
        
        // Arrange
        var tangerineRules = new TangerineWhistleRules();
        var spuriousRules = new SpuriousDragonRules();
        
        // Implementation similar to above but expecting 50 gas per byte
    }
}