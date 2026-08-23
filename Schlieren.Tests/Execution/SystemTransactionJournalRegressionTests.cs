using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class SystemTransactionJournalRegressionTests
{
    private const ulong SystemCallGas = 30_000_000;
    private const int FixtureStorageSlots = 1_357;
    private const int FixtureGasPadding = 2_158;
    private static readonly Address SystemCaller =
        Address.FromHex("0xfffffffffffffffffffffffffffffffffffffffe");

    [Theory]
    [InlineData("EIP-7002", "0x00000961ef480eb55e80d19ad83579a64c007002")]
    [InlineData("EIP-7251", "0x0000bbddc7ce488642fb579f8b00f3a590007251")]
    public async Task SystemRequestFixture_ReceivesFullGasAndCommitsEveryStorageWrite(
        string eip,
        string targetHex)
    {
        var target = Address.FromHex(targetHex);
        var state = new GlobalState();
        var code = BuildReachesGasLimitFixtureCode();
        state.SetCode(target, code);

        var result = await Transition().ApplyTransactionAsync(
            new Transaction
            {
                From = SystemCaller,
                To = target,
                GasLimit = SystemCallGas,
                GasPrice = 0,
                Authorization = TransactionAuthorization.System,
                EnableJournal = true
            },
            state,
            OsakaBlock());

        Assert.True(result.IsSuccess, $"{eip} system call failed: {result.Error}");
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        Assert.Empty(journal.Events.OfType<IntrinsicGasChargedEvent>());

        var root = Assert.Single(journal.Events.OfType<FrameEnteredEvent>());
        Assert.Null(root.ParentFrameId);
        Assert.Equal(SystemCallGas, root.GasLimit);

        var writes = journal.Events.OfType<StorageWriteEvent>().ToArray();
        Assert.Equal(FixtureStorageSlots, writes.Length);
        var finalWrite = writes[^1];
        Assert.Equal(target, finalWrite.StorageAddress);
        Assert.Equal(new BigInteger(FixtureStorageSlots - 1), finalWrite.Slot);
        Assert.Equal(BigInteger.One, finalWrite.Value);
        Assert.Contains(journal.Events.OfType<OpcodeGasEvent>(), opcode =>
            opcode.InstructionId == finalWrite.InstructionId && opcode.Name == "SSTORE");

        for (var slot = 0; slot < FixtureStorageSlots; slot++)
            Assert.Equal(BigInteger.One, await state.GetStorageAtAsync(target, slot));
    }

    [Fact]
    public async Task OrdinaryExternalTransaction_ChargesIntrinsicExactlyOnceBeforeRootFrame()
    {
        var sender = Address.FromHex("0x9100000000000000000000000000000000000001");
        var target = Address.FromHex("0x9200000000000000000000000000000000000002");
        var state = new GlobalState();
        state.SetBalance(sender, 1_000_000);
        state.SetCode(target, [0x00]);
        var rules = ForkRulesFactory.For("Osaka");
        var tx = new Transaction
        {
            From = sender,
            To = target,
            GasLimit = 100_000,
            GasPrice = 1,
            Authorization = TransactionAuthorization.Impersonated,
            EnableJournal = true
        };
        var intrinsicGas = IntrinsicGas.Compute(tx, rules);

        var result = await new StateTransition(new EvmMachine([new OpcodeStop()]))
            .ApplyTransactionAsync(tx, state,
                new BlockContext { BaseFeePerGas = 1, Rules = rules });

        Assert.True(result.IsSuccess);
        var journal = Assert.IsType<ExecutionJournal>(result.Journal);
        var intrinsic = Assert.Single(journal.Events.OfType<IntrinsicGasChargedEvent>());
        Assert.Equal(intrinsicGas, intrinsic.Amount);

        var root = Assert.Single(journal.Events.OfType<FrameEnteredEvent>());
        Assert.Equal(tx.GasLimit - intrinsicGas, root.GasLimit);
        Assert.True(intrinsic.Sequence < root.Sequence);

        var settlement = Assert.Single(journal.Events.OfType<TransactionSettledEvent>());
        Assert.Equal(result.GasUsed, settlement.ChargedGas);
        Assert.Equal(tx.GasLimit, settlement.ChargedGas + settlement.UnusedGasReturned);
    }

    private static StateTransition Transition() => new(new EvmMachine(
    [
        new OpcodeStop(),
        new OpcodePush1(),
        new OpcodePush2(),
        new OpcodeSstore(),
        new OpcodeJumpDest()
    ]));

    private static BlockContext OsakaBlock() => new()
    {
        Number = 1,
        BaseFeePerGas = 0,
        GasLimit = SystemCallGas,
        Coinbase = Address.Zero,
        Rules = ForkRulesFactory.For("Osaka")
    };

    private static byte[] BuildReachesGasLimitFixtureCode()
    {
        var code = new List<byte>(10_045);
        for (var slot = 0; slot < FixtureStorageSlots; slot++)
        {
            code.Add(0x60); // PUSH1 value
            code.Add(0x01);
            if (slot <= byte.MaxValue)
            {
                code.Add(0x60); // PUSH1 slot
                code.Add((byte)slot);
            }
            else
            {
                code.Add(0x61); // PUSH2 slot
                code.Add((byte)(slot >> 8));
                code.Add((byte)slot);
            }
            code.Add(0x55); // SSTORE
        }

        code.AddRange(Enumerable.Repeat((byte)0x5b, FixtureGasPadding)); // JUMPDEST
        code.Add(0x00); // STOP
        Assert.Equal(10_045, code.Count);
        return code.ToArray();
    }
}
