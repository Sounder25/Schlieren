using System.Collections.ObjectModel;

namespace Scrutor.Core.Execution
{
    /// <summary>
    /// The core EVM interpreter loop (the "machine").
    /// Steps through bytecode, dispatching opcodes and managing state.
    /// </summary>
    public sealed class EvmMachine
    {
        private readonly ReadOnlyDictionary<byte, IOpcode> _opcodes;

        public EvmMachine(IEnumerable<IOpcode> opcodes)
        {
            _opcodes = new ReadOnlyDictionary<byte, IOpcode>(
                opcodes.ToDictionary(op => op.Code, op => op)
            );
        }

        /// <summary>
        /// Executes EVM bytecode within a given context until execution halts.
        /// </summary>
        /// <param name="context">The execution context for this run.</param>
        /// <returns>An ExecutionResult summarizing the outcome.</returns>
        public async Task<ExecutionResult> ExecuteAsync(ExecutionContext context, CancellationToken ct = default)
        {
            while (context.ProgramCounter < context.Code.Length)
            {
                ct.ThrowIfCancellationRequested();

                var pc = context.ProgramCounter;
                var opcodeByte = context.Code[pc];

                if (!_opcodes.TryGetValue(opcodeByte, out var opcode))
                {
                    return ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasUsed);
                }

                try
                {
                    var (execResult, nextPc) = await opcode.ExecuteAsync(context, ct);
                    
                    // Consume gas
                    context.ConsumeGas(execResult.GasUsed);

                    // If the opcode execution itself failed, propagate the failure
                    if (!execResult.IsSuccess)
                    {
                        return execResult with { GasUsed = context.GasUsed };
                    }
                    
                    // Advance PC to the next instruction
                    context.ProgramCounter = nextPc;
                }
                catch (EvmOutOfGasException)
                {
                    return ExecutionResult.Failure(EvmError.OutOfGas, context.GasUsed);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Catch-all for other potential issues during opcode execution
                    return ExecutionResult.Failure(EvmError.InvalidOpcode, context.GasUsed);
                }
            }

            // Successfully executed to the end of the code
            return ExecutionResult.Success(context.GasUsed, logs: context.Logs);
        }
    }
}
