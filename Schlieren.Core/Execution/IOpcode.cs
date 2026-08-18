namespace Schlieren.Core.Execution
{
    /// <summary>
    /// Base interface for EVM opcodes.
    /// ExecuteAsync is async to support forking IO during execution.
    /// </summary>
    public interface IOpcode
    {
        byte Code { get; }
        string Name { get; }
        ValueTask<(ExecutionResult Result, int NextPc)> ExecuteAsync(ExecutionContext context, CancellationToken ct = default);
    }
}