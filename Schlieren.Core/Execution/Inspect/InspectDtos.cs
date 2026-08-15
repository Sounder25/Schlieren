using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schlieren.Core.Execution.Inspect;

public sealed class InspectResult
{
    public bool Ok { get; init; } = true;
    public string Fork { get; init; } = "";
    public InspectExecution Execution { get; init; } = new();
    public InspectTrace Trace { get; init; } = new();
    public InspectGasNode? GasTree { get; init; }
    public InspectDiagnosis? Diagnosis { get; init; }
}

public sealed class InspectExecution
{
    public bool Success { get; init; }
    public string Error { get; init; } = "None";
    public string GasUsed { get; init; } = "0x0";
    public string GasLimit { get; init; } = "0x0";
    public string RefundCounter { get; init; } = "0x0";
    public string ReturnValue { get; init; } = "0x";
}

public sealed class InspectTrace
{
    public List<InspectStructLog> StructLogs { get; init; } = new();
}

public sealed class InspectStructLog
{
    public int Pc { get; init; }
    public string Op { get; init; } = "";
    public string Gas { get; init; } = "0x0";
    public string GasCost { get; init; } = "0x0";
    public int GasCostDec { get; init; }
    public int Depth { get; init; } = 1;
    public List<string> Stack { get; init; } = new();
    public List<string> Memory { get; init; } = new();
    public Dictionary<string, string> Storage { get; init; } = new();
    public string? Contract { get; init; }
    public string? Caller { get; init; }
    public string? CallType { get; init; }
    public string? Output { get; init; }
}

public sealed class InspectGasNode
{
    public string Label { get; init; } = "";
    public ulong Gas { get; init; }
    public ulong TotalGas { get; init; }
    public List<InspectGasNode> Children { get; init; } = new();
}

public sealed class InspectDiagnosis
{
    public string Fingerprint { get; init; } = "";
    public string FirstPhase { get; init; } = "";
    public InspectDiagnosisHit? Root { get; init; }
    public List<InspectDiagnosisHit> Candidates { get; init; } = new();
}

public sealed class InspectDiagnosisHit
{
    public string RuleId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Grade { get; init; } = "POSSIBLE";
    public int Score { get; init; }
    public string Phase { get; init; } = "";
    public string Why { get; init; } = "";
    public string Proof { get; init; } = "";
    public string Consequences { get; init; } = "";
    public string LikelyFix { get; init; } = "";
    public string CodeBoundary { get; init; } = "";
    public string ProtocolRule { get; init; } = "";
    public long? GasDelta { get; init; }
}

public static class InspectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
