using System.Numerics;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Journal;

public static class JournalSecurityAnalyzer
{
    private const string ObservedPathLimitation =
        "This finding proves the pattern on the observed execution path; it does not prove exploitability for every input or environment.";

    private static readonly HashSet<BigInteger> ReservedProxySlots =
    [
        BigInteger.Zero,
        Eip1967Slot("eip1967.proxy.implementation"),
        Eip1967Slot("eip1967.proxy.admin"),
        Eip1967Slot("eip1967.proxy.beacon")
    ];

    public static IReadOnlyList<SecurityFinding> Analyze(JournalAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var findings = new List<SecurityFinding>();
        var effectsByFrame = analysis.StateEffects
            .Where(effect => effect.Effect.FrameId.HasValue)
            .GroupBy(effect => effect.Effect.FrameId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(effect => effect.Effect.Sequence).ToArray());

        foreach (var frame in analysis.Frames.Values.OrderBy(frame => frame.EntrySequence))
        {
            if (!effectsByFrame.TryGetValue(frame.Id, out var frameEffects))
                frameEffects = [];

            AnalyzeReentry(analysis, frame, frameEffects, effectsByFrame, findings);
            AnalyzeStorageCollision(frame, frameEffects, findings);
        }

        return findings.AsReadOnly();
    }

    private static void AnalyzeReentry(
        JournalAnalysis analysis,
        JournalFrameAnalysis frame,
        IReadOnlyList<AnalyzedStateEffect> frameEffects,
        IReadOnlyDictionary<long, AnalyzedStateEffect[]> effectsByFrame,
        ICollection<SecurityFinding> findings)
    {
        if (frame.CallType is not (CallType.Call or CallType.StaticCall))
            return;

        var matchingAncestor = frame.AncestorIds
            .Reverse()
            .Select(id => analysis.Frames[id])
            .FirstOrDefault(ancestor => ancestor.ContractAddress.Equals(frame.ContractAddress));
        if (matchingAncestor is null)
            return;

        var contacts = frameEffects
            .Where(effect => effect.Effect switch
            {
                StorageReadEvent read => read.StorageAddress.Equals(frame.ContractAddress),
                StorageWriteEvent write => write.StorageAddress.Equals(frame.ContractAddress),
                _ => false
            })
            .ToArray();
        var baseRule = contacts.Length == 0
            ? "SEC.REENTRANCY.OBSERVED"
            : "SEC.REENTRANCY.STATE_CONTACT";
        var baseSeverity = frame.ExecutionDisposition == ExecutionDisposition.Reverted ||
                           frame.CallType == CallType.StaticCall ||
                           contacts.Length == 0
            ? SecuritySeverity.Info
            : SecuritySeverity.Medium;
        var baseEvidence = new[] { frame.EntrySequence }
            .Concat(contacts.Select(contact => contact.Effect.Sequence));
        var contactSlots = contacts.SelectMany(contact => contact.Effect switch
        {
            StorageReadEvent read => new[] { read.Slot },
            StorageWriteEvent write => new[] { write.Slot },
            _ => Array.Empty<BigInteger>()
        });

        findings.Add(CreateFinding(
            $"{baseRule}:frame-{frame.Id}:event-{frame.EntrySequence}",
            baseRule,
            SecurityCategory.Reentrancy,
            baseSeverity,
            frame,
            contacts.FirstOrDefault()?.Effect.InstructionId,
            baseEvidence,
            frame.ExecutionDisposition,
            frame.PersistenceDisposition,
            [matchingAncestor.ContractAddress, frame.ContractAddress],
            contactSlots,
            $"Frame {frame.Id} re-entered storage context {frame.ContractAddress} already active in ancestor frame {matchingAncestor.Id}."));

        if (!effectsByFrame.TryGetValue(matchingAncestor.Id, out var ancestorEffects))
            return;
        var postWrites = ancestorEffects
            .Where(effect => effect.Effect is StorageWriteEvent write &&
                             write.StorageAddress.Equals(matchingAncestor.ContractAddress) &&
                             effect.Effect.Sequence > frame.ResolutionSequence)
            .ToArray();
        if (postWrites.Length == 0)
            return;

        var firstPostWrite = postWrites[0];
        var postSeverity = frame.ExecutionDisposition == ExecutionDisposition.Survived &&
                           postWrites.All(write => write.ExecutionDisposition == ExecutionDisposition.Survived)
            ? SecuritySeverity.Critical
            : SecuritySeverity.Info;
        findings.Add(CreateFinding(
            $"SEC.REENTRANCY.POST_WRITE:frame-{frame.Id}:event-{firstPostWrite.Effect.Sequence}",
            "SEC.REENTRANCY.POST_WRITE",
            SecurityCategory.Reentrancy,
            postSeverity,
            matchingAncestor,
            firstPostWrite.Effect.InstructionId,
            new[] { frame.EntrySequence, frame.ResolutionSequence }
                .Concat(postWrites.Select(write => write.Effect.Sequence)),
            frame.ExecutionDisposition,
            frame.PersistenceDisposition,
            [matchingAncestor.ContractAddress, frame.ContractAddress],
            postWrites.Select(write => ((StorageWriteEvent)write.Effect).Slot),
            $"Ancestor frame {matchingAncestor.Id} wrote storage after re-entrant frame {frame.Id} resolved."));
    }

