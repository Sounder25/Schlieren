namespace Scrutor.Core.Execution;

/// <summary>
/// Single EVM execution step used by debug trace RPCs.
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
}
