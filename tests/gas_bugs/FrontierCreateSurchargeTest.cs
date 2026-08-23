using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;

namespace Schlieren.Tests.GasBugs;

public class FrontierCreateSurchargeTest
{
    [Fact]
    public void FrontierShouldNotHaveCreateSurcharge()
    {
        // Arrange
        var frontierRules = new FrontierRules();
        var homesteadRules = new HomesteadRules();
        
        var createTx = new Transaction
        {
            To = null, // CREATE transaction
            Data = new byte[] { 0x60, 0x00, 0x60, 0x00, 0x55, 0x00 }, // PUSH1 0 PUSH1 0 SSTORE STOP
            GasLimit = 6000000,
            GasPrice = 1,
            Nonce = 0,
            Value = 0
        };
        
        // Act
        var frontierIntrinsic = IntrinsicGas.Compute(createTx, frontierRules);
        var homesteadIntrinsic = IntrinsicGas.Compute(createTx, homesteadRules);
        
        // Assert
        // Base intrinsic gas for CREATE with 6 bytes of data:
        // - TX.BASE: 21,000
        // - Calldata: 6 bytes * 68 (Frontier non-zero byte) = 408
        // - Frontier: NO 32,000 surcharge
        // - Homestead: HAS 32,000 surcharge
        var expectedFrontier = 21000UL + (6UL * 68UL); // 21,000 + 408 = 21,408
        var expectedHomestead = expectedFrontier + 32000UL; // 21,408 + 32,000 = 53,408
        
        Assert.Equal(expectedFrontier, frontierIntrinsic);
        Assert.Equal(expectedHomestead, homesteadIntrinsic);
        
        // The bug would show as frontierIntrinsic == homesteadIntrinsic (both have surcharge)
    }
}