using System.Collections.ObjectModel;

namespace Schlieren.Core.Execution
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
            // [AI-EDIT 2026-01-10] Track return data from RETURN/REVERT opcodes.
            // An opcode that terminates execution (RETURN, REVERT, STOP) sets PC past
            // the code boundary and may carry data in execResult.ReturnData.
            // We capture it here so the final result preserves it (critical for CREATE
            // init-code: the deployed bytecode lives in RETURN's data payload).
            byte[]? lastReturnData = null;

            while (context.ProgramCounter < context.Code.Length)
            {
                ct.ThrowIfCancellationRequested();

                var pc = context.ProgramCounter;
                var opcodeByte = context.Code[pc];
                var gasBefore = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;

                if (!_opcodes.TryGetValue(opcodeByte, out var opcode))
                {
                    context.AddTraceStep(pc, $"0x{opcodeByte:X2}", gasBefore, 0);
                    return ExecutionResult.Failure(
                        EvmError.InvalidOpcode,
                        context.GasLimit) with
                    {
                        TraceSteps = context.TraceSteps
                    };
                }

                try
                {
                    // Capture stack snapshot BEFORE opcode executes (EELS OpStart semantics:
                    //   evm_trace(evm, OpStart(op))  ← state before
                    //   op_implementation[op](evm)
                    // This makes structLogs show the stack the opcode *received*, not what it left.)
                    var preStack = context.CaptureTrace ? context.Stack.SnapshotTopFirst() : null;

                    var (execResult, nextPc) = await opcode.ExecuteAsync(context, ct);

                    // Pattern B opcodes charge here.
                    // Pattern A returns 0 because it already charged internally.
                    context.ConsumeGas(execResult.GasUsed);

                    // Compute observed gas delta for trace export. Some opcodes self-consume
                    // (MSTORE, SLOAD, CALL) and return GasUsed=0; others return their cost
                    // and rely on the line above to charge it. The trace must reflect the actual
                    // delta regardless of which pattern the opcode uses.
                    var gasAfter = context.GasLimit > context.GasUsed ? context.GasLimit - context.GasUsed : 0UL;
                    var actualGasUsed = gasBefore - gasAfter;

                    context.AddTraceStep(pc, opcode.Name, gasBefore, actualGasUsed, preStack);
                    // Record into gas frame journal (for gas causality tree)
                    if (context.GasFrame != null && execResult.GasUsed > 0)
                        context.GasFrame.OpcodeSteps.Add((opcode.Name, execResult.GasUsed));

                    // If the opcode execution itself failed, propagate the failure
                    if (!execResult.IsSuccess)
                    {
                        var failureGasUsed = execResult.Error == EvmError.Revert
                            ? context.GasUsed
                            : context.GasLimit;
                        return execResult with
                        {
                            GasUsed = failureGasUsed,
                            TraceSteps = context.TraceSteps
                        };
                    }

                    // Capture any return data (RETURN / REVERT opcodes carry deployed code or revert reason)
                    if (execResult.ReturnData.Length > 0)
                        lastReturnData = execResult.ReturnData;

                    // Advance PC to the next instruction
                    context.ProgramCounter = nextPc;
                }
                catch (EvmOutOfGasException)
                {
                    context.AddTraceStep(pc, opcode.Name, gasBefore, gasBefore);
                    return ExecutionResult.Failure(
                        EvmError.OutOfGas,
                        context.GasLimit) with
                    {
                        TraceSteps = context.TraceSteps
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[INTERNAL_ERROR] " +
                        $"depth={context.CallDepth} " +
                        $"pc={pc} " +
                        $"opcode={opcode.Name} (0x{opcodeByte:X2}) " +
                        $"gasUsed={context.GasUsed} " +
                        $"gasLimit={context.GasLimit} " +
                        $"contract={context.ContractAddress} " +
                        $"caller={context.Caller} " +
                        $"stack=[{string.Join(",", context.Stack.SnapshotTopFirst().Select(value => $"0x{value:x}"))}] " +
                        $"type={ex.GetType().FullName} " +
                        $"message={ex.Message}\n" +
                        $"stackTrace={ex.StackTrace}"
                    );
                    throw;
                }
            }

            // Successfully executed to the end of the code — preserve any RETURN data and gas refund counter
            return ExecutionResult.Success(context.GasUsed, returnData: lastReturnData, logs: context.Logs, traceSteps: context.TraceSteps) with { GasRefundCounter = context.GasRefundCounter };
        }
    }
}
