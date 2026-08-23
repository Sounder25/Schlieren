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
        var analysis = JournalAnalysis.Build(journal);
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
        var frameDtos = frames.ToDictionary(frame => frame.Id);
        var stateEffects = analysis.StateEffects.Select(MapStateEffect).ToArray();
        var securityFindings = Array.Empty<JournalSecurityFindingDto>();
        var frameEntries = journal.Events.OfType<FrameEnteredEvent>()
            .Where(entry => entry.FrameId.HasValue)
            .ToDictionary(entry => entry.FrameId!.Value);

        JournalFrameTreeNodeDto BuildFrameTree(long frameId)
        {
            var frame = analysis.Frames[frameId];
            var children = analysis.Frames.Values
                .Where(candidate => candidate.ParentId == frameId)
                .OrderBy(candidate => frameEntries[candidate.Id].Sequence)
                .Select(candidate => BuildFrameTree(candidate.Id))
                .ToArray();
            return new JournalFrameTreeNodeDto(
                frameDtos[frameId],
                frame.AncestorIds,
                stateEffects.Where(effect => effect.FrameId == frameId)
                    .Select(effect => effect.EffectId).ToArray(),
                securityFindings.Where(finding => finding.PrimaryFrameId == frameId)
                    .Select(finding => finding.Id).ToArray(),
                children);
        }

        var roots = analysis.Frames.Values
            .Where(frame => frame.ParentId is null)
            .OrderBy(frame => frameEntries[frame.Id].Sequence)
            .ToArray();
        if (roots.Length > 1)
            throw new JournalAnalysisException("MultipleRootFrames", "A journal trace must have at most one root frame.");
        var frameTree = roots.Length == 0 ? null : BuildFrameTree(roots[0].Id);

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
            tree.Conservation,
            stateEffects,
            securityFindings,
            frameTree);
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
            FrameStateCheckpointEvent =>
                ("frameStateCheckpoint", "observation", (ulong?)null, null, null, null, null, (object)new { }),
            FrameStateResolvedEvent e =>
                ("frameStateResolved", "observation", (ulong?)null, null, null, null, null,
                    (object)new { Resolution = Name(e.Resolution) }),
            TransactionPersistenceEvent e =>
                ("transactionPersistence", "observation", (ulong?)null, null, null, null, null,
                    (object)new { Outcome = Name(e.Outcome) }),
            StateEffectEvent e =>
                (StateEffectKind(e), "observation", (ulong?)null, null, e.Pc,
                    e.Opcode is byte op ? $"0x{op:x2}" : null, null, MapStateEffectData(e)),
            _ => throw new InvalidOperationException($"Unsupported journal event {entry.GetType().Name}.")
        };
        return new JournalEventDto(
            kind,
            entry.Sequence,
            entry.InstructionId,
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

    private static JournalStateEffectDto MapStateEffect(AnalyzedStateEffect analyzed) => new(
        analyzed.Effect.EffectId,
        analyzed.Effect.Sequence,
        analyzed.Effect.FrameId,
        analyzed.Effect.ParentFrameId,
        analyzed.Effect.InstructionId,
        StateEffectKind(analyzed.Effect),
        analyzed.Effect.Pc,
        analyzed.Effect.Opcode is byte opcode ? $"0x{opcode:x2}" : null,
        Name(analyzed.ExecutionDisposition),
        Name(analyzed.PersistenceDisposition),
        analyzed.RevertedByFrameId,
        MapStateEffectData(analyzed.Effect));

    private static string StateEffectKind(StateEffectEvent effect) => effect switch
    {
        StorageReadEvent => "storageRead",
        StorageWriteEvent => "storageWrite",
        TransientStorageReadEvent => "transientStorageRead",
        TransientStorageWriteEvent => "transientStorageWrite",
        _ => throw new InvalidOperationException($"Unsupported state effect {effect.GetType().Name}.")
    };

    private static object MapStateEffectData(StateEffectEvent effect) => effect switch
    {
        StorageReadEvent e => new
        {
            StorageAddress = e.StorageAddress.ToString(),
            Slot = Hex(e.Slot),
            Value = Hex(e.Value),
            e.IsWarm
        },
        StorageWriteEvent e => new
        {
            StorageAddress = e.StorageAddress.ToString(),
            Slot = Hex(e.Slot),
            OriginalValue = Hex(e.OriginalValue),
            PreviousValue = Hex(e.PreviousValue),
            Value = Hex(e.Value),
            e.IsWarm
        },
        TransientStorageReadEvent e => new
        {
            StorageAddress = e.StorageAddress.ToString(),
            Slot = Hex(e.Slot),
            Value = Hex(e.Value)
        },
        TransientStorageWriteEvent e => new
        {
            StorageAddress = e.StorageAddress.ToString(),
            Slot = Hex(e.Slot),
            PreviousValue = Hex(e.PreviousValue),
            Value = Hex(e.Value)
        },
        _ => throw new InvalidOperationException($"Unsupported state effect {effect.GetType().Name}.")
    };

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

    private static string Hex(System.Numerics.BigInteger value) =>
        "0x" + value.ToString("x");

    private static string Name<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