    private static void AnalyzeStorageCollision(
        JournalFrameAnalysis frame,
        IReadOnlyList<AnalyzedStateEffect> frameEffects,
        ICollection<SecurityFinding> findings)
    {
        if (frame.CallType is not (CallType.DelegateCall or CallType.CallCode) ||
            frame.CodeAddress is not { } codeAddress ||
            codeAddress.Equals(frame.ContractAddress))
            return;

        foreach (var effect in frameEffects.Where(effect =>
                     effect.Effect is StorageWriteEvent write && ReservedProxySlots.Contains(write.Slot)))
        {
            var write = (StorageWriteEvent)effect.Effect;
            var severity = effect.ExecutionDisposition == ExecutionDisposition.Reverted
                ? SecuritySeverity.Info
                : SecuritySeverity.Critical;
            findings.Add(CreateFinding(
                $"SEC.STORAGE.DELEGATE_COLLISION:{effect.Effect.Sequence}",
                "SEC.STORAGE.DELEGATE_COLLISION",
                SecurityCategory.StorageCollision,
                severity,
                frame,
                effect.Effect.InstructionId,
                [effect.Effect.Sequence],
                effect.ExecutionDisposition,
                effect.PersistenceDisposition,
                [frame.ContractAddress, codeAddress],
                [write.Slot],
                $"Code at {codeAddress} wrote reserved slot 0x{write.Slot:x} in storage owned by {frame.ContractAddress}."));
        }
    }

    private static SecurityFinding CreateFinding(
        string id,
        string ruleId,
        SecurityCategory category,
        SecuritySeverity severity,
        JournalFrameAnalysis primaryFrame,
        long? instructionId,
        IEnumerable<long> evidenceSequences,
        ExecutionDisposition executionDisposition,
        PersistenceDisposition persistenceDisposition,
        IEnumerable<Address> addresses,
        IEnumerable<BigInteger> slots,
        string summary) => new(
            id,
            ruleId,
            category,
            severity,
            DiagnosisGrade.Proven,
            primaryFrame.Id,
            instructionId,
            evidenceSequences.Distinct().Order().ToArray(),
            primaryFrame.AncestorIds,
            executionDisposition,
            persistenceDisposition,
            addresses.Distinct().OrderBy(address => address.ToString(), StringComparer.Ordinal).ToArray(),
            slots.Distinct().Order().ToArray(),
            summary,
            ObservedPathLimitation);

    private static BigInteger Eip1967Slot(string label) =>
        new BigInteger(
            CryptoUtils.Keccak256(System.Text.Encoding.ASCII.GetBytes(label)),
            isUnsigned: true,
            isBigEndian: true) - 1;
}
