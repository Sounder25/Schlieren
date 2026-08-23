using System.Numerics;
using System.Text.Json;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Security;

public sealed class JournalSecurityAnalyzerTests
{
    private static readonly Address Target = Address.FromHex("0x9100000000000000000000000000000000000001");
    private static readonly Address Other = Address.FromHex("0x9200000000000000000000000000000000000002");

    [Fact]
    public void SameOwnerCallWithoutStateContact_IsObservedInformational()
    {
        var journal = BuildReentryJournal(CallType.Call, null, [], FrameStateResolution.Commit);
        var finding = Assert.Single(ReentrancyFindings(journal));
        Assert.Equal("SEC.REENTRANCY.OBSERVED", finding.RuleId);
        Assert.Equal(SecuritySeverity.Info, finding.Severity);
        Assert.Equal(2, finding.PrimaryFrameId);
        Assert.Null(finding.InstructionId);
    }

    [Fact]
    public void SameOwnerCallWithStorageRead_IsStateContactMedium()
    {
        var journal = BuildReentryJournal(CallType.Call, NewRead(2, 7), [], FrameStateResolution.Commit);
        var finding = Assert.Single(ReentrancyFindings(journal));
        Assert.Equal("SEC.REENTRANCY.STATE_CONTACT", finding.RuleId);
        Assert.Equal(SecuritySeverity.Medium, finding.Severity);
        Assert.Equal([new BigInteger(7)], finding.StorageSlots);
    }

    [Fact]
    public void StaticCallStateContact_RemainsInformational()
    {
        var journal = BuildReentryJournal(CallType.StaticCall, NewRead(2, 7), [], FrameStateResolution.Commit);
        var finding = Assert.Single(ReentrancyFindings(journal));
        Assert.Equal("SEC.REENTRANCY.STATE_CONTACT", finding.RuleId);
        Assert.Equal(SecuritySeverity.Info, finding.Severity);
    }

    [Theory]
    [InlineData(CallType.DelegateCall)]
    [InlineData(CallType.CallCode)]
    public void SharedStorageExecution_IsNotClassifiedAsReentrancy(CallType callType)
    {
        var journal = BuildReentryJournal(callType, NewWrite(2, 9), [], FrameStateResolution.Commit);
        Assert.Empty(ReentrancyFindings(journal));
    }

    [Fact]
    public void DifferentStorageOwner_IsNotClassifiedAsReentrancy()
    {
        var journal = BuildReentryJournal(CallType.Call, NewRead(2, 7, Other), [],
            FrameStateResolution.Commit, differentChildAddress: true);
        Assert.Empty(ReentrancyFindings(journal));
    }

    [Fact]
    public void AncestorWriteBeforeChildEntry_DoesNotProducePostWrite()
    {
        var journal = BuildReentryJournal(CallType.Call, NewRead(2, 0), [NewWrite(1, 1)],
            FrameStateResolution.Commit, postWritesBeforeChild: true);
        var findings = ReentrancyFindings(journal);
        Assert.Single(findings);
        Assert.DoesNotContain(findings, finding => finding.RuleId == "SEC.REENTRANCY.POST_WRITE");
    }

    [Fact]
    public void AncestorWritesAfterChildResolution_ProduceOneCriticalAggregatedPostWrite()
    {
        var journal = BuildReentryJournal(CallType.Call, NewRead(2, 0),
            [NewWrite(1, 2), NewWrite(1, 1)], FrameStateResolution.Commit);
        var findings = ReentrancyFindings(journal);
        var critical = Assert.Single(findings, finding => finding.RuleId == "SEC.REENTRANCY.POST_WRITE");
        Assert.Equal(SecuritySeverity.Critical, critical.Severity);
        Assert.Equal(1, critical.PrimaryFrameId);
        Assert.Equal([BigInteger.One, new BigInteger(2)], critical.StorageSlots);
        Assert.Equal(critical.SupportingEventSequences.Order(), critical.SupportingEventSequences);
    }

