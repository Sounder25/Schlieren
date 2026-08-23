namespace Schlieren.Core.Execution.Journal;

public static class JournalTraceAssembler
{
    public static JournalTraceDto FromCanonical(
        string fork,
        ExecutionResult result,
        JournalTraceOptions? options = null)
    {
        options ??= new JournalTraceOptions();
        var journal = result.Journal
            ?? throw new ArgumentException("Execution result does not contain a journal.", nameof(result));
        var tree = JournalGasTree.Build(journal, result);
        var exits = journal.Events.OfType<FrameExitedEvent>()
            .Where(entry => entry.FrameId.HasValue)
            .GroupBy(entry => entry.FrameId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());
        var frames = journal.Events.OfType<FrameEnteredEvent>()
            .Where(entry => entry.FrameId.HasValue)
            .Select(entry =>
            {
                exits.TryGetValue(entry.FrameId!.Value, out var exit);
                return new JournalFrameDto(
                    entry.FrameId.Value,
                    entry.ParentFrameId,
                    entry.Depth,
                    entry.CallType.ToString(),
                    entry.ContractAddress.ToString(),
                    entry.CodeAddress?.ToString(),
                    entry.GasLimit,
                    exit?.Success,
                    exit?.Error.ToString(),
                    exit?.GasUsed,
                    exit?.GasRemaining);
            })
            .ToArray();
        var steps = journal.Events.OfType<OpcodeGasEvent>()
            .Where(entry => entry.FrameId.HasValue)
            .Select(entry => new JournalStepDto
            {
                Sequence = entry.Sequence,
                FrameId = entry.FrameId!.Value,
                ParentFrameId = entry.ParentFrameId,
                Depth = entry.Depth,
                Pc = entry.Pc,
                Opcode = $"0x{entry.Opcode:x2}",
                Op = entry.Name,
                GasBefore = entry.GasBefore,
                GasAfter = entry.GasAfter,
                GasCost = entry.Amount,
                Semantics = Name(entry.Semantics),
                CallType = entry.CallType?.ToString(),
                ContractAddress = entry.ContractAddress,
                CallerAddress = entry.CallerAddress,
                CodeAddress = entry.CodeAddress,
                Output = Hex(entry.Output),
                Stack = options.DisableStack ? null : entry.Stack.ToArray(),
                Memory = options.DisableMemory ? null : entry.Memory.ToArray(),
                Storage = options.DisableStorage
                    ? null
                    : new Dictionary<string, string>(entry.Storage, StringComparer.OrdinalIgnoreCase)
            })
            .ToArray();

        return new JournalTraceDto(
            result.IsSuccess,
            fork,
            new JournalExecutionDto(
                result.IsSuccess,
                result.IsSuccess ? null : result.Error.ToString(),
                tree.Conservation.SettledGas,
                result.GasRefundCounter,
                Hex(result.ReturnData)),
            journal.Events.Select(MapEvent).ToArray(),
            frames,
            steps,
            MapTree(tree.Root),
            tree.Conservation);
    }

    private static JournalEventDto MapEvent(ExecutionJournalEvent entry)
    {
        var (kind, semantics, amount, component, pc, opcode, opcodeName, data) = entry switch
        {
            TransactionStartedEvent e =>
                ("transactionStarted", "observation", (ulong?)null, null, (int?)null, null, null,
                    (object)new { e.GasLimit, e.IsInternal }),
            IntrinsicGasChargedEvent e =>
                ("intrinsicGasCharged", Name(e.Semantics), (ulong?)e.Amount, null, null, null, null,
                    (object)new { }),
            FrameEnteredEvent e =>
                ("frameEntered", Name(e.Semantics), (ulong?)e.GasLimit, null, null, null, null,
                    (object)new { e.Depth, CallType = e.CallType.ToString(), ContractAddress = e.ContractAddress.ToString(), CodeAddress = e.CodeAddress?.ToString() }),
            OpcodeGasEvent e =>
                ("opcodeGas", Name(e.Semantics), (ulong?)e.Amount, null, (int?)e.Pc,
                    $"0x{e.Opcode:x2}", e.Name, (object)new { e.GasBefore, e.GasAfter, e.Depth }),
            GasComponentEvent e =>
                ("gasComponent", Name(e.Semantics), (ulong?)e.Amount, e.Component, e.Pc,
                    e.Opcode is byte op ? $"0x{op:x2}" : null, e.OpcodeName,
                    (object)new { Scope = Name(e.Scope) }),
            ExceptionalGasBurnedEvent e =>
                ("exceptionalGasBurned", Name(e.Semantics), (ulong?)e.Amount, null, (int?)e.Pc,
                    null, e.Opcode, (object)new { Error = e.Error.ToString() }),
            RefundCounterChangedEvent e =>
                ("refundCounterChanged", Name(e.Semantics), (ulong?)null, null, null, null, null,
                    (object)new { e.Previous, e.Current, e.Delta }),
            FrameExitedEvent e =>
                ("frameExited", Name(e.Semantics), (ulong?)e.GasUsed, null, null, null, null,
                    (object)new { e.Depth, e.Success, Error = e.Error.ToString(), e.GasRemaining }),
            EffectiveGasRefundedEvent e =>
                ("effectiveGasRefunded", Name(e.Semantics), (ulong?)e.Amount, null, null, null, null,
                    (object)new { e.GrossGasUsed, e.RefundCap }),
            TransactionSettledEvent e =>
                ("transactionSettled", "observation", (ulong?)e.ChargedGas, null, null, null, null,
                    (object)new { e.UnusedGasReturned }),
            _ => throw new InvalidOperationException($"Unsupported journal event {entry.GetType().Name}.")
        };
        return new JournalEventDto(
            kind,
            entry.Sequence,
            entry.FrameId,
            entry.ParentFrameId,
            semantics,
            amount,
            component,
            pc,
            opcode,
            opcodeName,
            data);
    }

    private static JournalGasNodeDto MapTree(JournalGasNode node) => new(
        node.Id,
        node.Label,
        node.FrameId,
        Name(node.Semantics),
        node.Amount,
        Name(node.Effect),
        node.TotalGas,
        node.EventSequences,
        node.Children.Select(MapTree).ToArray());

    private static string Hex(IEnumerable<byte> bytes) =>
        "0x" + Convert.ToHexString(bytes.ToArray()).ToLowerInvariant();

    private static string Name<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
