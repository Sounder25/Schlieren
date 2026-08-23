using System.Numerics;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Journal;

public static class JournalSecurityAnalyzer
{
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

        foreach (var frame in analysis.Frames.Values.OrderBy(frame => frame.Depth).ThenBy(frame => frame.Id))
        {
            if (!effectsByFrame.TryGetValue(frame.Id, out var frameEffects))
                frameEffects = Array.Empty<AnalyzedStateEffect>();

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
        if (frame.CallType is not (CallType.Call or CallType.CallCode))
            return;

        var matchingAncestor = frame.AncestorIds
            .Select(id => analysis.Frames[id])
            .LastOrDefault(ancestor => ancestor.ContractAddress.Equals(frame.ContractAddress));
        if (matchingAncestor is null)
            return;

        var primary = frameEffects.FirstOrDefault(effect => effect.Effect is StorageWriteEvent);
        if (primary is null)
            return;

        findings.Add(CreateFinding(
            "SEC.REENTRANCY.REENTRY",
            SecurityCategory.Reentrancy,
            SecuritySeverity.Medium,
            frame,
            primary,
            [matchingAncestor.ContractAddress, frame.ContractAddress],
            primary.Effect is StorageWriteEvent write ? [write.Slot] : [],
            $"Frame {frame.Id} re-entered storage context {frame.ContractAddress} already active in ancestor frame {matchingAncestor.Id}."));

        if (!effectsByFrame.TryGetValue(matchingAncestor.Id, out var ancestorEffects))
            return;
        var postWrite = ancestorEffects.FirstOrDefault(effect =>
            effect.Effect is StorageWriteEvent && effect.Effect.Sequence > primary.Effect.Sequence);
        if (postWrite is not null)
        {
            findings.Add(CreateFinding(
                "SEC.REENTRANCY.POST_WRITE",
                SecurityCategory.Reentrancy,
                SecuritySeverity.Critical,
                matchingAncestor,
                postWrite,
                [matchingAncestor.ContractAddress, frame.ContractAddress],
                postWrite.Effect is StorageWriteEvent postStorage ? [postStorage.Slot] : [],
                $"Ancestor frame {matchingAncestor.Id} wrote storage after re-entrant frame {frame.Id}."));
        }
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
            findings.Add(CreateFinding(
                "SEC.STORAGE.DELEGATE_COLLISION",
                SecurityCategory.StorageCollision,
                SecuritySeverity.Critical,
                frame,
                effect,
                [frame.ContractAddress, codeAddress],
                [write.Slot],
                $"Code at {codeAddress} wrote reserved slot 0x{write.Slot:x} in storage owned by {frame.ContractAddress}."));
        }
    }

    private static SecurityFinding CreateFinding(
        string ruleId,
        SecurityCategory category,
        SecuritySeverity survivedSeverity,
        JournalFrameAnalysis frame,
        AnalyzedStateEffect evidence,
        IReadOnlyList<Address> addresses,
        IReadOnlyList<BigInteger> slots,
        string summary)
    {
        var severity = evidence.ExecutionDisposition == ExecutionDisposition.Reverted
            ? SecuritySeverity.Info
            : survivedSeverity;
        return new SecurityFinding(
            $"{ruleId}:{evidence.Effect.Sequence}",
            ruleId,
            category,
            severity,
            DiagnosisGrade.Proven,
            frame.Id,
            evidence.Effect.InstructionId,
            [evidence.Effect.Sequence],
            frame.AncestorIds,
            evidence.ExecutionDisposition,
            evidence.PersistenceDisposition,
            addresses,
            slots,
            summary,
            "This finding proves the pattern on the observed execution path; it does not prove exploitability for every input or environment.");
    }

    private static BigInteger Eip1967Slot(string label) =>
        new BigInteger(
            CryptoUtils.Keccak256(System.Text.Encoding.ASCII.GetBytes(label)),
            isUnsigned: true,
            isBigEndian: true) - 1;
}