    [Fact]
    public void RevertedReentryAndPostWrite_AreInformational()
    {
        var journal = BuildReentryJournal(CallType.Call, NewWrite(2, 0), [NewWrite(1, 1)],
            FrameStateResolution.Rollback);
        var findings = ReentrancyFindings(journal);
        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(SecuritySeverity.Info, finding.Severity));
        Assert.All(findings, finding => Assert.Equal(ExecutionDisposition.Reverted, finding.ExecutionDisposition));
    }

    [Fact]
    public void SimulationDiscarded_PreservesObservedSeverityAndDisposition()
    {
        var journal = BuildReentryJournal(CallType.Call, NewRead(2, 0), [], FrameStateResolution.Commit,
            persistence: TransactionPersistenceOutcome.SimulationDiscarded);
        var finding = Assert.Single(ReentrancyFindings(journal));
        Assert.Equal(SecuritySeverity.Medium, finding.Severity);
        Assert.Equal(ExecutionDisposition.Survived, finding.ExecutionDisposition);
        Assert.Equal(PersistenceDisposition.SimulationDiscarded, finding.PersistenceDisposition);
    }

    [Fact]
    public void NestedSameOwnerFrames_SelectNearestMatchingAncestor()
    {
        var journal = new ExecutionJournal();
        Enter(journal, 1, null, 0, CallType.Root, Target);
        Enter(journal, 2, 1, 1, CallType.Call, Other);
        Enter(journal, 3, 2, 2, CallType.Call, Target);
        Enter(journal, 4, 3, 3, CallType.Call, Target);
        Resolve(journal, 4, 3, FrameStateResolution.Commit);
        Resolve(journal, 3, 2, FrameStateResolution.Commit);
        Resolve(journal, 2, 1, FrameStateResolution.Commit);
        Resolve(journal, 1, null, FrameStateResolution.Commit);
        journal.Record(new TransactionPersistenceEvent { Outcome = TransactionPersistenceOutcome.CommittedToState });
        var findings = ReentrancyFindings(journal);
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.PrimaryFrameId == 3 && finding.Summary.Contains("ancestor frame 1"));
        Assert.Contains(findings, finding => finding.PrimaryFrameId == 4 && finding.Summary.Contains("ancestor frame 3"));
    }

    [Fact]
    public void AnalysisIsDeterministicAcrossRepeatedRuns()
    {
        var journal = BuildReentryJournal(CallType.Call, NewRead(2, 0),
            [NewWrite(1, 2), NewWrite(1, 1)], FrameStateResolution.Commit);
        var first = ReentrancyFindings(journal).Select(finding => JsonSerializer.Serialize(finding)).ToArray();
        var second = ReentrancyFindings(journal).Select(finding => JsonSerializer.Serialize(finding)).ToArray();
        Assert.Equal(first, second);
    }

    [Fact]
    public void RevertedDelegateCollision_IsInformationalAndNamesExactGeometry()
    {
        var implementation = Address.FromHex("0x9300000000000000000000000000000000000003");
        var journal = new ExecutionJournal();
        Enter(journal, 1, null, 0, CallType.Root, Target);
        Enter(journal, 2, 1, 1, CallType.DelegateCall, Target, implementation);
        journal.Record(NewWrite(2, 0));
        Resolve(journal, 2, 1, FrameStateResolution.Rollback);
        Resolve(journal, 1, null, FrameStateResolution.Commit);
        journal.Record(new TransactionPersistenceEvent { Outcome = TransactionPersistenceOutcome.CommittedToState });
        var finding = Assert.Single(JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal)),
            item => item.RuleId == "SEC.STORAGE.DELEGATE_COLLISION");
        Assert.Equal(SecuritySeverity.Info, finding.Severity);
        Assert.Equal(ExecutionDisposition.Reverted, finding.ExecutionDisposition);
        Assert.Equal(PersistenceDisposition.NotApplicable, finding.PersistenceDisposition);
        Assert.Contains(Target, finding.Addresses);
        Assert.Contains(implementation, finding.Addresses);
        Assert.Contains(BigInteger.Zero, finding.StorageSlots);
    }

    private static IReadOnlyList<SecurityFinding> ReentrancyFindings(ExecutionJournal journal) =>
        JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal))
            .Where(finding => finding.Category == SecurityCategory.Reentrancy).ToArray();

    private static ExecutionJournal BuildReentryJournal(
        CallType childType,
        StateEffectEvent? childEffect,
        IReadOnlyList<StorageWriteEvent> postWrites,
        FrameStateResolution childResolution,
        bool postWritesBeforeChild = false,
        bool differentChildAddress = false,
        TransactionPersistenceOutcome persistence = TransactionPersistenceOutcome.CommittedToState)
    {
        var journal = new ExecutionJournal();
        Enter(journal, 1, null, 0, CallType.Root, Target);
        if (postWritesBeforeChild)
            foreach (var postWrite in postWrites) journal.Record(postWrite);
        Enter(journal, 2, 1, 1, childType, differentChildAddress ? Other : Target);
        if (childEffect is not null) journal.Record(childEffect);
        Resolve(journal, 2, 1, childResolution);
        if (!postWritesBeforeChild)
            foreach (var postWrite in postWrites) journal.Record(postWrite);
        Resolve(journal, 1, null, FrameStateResolution.Commit);
        journal.Record(new TransactionPersistenceEvent { Outcome = persistence });
        return journal;
    }

    private static StorageReadEvent NewRead(long frameId, BigInteger slot, Address? storageAddress = null) => new()
    {
        Scope = StateEffectScope.Frame, FrameId = frameId, ParentFrameId = frameId == 1 ? null : 1,
        InstructionId = 10 + frameId, Pc = 3, Opcode = 0x54, StorageAddress = storageAddress ?? Target,
        Slot = slot, Value = 1, IsWarm = true
    };

    private static StorageWriteEvent NewWrite(long frameId, BigInteger slot) => new()
    {
        Scope = StateEffectScope.Frame, FrameId = frameId, ParentFrameId = frameId == 1 ? null : 1,
        InstructionId = 20 + frameId, Pc = 4, Opcode = 0x55, StorageAddress = Target, Slot = slot,
        OriginalValue = 0, PreviousValue = 0, Value = 1, IsWarm = true
    };

    private static void Enter(ExecutionJournal journal, long id, long? parentId, int depth,
        CallType callType, Address contractAddress, Address? codeAddress = null)
    {
        journal.Record(new FrameEnteredEvent
        {
            FrameId = id, ParentFrameId = parentId, Depth = depth, CallType = callType,
            ContractAddress = contractAddress, CodeAddress = codeAddress, GasLimit = 100_000
        });
        journal.Record(new FrameStateCheckpointEvent { FrameId = id, ParentFrameId = parentId });
    }

    private static void Resolve(ExecutionJournal journal, long id, long? parentId,
        FrameStateResolution resolution) => journal.Record(new FrameStateResolvedEvent
        { FrameId = id, ParentFrameId = parentId, Resolution = resolution });
}
