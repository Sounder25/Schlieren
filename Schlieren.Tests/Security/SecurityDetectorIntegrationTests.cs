using Schlieren.Core.Execution;
using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

/// <summary>
/// Integration tests that exercise both ReentrancyDetector and StorageCollisionDetector together
/// on realistic attack scenarios.
/// </summary>
public class SecurityDetectorIntegrationTests
{
    [Fact]
    public void CombinedAttack_ReentrancyAndStorageCollision_BothDetected()
    {
        // Arrange - Simulate a combined reentrancy + storage collision attack:
        // 1. Proxy delegates to Implementation
        // 2. Implementation reads balance (slot 0)
        // 3. Implementation calls Attacker
        // 4. Attacker re-enters Proxy (REENTRANCY)
        // 5. Implementation writes to slot 0 (COLLISION - overwrites proxy owner)
        // 6. Implementation also writes to EIP-1967 implementation slot (COLLISION)
        
        var trace = new List<ExecutionTraceStep>
        {
            // Step 0: Proxy enters
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xProxy" },
            
            // Step 1: Proxy DELEGATECALLs to Implementation
            new() { Pc = 1, Op = "DELEGATECALL", Depth = 1, ContractAddress = "0xProxy" },
            
            // Step 2: Implementation contract (Depth 2, DELEGATECALL frame)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            // Step 3: Implementation reads balance from slot 0 (will be STALE)
            new() { Pc = 1, Op = "SLOAD", Depth = 2, ContractAddress = "0xImpl", 
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000000"] = "0x64" },
                    CallType = CallType.DelegateCall },
            
            // Step 4: Implementation calls Attacker contract
            new() { Pc = 2, Op = "CALL", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            // Step 5: Attacker contract (Depth 3)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 3, ContractAddress = "0xAttacker" },
            
            // Step 6: Attacker calls back to Proxy (REENTRANCY trigger!)
            new() { Pc = 1, Op = "CALL", Depth = 3, ContractAddress = "0xAttacker" },
            
            // Step 7-9: Proxy RE-ENTERED at depth 4 (same contract as step 0)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 4, ContractAddress = "0xProxy", CallerAddress = "0xAttacker" },
            new() { Pc = 1, Op = "SLOAD", Depth = 4, ContractAddress = "0xProxy",
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000000"] = "0x64" } },
            new() { Pc = 2, Op = "STOP", Depth = 4, ContractAddress = "0xProxy" },
            
            // Step 10: Back to Attacker
            new() { Pc = 2, Op = "STOP", Depth = 3, ContractAddress = "0xAttacker" },
            
            // Step 11: Back to Implementation - writes to slot 0 (COLLISION!)
            new() { Pc = 3, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl",
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000000"] = "0x32" },
                    CallType = CallType.DelegateCall },
            
            // Step 12: Implementation also writes to EIP-1967 implementation slot (COLLISION!)
            new() { Pc = 4, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl",
                    Storage = new() { ["0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc"] = "0xbad" },
                    CallType = CallType.DelegateCall },
            
            // Unwind
            new() { Pc = 5, Op = "STOP", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xProxy" }
        };
        
        // Act
        var reentrancyFindings = ReentrancyDetector.Analyze(trace);
        var collisionFindings = StorageCollisionDetector.Analyze(trace);
        
        // Assert - Reentrancy
        Assert.NotEmpty(reentrancyFindings);
        Assert.Contains(reentrancyFindings, f => f.TargetContract == "0xProxy");
        Assert.Contains(reentrancyFindings, f => f.DepthDelta >= 2); // Depth 1 -> Depth 4
        
        // Assert - Storage Collisions
        Assert.NotEmpty(collisionFindings);
        Assert.Contains(collisionFindings, f => f.CollisionType == StorageCollisionType.LegacySlotZero);
        Assert.Contains(collisionFindings, f => f.CollisionType == StorageCollisionType.Erc1967Implementation);
        
        // Log for visibility
        Console.WriteLine($"Reentrancy findings: {reentrancyFindings.Count}");
        Console.WriteLine($"Collision findings: {collisionFindings.Count}");
    }
    
    [Fact]
    public void SafeExecution_NoVulnerabilities_ReturnsEmpty()
    {
        // Arrange - Normal safe execution: no reentrancy, no collisions
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xSafe" },
            new() { Pc = 1, Op = "SLOAD", Depth = 1, ContractAddress = "0xSafe",
                    Storage = new() { ["0x01"] = "0x64" } },
            new() { Pc = 2, Op = "SSTORE", Depth = 1, ContractAddress = "0xSafe",
                    Storage = new() { ["0x01"] = "0x32" } },
            new() { Pc = 3, Op = "STOP", Depth = 1, ContractAddress = "0xSafe" }
        };
        
        // Act
        var reentrancy = ReentrancyDetector.Analyze(trace);
        var collisions = StorageCollisionDetector.Analyze(trace);
        
        // Assert - Both should return empty
        Assert.Empty(reentrancy);
        Assert.Empty(collisions);
    }
    
    [Fact]
    public void OnlyReentrancy_ReturnsReentrancyOnly()
    {
        // Arrange - Reentrancy without storage collision
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "CALL", Depth = 1, ContractAddress = "0xA" },
            
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 1, Op = "CALL", Depth = 2, ContractAddress = "0xB" },
            
            // Re-enter A at depth 3
            new() { Pc = 0, Op = "JUMPDEST", Depth = 3, ContractAddress = "0xA", CallerAddress = "0xB" },
            new() { Pc = 1, Op = "STOP", Depth = 3, ContractAddress = "0xA" },
            
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var reentrancy = ReentrancyDetector.Analyze(trace);
        var collisions = StorageCollisionDetector.Analyze(trace);
        
        // Assert
        Assert.NotEmpty(reentrancy);
        Assert.Empty(collisions); // No DELEGATECALL, no collision
    }
    
