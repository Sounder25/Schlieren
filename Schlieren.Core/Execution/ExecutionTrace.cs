namespace Schlieren.Core.Execution;

/// <summary>
/// Type of call frame for security analysis and UI visualization.
/// </summary>
public enum CallType
{
    /// <summary>
    /// Root transaction execution (not a sub-call).
    /// </summary>
    Root,
    
    /// <summary>
    /// Standard CALL opcode.
    /// </summary>
    Call,
    
    /// <summary>
    /// CALLCODE opcode (deprecated) — executes external code in caller's context.
    /// </summary>
    CallCode,
    
    /// <summary>
    /// DELEGATECALL opcode — executes external code in caller's storage context.
    /// Critical for proxy patterns and storage collision detection.
    /// </summary>
    DelegateCall,
    
    /// <summary>
    /// STATICCALL opcode — read-only call, cannot modify state.
    /// </summary>
    StaticCall,
    
    /// <summary>
    /// CREATE opcode — contract deployment.
    /// </summary>
    Create,
    
    /// <summary>
    /// CREATE2 opcode — deterministic contract deployment.
    /// </summary>
    Create2
}

/// <summary>
/// Single EVM execution step used by debug trace RPCs and security analysis.
/// </summary>
public sealed class ExecutionTraceStep
{
    public int Pc { get; init; }
    public string Op { get; init; } = string.Empty;
    public string Gas { get; init; } = "0x0";
    public string GasCost { get; init; } = "0x0";
    public int Depth { get; init; } = 1;
    public List<string> Stack { get; init; } = new();
    public List<string> Memory { get; init; } = new();
    public Dictionary<string, string> Storage { get; init; } = new();
    
    /// <summary>
    /// Call type for this frame. Null for steps that don't represent a call boundary.
    /// Set only when this step is the first step of a new call frame.
    /// </summary>
    public CallType? CallType { get; init; }
    
    /// <summary>
    /// Address of the contract being executed in this frame.
    /// Null for root transaction until first context is established.
    /// </summary>
    public string? ContractAddress { get; init; }
    
    /// <summary>
    /// Address of the caller (msg.sender in Solidity terms).
    /// Null for root transaction.
    /// </summary>
    public string? CallerAddress { get; init; }
    
    /// <summary>
    /// For DELEGATECALL/CALLCODE: the address whose code is being executed.
    /// Null for CALL, STATICCALL, CREATE, CREATE2.
    /// </summary>
    public string? CodeAddress { get; init; }
}
