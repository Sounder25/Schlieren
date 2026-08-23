using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Primitives;

namespace Schlieren.Tests.Security;

public sealed class JournalSecurityAnalyzerTests
{
    [Fact]
    public void ReenteredStorageOwner_WithWrite_ProducesProofLinkedFinding()
    {
        var address = Address.FromHex("0x9100000000000000000000000000000000000001");
        var journal = BuildTwoFrameJournal(
            address,
            address,
            CallType.Call,
            codeAddress: null,
            slot: 1,
            childResolution: FrameStateResolution.Commit);

        var finding = Assert.Single(
            JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal)),
            item => item.RuleId == "SEC.REENTRANCY.REENTRY");

        Assert.Equal(2, finding.PrimaryFrameId);
        Assert.Equal(SecuritySeverity.Medium, finding.Severity);
        Assert.Equal(ExecutionDisposition.Survived, finding.ExecutionDisposition);
        Assert.NotEmpty(finding.SupportingEventSequences);
        Assert.Equal([1L], finding.FrameAncestry);
    }

    [Fact]
    public void RevertedDelegateCollision_IsInformationalAndNamesExactGeometry()
    {
        var proxy = Address.FromHex("0x9200000000000000000000000000000000000002");
        var implementation = Address.FromHex("0x9300000000000000000000000000000000000003");
        var journal = BuildTwoFrameJournal(
            proxy,
            proxy,
            CallType.DelegateCall,
            implementation,
            slot: 0,
            childResolution: FrameStateResolution.Rollback);

        var finding = Assert.Single(
            JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal)),
            item => item.RuleId == "SEC.STORAGE.DELEGATE_COLLISION");

        Assert.Equal(SecuritySeverity.Info, finding.Severity);
        Assert.Equal(ExecutionDisposition.Reverted, finding.ExecutionDisposition);
        Assert.Equal(PersistenceDisposition.NotApplicable, finding.PersistenceDisposition);
        Assert.Contains(proxy, finding.Addresses);
        Assert.Contains(implementation, finding.Addresses);
        Assert.Contains(BigInteger.Zero, finding.StorageSlots);
    }

    private static ExecutionJournal BuildTwoFrameJournal(
        Address rootAddress,
        Address childAddress,
        CallType childType,
        Address? codeAddress,
        BigInteger slot,
        FrameStateResolution childResolution)
    {
        var journal = new ExecutionJournal();
        Enter(journal, 1, null, 0, CallType.Root, rootAddress, null);
        Enter(journal, 2, 1, 1, childType, childAddress, codeAddress);
        journal.Record(new StorageWriteEvent
        {
            Scope = StateEffectScope.Frame,
            FrameId = 2,
            ParentFrameId = 1,
            InstructionId = 7,
            Pc = 3,
            Opcode = 0x55,
            StorageAddress = childAddress,
            Slot = slot,
            OriginalValue = 0,
            PreviousValue = 0,
            Value = 1,
            IsWarm = true
        });
        journal.Record(new FrameStateResolvedEvent
        {
            FrameId = 2,
            ParentFrameId = 1,
            Resolution = childResolution
        });
        journal.Record(new FrameStateResolvedEvent { FrameId = 1, Resolution = FrameStateResolution.Commit });
        journal.Record(new TransactionPersistenceEvent
        {
            Outcome = TransactionPersistenceOutcome.CommittedToState
        });
        return journal;
    }

    private static void Enter(
        ExecutionJournal journal,
        long id,
        long? parent,
        int depth,
        CallType type,
        Address contract,
        Address? code)
    {
        journal.Record(new FrameEnteredEvent
        {
            FrameId = id,
            ParentFrameId = parent,
            Depth = depth,
            CallType = type,
            ContractAddress = contract,
            CodeAddress = code,
            GasLimit = 100_000
        });
        journal.Record(new FrameStateCheckpointEvent { FrameId = id, ParentFrameId = parent });
    }
}
