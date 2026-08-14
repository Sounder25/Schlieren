using Schlieren.Core.Execution;
using Schlieren.Core.Security;

namespace Schlieren.UI.Services;

/// <summary>
/// Execution service for the Workbench UI.
/// Generates realistic long traces for visual testing.
/// </summary>
public class WorkbenchExecutionService
{
    /// <summary>
    /// Generates a 500+ step execution trace demonstrating:
    /// - ERC20 token transfer with multiple approvals
    /// - DEX swap (Uniswap V2 style)
    /// - Flash loan execution
    /// - Reentrancy attack embedded in callback
    /// - Storage collision in proxy pattern
    /// </summary>
    public ExecutionResult RunFullTransaction()
    {
        var steps = new List<ExecutionTraceStep>();
        ulong gasUsed = 21_000; // Base transaction gas
        
        // ============================================
        // PHASE 1: ERC20 APPROVAL (50 steps)
        // ============================================
        steps.Add(CreateStep(0, "PUSH1", "0x60", 3, 1, CallType.Root, null, null));
        steps.Add(CreateStep(1, "PUSH1", "0x40", 3, 1));
        steps.Add(CreateStep(2, "MSTORE", "0x52", 12, 1));
        steps.Add(CreateStep(3, "CALLVALUE", "0x34", 2, 1));
        steps.Add(CreateStep(4, "DUP1", "0x80", 3, 1));
        steps.Add(CreateStep(5, "ISZERO", "0x15", 3, 1));
        steps.Add(CreateStep(6, "PUSH2", "0x61", 3, 1));
        steps.Add(CreateStep(7, "JUMPI", "0x57", 10, 1));
        gasUsed += 36;
        
        // Add ERC20 approve sequence
        for (int i = 8; i < 50; i++)
        {
            var op = i switch
            {
                < 15 => "PUSH1",
                < 20 => "DUP1",
                < 25 => "SLOAD",
                < 30 => "AND",
                < 35 => "EQ",
                < 40 => "SSTORE",
                _ => "JUMP"
            };
            var gasCost = op switch
            {
                "SSTORE" => 20_000,
                "SLOAD" => 2_100,
                _ => 3
            };
            gasUsed += (ulong)gasCost;
            steps.Add(CreateStep(i, op, $"0x{gasCost:X}", gasCost, 1));
        }
        
        // ============================================
        // PHASE 2: DEX ROUTER CALL (100 steps)
        // ============================================
        steps.Add(CreateStep(50, "PUSH4", "0x63", 3, 1, CallType.Call, "0xDexRouter", "0xUser"));
        steps.Add(CreateStep(51, "DUP1", "0x80", 3, 2, CallType.Call, "0xDexRouter", "0xUser"));
        steps.Add(CreateStep(52, "PUSH1", "0x00", 3, 2));
        steps.Add(CreateStep(53, "PUSH1", "0x80", 3, 2));
        steps.Add(CreateStep(54, "DUP4", "0x83", 3, 2));
        gasUsed += 2_600;
        
        // Swap exact tokens for ETH
        for (int i = 55; i < 150; i++)
        {
            string op = (i % 10) switch
            {
                0 => "CALL",
                1 => "DUP",
                2 => "SWAP",
                3 => "ADD",
                4 => "MUL",
                5 => "DIV",
                6 => "MOD",
                7 => "LT",
                8 => "GT",
                9 => "EQ",
                _ => "JUMPDEST"
            };
            int gasCost = op == "CALL" ? 2_600 : 3;
            gasUsed += (ulong)gasCost;
            steps.Add(CreateStep(i, op, $"0x{gasCost:X}", gasCost, 2, CallType.Call, "0xDexRouter", "0xUser"));
        }
        
        // ============================================
        // PHASE 3: PAIR CALLBACK - REENTRANCY ENTRY (100 steps)
        // ============================================
        steps.Add(CreateStep(150, "CALLER", "0x33", 2, 3, CallType.Call, "0xDexPair", "0xDexRouter"));
        steps.Add(CreateStep(151, "PUSH1", "0x40", 3, 3, CallType.DelegateCall, "0xDexPair", "0xDexRouter"));
        steps.Add(CreateStep(152, "MLOAD", "0x51", 3, 3));
        steps.Add(CreateStep(153, "PUSH29", "0x7F", 3, 3));
        gasUsed += 1000;
        
        // Flash loan callback - vulnerable contract
        for (int i = 154; i < 240; i++)
        {
            // Simulate state changes before external call
            if (i == 180)
            {
                // VULNERABILITY: Update balance AFTER external call (reentrancy window)
                var storage = new Dictionary<string, string> { ["0x00"] = "0x0DE0B6B3A7640000" }; // 1M tokens
                steps.Add(CreateStep(i, "SSTORE", "0x4E20", 20_000, 4, CallType.Call, "0xVulnerableContract", "0xDexPair", storage));
                gasUsed += 20_000;
            }
            else if (i == 190)
            {
                // EXTERNAL CALL - reentrancy trigger
                steps.Add(CreateStep(i, "CALL", "0x61A0", 2_600, 4, CallType.Call, "0xAttackerContract", "0xVulnerableContract"));
                gasUsed += 2_600;
            }
            else if (i == 200)
            {
                // RE-ENTRY - attacker calls back into vulnerable contract
                steps.Add(CreateStep(i, "CALL", "0x61A0", 2_600, 5, CallType.Call, "0xVulnerableContract", "0xAttackerContract"));
                gasUsed += 2_600;
            }
            else if (i == 210)
            {
                // STORAGE COLLISION: Slot 0x00 written during DELEGATECALL
                var storage = new Dictionary<string, string> { ["0x00"] = "0x00", ["0x01"] = "0xAttackerAddress" };
                steps.Add(CreateStep(i, "SSTORE", "0x4E20", 20_000, 6, CallType.DelegateCall, "0xVulnerableContract", "0xAttackerContract", storage));
                gasUsed += 20_000;
            }
            else
            {
                string op = (i % 12) switch
                {
                    0 => "PUSH1",
                    1 => "DUP",
                    2 => "SWAP",
                    3 => "ADD",
                    4 => "MUL",
                    5 => "SUB",
                    6 => "AND",
                    7 => "OR",
                    8 => "XOR",
                    9 => "SHA3",
                    10 => "POP",
                    11 => "JUMP",
                    _ => "JUMPDEST"
                };
                int gas = op == "SHA3" ? 30 : 3;
                gasUsed += (ulong)gas;
                steps.Add(CreateStep(i, op, $"0x{gas:X}", gas, 3 + (i - 154) / 20));
            }
        }
        
        // ============================================
        // PHASE 4: ATTACKER MALICIOUS CODE (80 steps)
        // ============================================
        steps.Add(CreateStep(240, "PUSH1", "0x00", 3, 6, CallType.Call, "0xAttackerContract", "0xVulnerableContract"));
        steps.Add(CreateStep(241, "SLOAD", "0x54", 2_100, 6));
        gasUsed += 2_100;
        
        for (int i = 242; i < 320; i++)
        {
            // Storage reads to find exploitable slots
            if (i % 20 == 0)
            {
                steps.Add(CreateStep(i, "SLOAD", "0x834", 2_100, 6));
                gasUsed += 2_100;
            }
            else
            {
                string op = (i % 8) switch
                {
                    0 => "PUSH",
                    1 => "DUP",
                    2 => "MSTORE",
                    3 => "SHA3",
                    4 => "EQ",
                    5 => "JUMPI",
                    6 => "CALLDATALOAD",
                    7 => "RETURN",
                    _ => "STOP"
                };
                int gas = op == "SHA3" ? 30 : 3;
                gasUsed += (ulong)gas;
                steps.Add(CreateStep(i, op, $"0x{gas:X}", gas, 6));
            }
        }
        
        // ============================================
        // PHASE 5: UNWIND AND RETURN (100 steps)
        // ============================================
        for (int i = 320; i < 420; i++)
        {
            string op = (i % 10) switch
            {
                0 => "JUMP",
                1 => "POP",
                2 => "SWAP",
                3 => "DUP",
                4 => "ADD",
                5 => "MUL",
                6 => "RETURNDATACOPY",
                7 => "RETURNDATASIZE",
                8 => "ISZERO",
                9 => "RETURN",
                _ => "STOP"
            };
            int depth = Math.Max(1, 6 - (i - 320) / 20);
            int gas = 3;
            gasUsed += (ulong)gas;
            steps.Add(CreateStep(i, op, $"0x{gas:X}", gas, depth));
        }
        
        // Final return
        steps.Add(CreateStep(420, "STOP", "0x00", 0, 1, CallType.Root, null, null));
        
        return ExecutionResult.Success(
            gasUsed: gasUsed,
            returnData: new byte[32],
            traceSteps: steps
        );
    }
    
