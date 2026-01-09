using Scrutor.Core.Models;

namespace Scrutor.Core.Execution;

public enum EvmError
{
    None,
    StackUnderflow,
    StackOverflow,
    OutOfGas,
    InvalidOpcode,
    BadJumpDestination,
    Revert,
    InvalidMemoryAccess,
    NonceTooLow,
    NonceTooHigh,
    InsufficientFunds,
    InternalError
}

public readonly record struct ExecutionResult
{
    public bool IsSuccess { get; init; }
    public EvmError Error { get; init; }
    public ulong GasUsed { get; init; }
    public byte[] ReturnData { get; init; }
    public List<TransactionLog> Logs { get; init; }

    private ExecutionResult(bool success, EvmError error, ulong gasUsed, byte[] returnData, List<TransactionLog>? logs = null)
    {
        IsSuccess = success;
        Error = error;
        GasUsed = gasUsed;
        ReturnData = returnData ?? Array.Empty<byte>();
        Logs = logs ?? new List<TransactionLog>();
    }

    public static ExecutionResult Success(ulong gasUsed, byte[]? returnData = null, List<TransactionLog>? logs = null) =>
        new(true, EvmError.None, gasUsed, returnData ?? Array.Empty<byte>(), logs);

    public static ExecutionResult Failure(EvmError error, ulong gasUsed = 0, byte[]? returnData = null) =>
        new(false, error, gasUsed, returnData ?? Array.Empty<byte>());
}