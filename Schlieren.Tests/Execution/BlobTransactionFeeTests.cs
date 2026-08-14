using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

public sealed class BlobTransactionFeeTests
{
    [Fact]
    public async Task ApplyTransactionAsync_DeductsAndBurnsActualBlobFee()
    {
        var sender = Address.FromHex("0x1000000000000000000000000000000000000001");
        var recipient = Address.FromHex("0x2000000000000000000000000000000000000002");
        var state = new GlobalState();
        var initialBalance = new BigInteger(10_000_000);
        state.SetBalance(sender, initialBalance);

        var tx = new Transaction
        {
            From = sender,
            To = recipient,
            Nonce = 0,
            GasLimit = 21_000,
            GasPrice = 10,
            MaxFeePerGas = 10,
            MaxPriorityFeePerGas = 3,
            MaxFeePerBlobGas = 10,
            TxType = 3,
            BlobVersionedHashes =
            [
                VersionedHash(1),
                VersionedHash(2),
                VersionedHash(3)
            ],
            Authorization = TransactionAuthorization.Impersonated
        };
        var block = new BlockContext
        {
            BaseFeePerGas = 7,
            ExcessBlobGas = 0
        };

        var machine = new EvmMachine(typeof(IOpcode).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IOpcode).IsAssignableFrom(type))
            .Select(type => (IOpcode)Activator.CreateInstance(type)!));
        var result = await new StateTransition(machine)
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);
        Assert.Equal(21_000UL, result.GasUsed);
        var expectedExecutionFee = new BigInteger(21_000 * 10);
        var expectedBlobFee = new BigInteger(3 * 131_072);
        Assert.Equal(
            initialBalance - expectedExecutionFee - expectedBlobFee,
            await state.GetBalanceAsync(sender));
    }

    private static byte[] VersionedHash(byte suffix)
    {
        var hash = new byte[32];
        hash[0] = 1;
        hash[^1] = suffix;
        return hash;
    }
}