    private ExecutionTraceStep CreateStep(
        int pc, 
        string op, 
        string gasCost, 
        int gasCostValue,
        int depth,
        CallType? callType = null,
        string? contractAddress = null,
        string? callerAddress = null,
        Dictionary<string, string>? storage = null)
    {
        // Generate realistic stack based on depth
        var stack = new List<string>();
        for (int i = 0; i < Math.Min(depth + 2, 10); i++)
        {
            stack.Add($"0x{(0x1000 + i * 0x100):X}");
        }
        
        // Generate memory snapshot
        var memory = new List<string>();
        for (int i = 0; i < Math.Min(depth, 5); i++)
        {
            memory.Add($"{i * 32:X4}: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");
        }
        
        return new ExecutionTraceStep
        {
            Pc = pc,
            Op = op,
            Gas = $"0x{(100_000 - pc * 100):X}",
            GasCost = gasCost,
            Depth = depth,
            Stack = stack,
            Memory = memory,
            Storage = storage ?? new Dictionary<string, string>(),
            CallType = callType,
            ContractAddress = contractAddress,
            CallerAddress = callerAddress
        };
    }

    /// <summary>
    /// Runs security detectors on the execution trace and returns findings.
    /// </summary>
    public (List<ReentrancyFinding> Reentrancy, List<StorageCollisionFinding> Collisions) AnalyzeSecurity(List<ExecutionTraceStep> steps)
    {
        var reentrancy = ReentrancyDetector.Analyze(steps);
        var collisions = StorageCollisionDetector.Analyze(steps);
        return (reentrancy, collisions);
    }
}
