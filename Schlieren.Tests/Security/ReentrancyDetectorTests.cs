using Schlieren.Core.Execution;
using Schlieren.Core.Security;
using Xunit;

namespace Schlieren.Tests.Security;

public class ReentrancyDetectorTests
{
    [Fact]
    public void Analyze_EmptyTrace_ReturnsNoFindings()
    {
        var result = ReentrancyDetector.Analyze(new List<ExecutionTraceStep>());
        
        Assert.Empty(result);
    }
    
    [Fact]
    public void Analyze_SingleFrame_NoReentrancy()
    {
        // Arrange - simple single-frame execution
        var trace = new List<ExecutionTraceStep>
        {
            new() { Pc = 0, Op = "PUSH1", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 2, Op = "SLOAD", Depth = 1, ContractAddress = "0xA", Storage = new() { ["0x00"] = "0x64" } },
            new() { Pc = 4, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var findings = ReentrancyDetector.Analyze(trace);
        
        // Assert
        Assert.Empty(findings);
    }
    
    [Fact]
    public void Analyze_DifferentContracts_NoReentrancy()
    {
        // Arrange - two different contracts, no reentry
        var trace = new List<ExecutionTraceStep>
        {
            // Frame 1: Contract A
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "CALL", Depth = 1, ContractAddress = "0xA" },
            // Frame 2: Contract B (different contract)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 1, Op = "STOP", Depth = 2, ContractAddress = "0xB" },
            // Back to A
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var findings = ReentrancyDetector.Analyze(trace);
        
        // Assert - no reentrancy because B is not A
        Assert.Empty(findings);
    }
    
    [Fact]
    public void Analyze_SameContractReentry_DetectsReentrancy()
    {
        // Arrange - classic reentrancy pattern
        var trace = new List<ExecutionTraceStep>
        {
            // Frame 1: Contract A reads balance
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "SLOAD", Depth = 1, ContractAddress = "0xA", 
                    Storage = new() { ["0x00"] = "0x64" } }, // balance = 100
            new() { Pc = 2, Op = "CALL", Depth = 1, ContractAddress = "0xA" },
            
            // Frame 2: Attacker contract calls back
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 1, Op = "CALL", Depth = 2, ContractAddress = "0xB" }, // calls back to A
            
            // Frame 3: REENTRANT CALL to Contract A
            new() { Pc = 0, Op = "JUMPDEST", Depth = 3, ContractAddress = "0xA", CallerAddress = "0xB" }, // REENTRY!
            new() { Pc = 1, Op = "SLOAD", Depth = 3, ContractAddress = "0xA",
                    Storage = new() { ["0x00"] = "0x64" } }, // reads STALE balance
            new() { Pc = 2, Op = "STOP", Depth = 3, ContractAddress = "0xA" },
            
            // Back to Frame 2
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xB" },
            
            // Back to Frame 1
            new() { Pc = 3, Op = "SSTORE", Depth = 1, ContractAddress = "0xA",
                    Storage = new() { ["0x00"] = "0x32" } }, // too late!
            new() { Pc = 4, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var findings = ReentrancyDetector.Analyze(trace);
        
        // Assert
        Assert.NotEmpty(findings);
        var finding = findings[0];
        Assert.Equal("0xA", finding.TargetContract);
        Assert.Equal(ReentrancySeverity.Critical, finding.Severity);
        Assert.Contains("re-entered", finding.Description);
    }
    
    [Fact]
    public void Analyze_MultipleReentries_DetectsAll()
    {
        // Arrange - nested reentrancy
        var trace = new List<ExecutionTraceStep>
        {
            // Frame 1: Contract A
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "CALL", Depth = 1, ContractAddress = "0xA" },
            
            // Frame 2: Contract B calls A
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 1, Op = "CALL", Depth = 2, ContractAddress = "0xB" },
            
            // Frame 3: REENTRANT to A
            new() { Pc = 0, Op = "JUMPDEST", Depth = 3, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "CALL", Depth = 3, ContractAddress = "0xA" },
            
            // Frame 4: REENTRANT to A again (double reentry!)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 4, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "STOP", Depth = 4, ContractAddress = "0xA" },
            
            // Unwind
            new() { Pc = 2, Op = "STOP", Depth = 3, ContractAddress = "0xA" },
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var findings = ReentrancyDetector.Analyze(trace);
        
        // Assert - detector finds multiple reentries because A appears at depths 1, 3, 4
        // Frame 3 (depth 3) re-enters A which was active at depth 1
        // Frame 4 (depth 4) re-enters A which was active at depths 1 and 3
        Assert.True(findings.Count >= 2, "Should detect at least 2 reentries");
        Assert.All(findings, f => Assert.Equal("0xA", f.TargetContract));
    }
    
    [Fact]
    public void Analyze_NoPostCallMutation_MediumSeverity()
    {
        // Arrange - reentry but no state modification after call
        var trace = new List<ExecutionTraceStep>
        {
            // Frame 1: Contract A
            new() { Pc = 0, Op = "JUMPDEST", Depth = 1, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "CALL", Depth = 1, ContractAddress = "0xA" },
            
            // Frame 2: Contract B
            new() { Pc = 0, Op = "JUMPDEST", Depth = 2, ContractAddress = "0xB" },
            new() { Pc = 1, Op = "CALL", Depth = 2, ContractAddress = "0xB" },
            
            // Frame 3: REENTRANT to A (no mutations in frame 1 after this)
            new() { Pc = 0, Op = "JUMPDEST", Depth = 3, ContractAddress = "0xA" },
            new() { Pc = 1, Op = "STOP", Depth = 3, ContractAddress = "0xA" },
            
            // Back to Frame 2
            new() { Pc = 2, Op = "STOP", Depth = 2, ContractAddress = "0xB" },
            
            // Back to Frame 1 - no SSTORE after call
            new() { Pc = 2, Op = "STOP", Depth = 1, ContractAddress = "0xA" }
        };
        
        // Act
        var findings = ReentrancyDetector.Analyze(trace);
        
        // Assert - should be Medium severity (no post-call mutation)
        Assert.NotEmpty(findings);
        Assert.Equal(ReentrancySeverity.Medium, findings[0].Severity);
    }
    
    [Fact]
    public void ReentrancyFinding_HasRequiredFields()
    {
        // Arrange
        var finding = new ReentrancyFinding
        {
            Severity = ReentrancySeverity.Critical,
            TargetContract = "0xA",
            AttackerContract = "0xB",
            InitialEntryStep = 10,
            ReentryStep = 25,
            DepthDelta = 2,
            MutatedStorageSlots = new List<string> { "0x00", "0x01" },
            Description = "Test finding"
        };
        
        // Assert
        Assert.Equal(ReentrancySeverity.Critical, finding.Severity);
        Assert.Equal("0xA", finding.TargetContract);
        Assert.Equal("0xB", finding.AttackerContract);
        Assert.Equal(10, finding.InitialEntryStep);
        Assert.Equal(25, finding.ReentryStep);
        Assert.Equal(2, finding.DepthDelta);
        Assert.Equal(2, finding.MutatedStorageSlots.Count);
        Assert.Equal("Test finding", finding.Description);
    }
}
