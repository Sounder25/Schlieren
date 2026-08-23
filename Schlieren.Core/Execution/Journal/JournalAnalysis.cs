using System.Collections.ObjectModel;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Journal;

public sealed record AnalyzedStateEffect(
    StateEffectEvent Effect,
    ExecutionDisposition ExecutionDisposition,
    PersistenceDisposition PersistenceDisposition,
    long? RevertedByFrameId);

public sealed record JournalFrameAnalysis(
    long Id,
    long? ParentId,
    int Depth,
    CallType CallType,
    Address ContractAddress,
    Address? CodeAddress,
    FrameStateResolution Resolution,
    IReadOnlyList<long> AncestorIds);

public sealed class JournalAnalysis
{
    private JournalAnalysis(
        IReadOnlyDictionary<long, JournalFrameAnalysis> frames,
        IReadOnlyList<AnalyzedStateEffect> stateEffects)
    {
        Frames = frames;
        StateEffects = stateEffects;
    }

    public IReadOnlyDictionary<long, JournalFrameAnalysis> Frames { get; }
    public IReadOnlyList<AnalyzedStateEffect> StateEffects { get; }

    public static JournalAnalysis Build(ExecutionJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var entered = new Dictionary<long, FrameEnteredEvent>();
        var checkpoints = new HashSet<long>();
        var resolutions = new Dictionary<long, FrameStateResolution>();
        var effects = new List<StateEffectEvent>();
        TransactionPersistenceOutcome? persistence = null;

        foreach (var entry in journal.Events)
        {
            switch (entry)
            {
                case FrameEnteredEvent frame:
                    var frameId = RequireFrameId(frame, "FrameEnteredWithoutId");
                    if (!entered.TryAdd(frameId, frame))
                        throw Error("DuplicateFrame", $"Frame {frameId} was entered more than once.");
                    break;
                case FrameStateCheckpointEvent checkpoint:
                    var checkpointId = RequireFrameId(checkpoint, "CheckpointWithoutFrame");
                    if (!checkpoints.Add(checkpointId))
                        throw Error("DuplicateCheckpoint", $"Frame {checkpointId} has multiple checkpoints.");
                    break;
                case FrameStateResolvedEvent resolved:
                    var resolvedId = RequireFrameId(resolved, "ResolutionWithoutFrame");
                    if (!resolutions.TryAdd(resolvedId, resolved.Resolution))
                        throw Error("DuplicateFrameResolution", $"Frame {resolvedId} has multiple resolutions.");
                    break;
                case TransactionPersistenceEvent transactionPersistence:
                    if (persistence.HasValue)
                        throw Error("MultipleTransactionPersistence", "The journal has multiple transaction persistence outcomes.");
                    persistence = transactionPersistence.Outcome;
                    break;
                case StateEffectEvent effect:
                    effects.Add(effect);
                    break;
            }
        }

        foreach (var effect in effects)
        {
            if (effect.Scope == StateEffectScope.Frame &&
                (!effect.FrameId.HasValue || !entered.ContainsKey(effect.FrameId.Value)))
                throw Error("UnknownEffectFrame", $"Effect {effect.EffectId} references an unknown frame.");
            if (effect.Scope == StateEffectScope.Transaction && effect.FrameId.HasValue)
                throw Error("TransactionEffectHasFrame", $"Transaction effect {effect.EffectId} must not reference a frame.");
        }

        foreach (var frameId in entered.Keys)
        {
            if (!checkpoints.Contains(frameId))
                throw Error("MissingFrameCheckpoint", $"Frame {frameId} has no checkpoint.");
            if (!resolutions.ContainsKey(frameId))
                throw Error("MissingFrameResolution", $"Frame {frameId} has no resolution.");
        }
        foreach (var frameId in checkpoints.Concat(resolutions.Keys))
            if (!entered.ContainsKey(frameId))
                throw Error("LifecycleWithoutFrame", $"Lifecycle evidence references unknown frame {frameId}.");

        if (!persistence.HasValue)
            throw Error("MissingTransactionPersistence", "The journal has no transaction persistence outcome.");

        var ancestorCache = new Dictionary<long, IReadOnlyList<long>>();
        IReadOnlyList<long> Ancestors(long frameId, HashSet<long>? visiting = null)
        {
            if (ancestorCache.TryGetValue(frameId, out var cached))
                return cached;
            visiting ??= new HashSet<long>();
            if (!visiting.Add(frameId))
                throw Error("FrameCycle", $"Frame ancestry contains a cycle at {frameId}.");

            var frame = entered[frameId];
            IReadOnlyList<long> result;
            if (frame.ParentFrameId is not { } parentId)
            {
                result = Array.Empty<long>();
            }
            else
            {
                if (!entered.TryGetValue(parentId, out var parent))
                    throw Error("UnknownParentFrame", $"Frame {frameId} references unknown parent {parentId}.");
                if (frame.Depth != parent.Depth + 1)
                    throw Error("InvalidFrameDepth", $"Frame {frameId} depth does not follow parent {parentId}.");
                result = Ancestors(parentId, visiting).Append(parentId).ToArray();
            }
            visiting.Remove(frameId);
            ancestorCache[frameId] = result;
            return result;
        }

        var frameModels = entered.ToDictionary(
            pair => pair.Key,
            pair => new JournalFrameAnalysis(
                pair.Key,
                pair.Value.ParentFrameId,
                pair.Value.Depth,
                pair.Value.CallType,
                pair.Value.ContractAddress,
                pair.Value.CodeAddress,
                resolutions[pair.Key],
                Ancestors(pair.Key)));

        var analyzed = effects.Select(effect =>
        {
            long? revertedBy = null;
            if (effect.FrameId is { } effectFrameId)
            {
                long? cursor = effectFrameId;
                while (cursor.HasValue)
                {
                    if (resolutions[cursor.Value] == FrameStateResolution.Rollback)
                    {
                        revertedBy = cursor;
                        break;
                    }
                    cursor = entered[cursor.Value].ParentFrameId;
                }
            }

            var execution = revertedBy.HasValue
                ? ExecutionDisposition.Reverted
                : ExecutionDisposition.Survived;
            var persistenceDisposition = execution == ExecutionDisposition.Reverted
                ? PersistenceDisposition.NotApplicable
                : persistence == TransactionPersistenceOutcome.CommittedToState
                    ? PersistenceDisposition.CommittedToState
                    : PersistenceDisposition.SimulationDiscarded;
            return new AnalyzedStateEffect(effect, execution, persistenceDisposition, revertedBy);
        }).ToArray();

        return new JournalAnalysis(
            new ReadOnlyDictionary<long, JournalFrameAnalysis>(frameModels),
            Array.AsReadOnly(analyzed));
    }

    private static long RequireFrameId(ExecutionJournalEvent entry, string code) =>
        entry.FrameId ?? throw Error(code, $"{entry.GetType().Name} requires a frame ID.");

    private static JournalAnalysisException Error(string code, string message) => new(code, message);
}
