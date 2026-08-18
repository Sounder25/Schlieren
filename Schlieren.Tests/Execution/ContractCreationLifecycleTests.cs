using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class ContractCreationLifecycleTests
{
    [Fact]
    public async Task SuccessfulCreation_InitializesCreatedAccountNonce()
    {
        var (state, transition, sender, transaction, block) =
            CreateScenario([0x60, 0x00, 0x60, 0x00, 0xf3]);
        var createdAddress = CryptoUtils.DeriveContractAddress(sender, transaction.Nonce);

        var result = await transition.ApplyTransactionAsync(
            transaction,
            state,
            block,
            commit: true);

        Assert.True(result.IsSuccess, $"Creation failed: {result.Error}");
        Assert.Equal(1UL, await state.GetNonceAsync(createdAddress));
    }

    [Fact]
    public async Task FailedCreation_DoesNotPersistCreatedAccountNonce()
    {
        var (state, transition, sender, transaction, block) =
            CreateScenario([0xfe]);
        var createdAddress = CryptoUtils.DeriveContractAddress(sender, transaction.Nonce);

        var result = await transition.ApplyTransactionAsync(
            transaction,
            state,
            block,
            commit: true);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(createdAddress, state.Snapshot().Keys);
    }

    private static (
        GlobalState State,
        StateTransition Transition,
        Address Sender,
        Transaction Transaction,
        BlockContext Block) CreateScenario(byte[] initCode)
    {
        var state = new GlobalState();
        var sender = Address.FromHex("0x1000000000000000000000000000000000000001");
        state.SetBalance(sender, BigInteger.Pow(10, 18));

        var transaction = new Transaction
        {
            From = sender,
            To = null,
            Nonce = 0,
            GasLimit = 100_000,
            GasPrice = 1,
            Data = initCode,
            Authorization = TransactionAuthorization.Impersonated
        };

        var block = new BlockContext
        {
            ChainId = 1,
            Number = 1,
            Timestamp = 1,
            GasLimit = 30_000_000,
            BaseFeePerGas = 0,
            Coinbase = Address.Zero
        };

        var transition = new StateTransition(
            new EvmMachine(typeof(IOpcode).Assembly
                .GetTypes()
                .Where(type =>
                    type is { IsClass: true, IsAbstract: false } &&
                    typeof(IOpcode).IsAssignableFrom(type))
                .Select(type => (IOpcode)Activator.CreateInstance(type)!)));

        return (state, transition, sender, transaction, block);
    }
}
