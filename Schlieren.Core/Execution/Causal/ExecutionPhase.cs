namespace Schlieren.Core.Execution.Causal;

/// <summary>
/// Earlier phases outrank later ones. The first phase that differs is the root cause.
/// </summary>
public enum ExecutionPhase
{
    TransactionValidation = 1,
    IntrinsicGas = 2,
    OpcodeActivation = 3,
    OpcodeResult = 4,
    GasCharge = 5,
    FrameAllocation = 6,
    StateMutation = 7,
    CommitRevert = 8,
    Refund = 9,
    Settlement = 10,
    FinalState = 11
}
