namespace Schlieren.Core.Execution.Causal;

/// <summary>Cluster key from first causal divergence — not final mismatch type.</summary>
public static class CausalFingerprint
{
    public static string Build(string fork, ExecutionPhase phase, string ruleId)
        => $"{phase.ToLabel()} / {ruleId} / {fork}";

    public static string ToLabel(this ExecutionPhase phase) => phase switch
    {
        ExecutionPhase.TransactionValidation => "VALIDATION",
        ExecutionPhase.IntrinsicGas => "INTRINSIC",
        ExecutionPhase.OpcodeActivation => "OPCODE_GATE",
        ExecutionPhase.OpcodeResult => "OPCODE",
        ExecutionPhase.GasCharge => "GAS",
        ExecutionPhase.FrameAllocation => "FRAME",
        ExecutionPhase.StateMutation => "STATE",
        ExecutionPhase.CommitRevert => "COMMIT",
        ExecutionPhase.Refund => "REFUND",
        ExecutionPhase.Settlement => "SETTLE",
        _ => "FINAL"
    };
}
