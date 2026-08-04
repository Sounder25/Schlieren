using Scrutor.Core.Execution;
using Scrutor.Core.Security;
using Xunit;

namespace Scrutor.Tests.Security;

public class StorageCollisionDetectorTests
{
    [Fact]
    public void Analyze_EmptyTrace_ReturnsNoFindings()
    {
        var result = StorageCollisionDetector.Analyze(new List<ExecutionTraceStep>());
        
        Assert.Empty(result);
    }
    
    [Fact]
    public void Analyze_NoDelegateCall_ReturnsNoFindings()
    {
        // Arrange - regular CALL (not DELEGATECALL), SSTORE to slot 0
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "SSTORE", Depth = 1, ContractAddress = "0xA", 
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000000"] = "0xdead" },
                    CallType = CallType.Call },
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var findings = StorageCollisionDetector.Analyze(trace);
        
        // Assert - no collision because it's not a DELEGATECALL
        Assert.Empty(findings);
    }
    
    [Fact]
    public void Analyze_DelegateCallSstoreSlotZero_DetectsCollision()
    {
        // Arrange - DELEGATECALL to implementation that writes slot 0x00
        var trace = new List<ExecutionTraceStep>
        {
            // Proxy contract (Depth 1)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xProxy" },
            new() { Pc = 1, Op = "DELEGATECALL", Depth = 1, ContractAddress = "0xProxy" },
            
            // Implementation contract (Depth 2, DELEGATECALL)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            new() { Pc = 1, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl", 
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000000"] = "0xbeef" },
                    CallType = CallType.DelegateCall },
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            // Back to Proxy
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xProxy" }
        };
        
        // Act
        var findings = StorageCollisionDetector.Analyze(trace);
        
        // Assert
        Assert.NotEmpty(findings);
        var finding = findings[0];
        Assert.Equal(StorageCollisionType.LegacySlotZero, finding.CollisionType);
        Assert.Contains("0xProxy", finding.ProxyContract);
        Assert.Contains("0xImpl", finding.ImplementationContract);
        Assert.Equal("0x0000000000000000000000000000000000000000000000000000000000000000".ToLowerInvariant(), 
                     finding.CollidingSlot.ToLowerInvariant());
    }
    
    [Fact]
    public void Analyze_DelegateCallWritesErc1967Implementation_DetectsCollision()
    {
        // Arrange - DELEGATECALL that corrupts EIP-1967 implementation slot
        var erc1967ImplSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bbc";
        
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xProxy" },
            new() { Pc = 1, Op = "DELEGATECALL", Depth = 1, ContractAddress = "0xProxy" },
            
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            new() { Pc = 1, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl",
                    Storage = new() { [erc1967ImplSlot] = "0xbad" },
                    CallType = CallType.DelegateCall },
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xProxy" }
        };
        
        // Act
        var findings = StorageCollisionDetector.Analyze(trace);
        
        // Assert
        Assert.NotEmpty(findings);
        Assert.Equal(StorageCollisionType.Erc1967Implementation, findings[0].CollisionType);
        Assert.Contains("DELEGATECALL", findings[0].Description);
    }
    
    [Fact]
    public void Analyze_DelegateCallWritesErc1967Admin_DetectsCollision()
    {
        // Arrange - DELEGATECALL that corrupts EIP-1967 admin slot
        var erc1967AdminSlot = "0xb535470464514b7b90209420923d607555bbe57d57f7e2f322fce670654068d3";
        
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xProxy" },
            new() { Pc = 1, Op = "DELEGATECALL", Depth = 1, ContractAddress = "0xProxy" },
            
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            new() { Pc = 1, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl",
                    Storage = new() { [erc1967AdminSlot] = "0xbad" },
                    CallType = CallType.DelegateCall },
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xProxy" }
        };
        
        // Act
        var findings = StorageCollisionDetector.Analyze(trace);
        
        // Assert
        Assert.NotEmpty(findings);
        Assert.Equal(StorageCollisionType.Erc1967Admin, findings[0].CollisionType);
    }
    
    [Fact]
    public void Analyze_DelegateCallWritesNonReservedSlot_ReturnsNoFindings()
    {
        // Arrange - DELEGATECALL that writes to a non-reserved slot
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xProxy" },
            new() { Pc = 1, Op = "DELEGATECALL", Depth = 1, ContractAddress = "0xProxy" },
            
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            // Writing to slot 0x01 (not reserved)
            new() { Pc = 1, Op = "SSTORE", Depth = 2, ContractAddress = "0xImpl",
                    Storage = new() { ["0x0000000000000000000000000000000000000000000000000000000000000001"] = "0xbad" },
                    CallType = CallType.DelegateCall },
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xImpl", CallType = CallType.DelegateCall },
            
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xProxy" }
        };
        
        // Act
        var findings = StorageCollisionDetector.Analyze(trace);
        
        // Assert - slot 0x01 is not reserved, so no collision
        Assert.Empty(findings);
    }
    
    [Fact]
    public void StorageCollisionFinding_HasRequiredFields()
    {
        // Arrange
        var finding = new StorageCollisionFinding
        {
            CollisionType = StorageCollisionType.LegacySlotZero,
            ProxyContract = "0xProxy",
            ImplementationContract = "0xImpl",
            CollidingSlot = "0x00",
            WrittenValue = "0xbeef",
            StepIndex = 42,
            Description = "Test collision"
        };
        
        // Assert
        Assert.Equal(StorageCollisionType.LegacySlotZero, finding.CollisionType);
        Assert.Equal("0xProxy", finding.ProxyContract);
        Assert.Equal("0xImpl", finding.ImplementationContract);
        Assert.Equal("0x00", finding.CollidingSlot);
        Assert.Equal("0xbeef", finding.WrittenValue);
        Assert.Equal(42, finding.StepIndex);
        Assert.Equal("Test collision", finding.Description);
    }
    
    [Fact]
    public void Erc1967Slots_AreCorrectlyDefined()
    {
        // Verify the EIP-1967 slots are correct per spec
        Assert.StartsWith("0x36", StorageCollisionDetector.Erc1967ImplementationSlot);
        Assert.StartsWith("0xb5", StorageCollisionDetector.Erc1967AdminSlot);
        Assert.Equal("0x0000000000000000000000000000000000000000000000000000000000000000", 
                     StorageCollisionDetector.LegacySlotZero);
    }
}