    [Fact]
    public void OnlyStorageCollision_ReturnsCollisionOnly()
    {
        // Arrange - Storage collision without reentrancy
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xProxy" },
            new() { Pc = 1, Op = "DELEGATECALL", Depth = 1, ContractAddress = "0xProxy" },
            
            // Implementation writes to slot 0 (collision) but no re-entrant calls
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            new() { Pc = 1, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl",
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000000"] = "0xbad" },
                    CallType = CallType.DelegateCall },
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xProxy" }
        };
        
        // Act
        var reentrancy = ReentrancyDetector.Analyze(trace);
        var collisions = StorageCollisionDetector.Analyze(trace);
        
        // Assert
        Assert.Empty(reentrancy); // No re-entry
        Assert.NotEmpty(collisions); // Collision detected
        Assert.Equal(StorageCollisionType.LegacySlotZero, collisions[0].CollisionType);
    }
    
    [Fact]
    public void AllSecurityTypes_ClassifiedCorrectly()
    {
        // Verify all enum values are accessible (auto-numbered from 0)
        Assert.Equal(2, (int)ReentrancySeverity.Critical);
        Assert.Equal(1, (int)ReentrancySeverity.Medium);
        Assert.Equal(0, (int)ReentrancySeverity.Info);
        
        Assert.Equal(0, (int)StorageCollisionType.LegacySlotZero);
        Assert.Equal(1, (int)StorageCollisionType.Erc1967Implementation);
        Assert.Equal(2, (int)StorageCollisionType.Erc1967Admin);
        Assert.Equal(3, (int)StorageCollisionType.ProxyLayoutOverlap);
        
        Assert.Equal(0, (int)CallType.Root);
        Assert.Equal(1, (int)CallType.Call);
        Assert.Equal(3, (int)CallType.DelegateCall);
        Assert.Equal(4, (int)CallType.StaticCall);
    }
}
