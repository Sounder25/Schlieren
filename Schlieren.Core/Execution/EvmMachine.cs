using System.Collections.ObjectModel;
using System.Numerics;
using Schlieren.Core.Execution.Journal;

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
                IReadOnlyList<BigInteger>? preStack = context.CaptureTrace || context.Journal is not null
                    ? context.Stack.SnapshotTopFirst()
                    : null;

                if (!_opcodes.TryGetValue(opcodeByte, out var opcode))
                {
                    context.AddTraceStep(pc, $"0x{opcodeByte:X2}", gasBefore, 0);
                    RecordExceptionalBurn(
                        context,
                        pc,
                        $"0x{opcodeByte:X2}",
                        gasBefore,
                        EvmError.InvalidOpcode);
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
                    context.SetActiveOpcode(pc, opcodeByte, opcode.Name);
                    (ExecutionResult execResult, int nextPc) execution;
                    try
                    {
                        execution = await opcode.ExecuteAsync(context, ct);
                    }
                    finally
                    {
                        context.ClearActiveOpcode();
                    }
                    var (execResult, nextPc) = execution;

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
                    RecordOpcodeGas(
                        context,
                        pc,
                        opcodeByte,
                        opcode.Name,
                        gasBefore,
                        gasAfter,
                        actualGasUsed,
                        IsCallLikeOpcode(opcode.Name)
                            ? GasSemantics.InclusiveFrameDelta
                            : GasSemantics.ExclusiveCharge,
                        preStack);
                    // Record into gas frame journal (for gas causality tree)
                    if (context.GasFrame != null && execResult.GasUsed > 0)
                        context.GasFrame.OpcodeSteps.Add((opcode.Name, execResult.GasUsed));

                    // If the opcode execution itself failed, propagate the failure
                    if (!execResult.IsSuccess)
                    {
                        if (execResult.Error != EvmError.Revert && gasAfter > 0)
                        {
                            RecordExceptionalBurn(
                                context,
                                pc,
                                opcode.Name,
                                gasAfter,
                                execResult.Error);
                        }
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
                    RecordOpcodeGas(
                        context,
                        pc,
                        opcodeByte,
                        opcode.Name,
                        gasBefore,
                        0,
                        0,
                        GasSemantics.Observation,
                        preStack);
                    RecordExceptionalBurn(
                        context,
                        pc,
                        opcode.Name,
                        gasBefore,
                        EvmError.OutOfGas);
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

        private static bool IsCallLikeOpcode(string name) =>
            name is "CALL" or "CALLCODE" or "DELEGATECALL" or "STATICCALL" or "CREATE" or "CREATE2";

        private static void RecordOpcodeGas(
            ExecutionContext context,
            int pc,
            byte opcode,
            string name,
            ulong gasBefore,
            ulong gasAfter,
            ulong amount,
            GasSemantics semantics,
            IReadOnlyList<BigInteger>? preStack = null)
        {
            if (context.Journal is not { } journal)
                return;

            var state = context.CaptureJournalMachineState(preStack, name);

            journal.Record(new OpcodeGasEvent
            {
                FrameId = context.JournalFrameId,
                ParentFrameId = context.JournalParentFrameId,
                Pc = pc,
                Opcode = opcode,
                Name = name,
                GasBefore = gasBefore,
                GasAfter = gasAfter,
                Amount = amount,
                Semantics = semantics,
                Depth = context.CallDepth,
                CallType = state.CallType,
                ContractAddress = state.ContractAddress,
                CallerAddress = state.CallerAddress,
                CodeAddress = state.CodeAddress,
                Stack = state.Stack,
                Memory = state.Memory,
                Storage = state.Storage,
                Output = state.Output
            });
        }

        private static void RecordExceptionalBurn(
            ExecutionContext context,
            int pc,
            string opcode,
            ulong amount,
            EvmError error)
        {
            if (context.Journal is not { } journal)
                return;

            journal.Record(new ExceptionalGasBurnedEvent
            {
                FrameId = context.JournalFrameId,
                ParentFrameId = context.JournalParentFrameId,
                Pc = pc,
                Opcode = opcode,
                Amount = amount,
                Error = error
            });
        }
    }
}
