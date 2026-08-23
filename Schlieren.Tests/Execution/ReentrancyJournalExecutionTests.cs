using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class ReentrancyJournalExecutionTests
{
    [Fact]
    public async Task RealAtoBtoA_ProducesStateContactAndCriticalPostWrite()
    {
        var run = await Run(attackerReverts: false, enableJournal: true);
        var journal = Assert.IsType<ExecutionJournal>(run.Result.Journal);
        var analysis = JournalAnalysis.Build(journal);
        var findings = JournalSecurityAnalyzer.Analyze(analysis);

        Assert.True(run.Result.IsSuccess);
        Assert.Equal(3, analysis.Frames.Count);
        var reentered = Assert.Single(analysis.Frames.Values,
            frame => frame.Depth == 2 && frame.ContractAddress.Equals(ReentrancyJournalFixture.Target));
        Assert.Equal(CallType.Call, reentered.CallType);
        Assert.Contains(findings, finding =>
            finding.RuleId == "SEC.REENTRANCY.STATE_CONTACT" && finding.PrimaryFrameId == reentered.Id);
        Assert.Contains(findings, finding =>
            finding.RuleId == "SEC.REENTRANCY.POST_WRITE" &&
            finding.Severity == SecuritySeverity.Critical &&
            finding.StorageSlots.Contains(BigInteger.One));
    }

    [Fact]
    public async Task RevertedAttackerPath_IsVisibleButInformational()
    {
        var run = await Run(attackerReverts: true, enableJournal: true);
        var journal = Assert.IsType<ExecutionJournal>(run.Result.Journal);
        var findings = JournalSecurityAnalyzer.Analyze(JournalAnalysis.Build(journal))
            .Where(finding => finding.Category == SecurityCategory.Reentrancy)
            .ToArray();

        Assert.True(run.Result.IsSuccess);
        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Equal(SecuritySeverity.Info, finding.Severity));
        Assert.Contains(findings, finding => finding.ExecutionDisposition == ExecutionDisposition.Reverted);
    }

    [Fact]
    public async Task JournalToggle_DoesNotChangeCanonicalExecution()
    {
        var withJournal = await Run(attackerReverts: false, enableJournal: true);
        var withoutJournal = await Run(attackerReverts: false, enableJournal: false);

        Assert.Equal(withoutJournal.Result.IsSuccess, withJournal.Result.IsSuccess);
        Assert.Equal(withoutJournal.Result.Error, withJournal.Result.Error);
        Assert.Equal(withoutJournal.Result.GasUsed, withJournal.Result.GasUsed);
        Assert.Equal(withoutJournal.Result.ReturnData, withJournal.Result.ReturnData);
        Assert.Equal(withoutJournal.Slot0, withJournal.Slot0);
        Assert.Equal(withoutJournal.Slot1, withJournal.Slot1);
    }

    private static async Task<(ExecutionResult Result, BigInteger Slot0, BigInteger Slot1)> Run(
        bool attackerReverts,
        bool enableJournal)
    {
        var state = new GlobalState();
        ReentrancyJournalFixture.Install(state, attackerReverts);
        var result = await new StateTransition(new EvmMachine(ReentrancyJournalFixture.Opcodes()))
            .ApplyTransactionAsync(
                new Transaction
                {
                    From = ReentrancyJournalFixture.Sender,
                    To = ReentrancyJournalFixture.Target,
                    GasLimit = 500_000,
                    GasPrice = 1,
                    Authorization = TransactionAuthorization.Impersonated,
                    EnableJournal = enableJournal
                },
                state,
                new BlockContext { BaseFeePerGas = 1, Rules = ForkRulesFactory.For("Osaka") });
        return (
            result,
            await state.GetStorageAtAsync(ReentrancyJournalFixture.Target, 0),
            await state.GetStorageAtAsync(ReentrancyJournalFixture.Target, 1));
    }
}
