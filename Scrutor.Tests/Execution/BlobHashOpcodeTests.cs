using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using EvmExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Tests.Execution;

public sealed class BlobHashOpcodeTests
{
    private static readonly byte[] Hash0 =
        Convert.FromHexString("01B8C5B09810B5FC07355D3DA42E2C3A3E200C1D9A678491B7E8E256FC50CC4F");
    private static readonly byte[] Hash1 =
        Convert.FromHexString("015B4C8CC4F86AA2D2CF9E9CE97FCA704A11A6C20F6B1D6C00A6E15F6D60A6DF");
    private static readonly byte[] Hash2 =
        Convert.FromHexString("01878F80EAF10BE1A6F618E6F8C071B10A6C14D9B89A3BF2A3F3CF2DB6C5681D");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    public async Task ValidIndex_ReturnsSelectedVersionedHash(
        int index,
        int expectedHash)
    {
        var context = CreateContext([Hash0, Hash1, Hash2]);

        var (result, _) = await ExecuteAsync(context, index);

        Assert.True(result.IsSuccess);
        Assert.Equal(3UL, result.GasUsed);
        Assert.Equal(
            ToWord(new[] { Hash0, Hash1, Hash2 }[expectedHash]),
            Pop(context));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task InvalidIndex_ReturnsZero(int index)
    {
        var context = CreateContext([Hash0]);

        var (result, _) = await ExecuteAsync(context, index);

        Assert.True(result.IsSuccess);
        Assert.Equal(BigInteger.Zero, Pop(context));
    }

    [Fact]
    public async Task UInt256MaxIndex_ReturnsZeroWithoutConversionFailure()
    {
        var context = CreateContext([Hash0]);
        var max = (BigInteger.One << 256) - 1;

        var (result, _) = await ExecuteAsync(context, max);

        Assert.True(result.IsSuccess);
        Assert.Equal(BigInteger.Zero, Pop(context));
    }

    [Fact]
    public async Task EmptyHashList_ReturnsZero()
    {
        var context = CreateContext([]);

        var (result, _) = await ExecuteAsync(context, BigInteger.Zero);

        Assert.True(result.IsSuccess);
        Assert.Equal(BigInteger.Zero, Pop(context));
    }

    [Fact]
    public async Task PreCancun_BlobHashIsInvalid()
    {
        var context = CreateContext([Hash0], blobHashEnabled: false);

        var (result, _) = await ExecuteAsync(context, BigInteger.Zero);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.InvalidOpcode, result.Error);
    }

    [Fact]
    public async Task NestedCall_ReceivesSameTransactionBlobHashes()
    {
        var state = new GlobalState();
        var root = Address.FromHex(
            "0x0000000000000000000000000000000000000020");
        var child = Address.FromHex(
            "0x0000000000000000000000000000000000000010");
        var observed = new List<IReadOnlyList<byte[]>>();
        state.SetCode(root,
        [
            0xaa,
            0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00,
            0x60, 0x00, 0x60, 0x10, 0x60, 0xff, 0xf1,
            0x00
        ]);
        state.SetCode(child, [0xaa, 0x00]);
        var machine = new EvmMachine(
        [
            new CaptureBlobHashesOpcode(observed),
            new OpcodePush1(),
            new OpcodeCall(),
            new OpcodeStop()
        ]);
        var transaction = new Transaction
        {
            From = Address.Zero,
            To = root,
            GasLimit = 100_000,
            Authorization = TransactionAuthorization.Internal,
            BlobVersionedHashes = [Hash0, Hash1]
        };

        var result = await new StateTransition(machine).ApplyTransactionAsync(
            transaction,
            state,
            BlockContext.Genesis,
            commit: true);

        Assert.True(result.IsSuccess, result.Error.ToString());
        Assert.Equal(2, observed.Count);
        Assert.All(observed, hashes =>
        {
            Assert.Equal(2, hashes.Count);
            Assert.Equal(Hash0, hashes[0]);
            Assert.Equal(Hash1, hashes[1]);
        });
    }

    private static EvmExecutionContext CreateContext(
        IReadOnlyList<byte[]> hashes,
        bool blobHashEnabled = true) => new()
    {
        BlobVersionedHashes = hashes,
        Block = new BlockContext { BlobHashEnabled = blobHashEnabled }
    };

    private static async ValueTask<(ExecutionResult Result, int NextPc)>
        ExecuteAsync(EvmExecutionContext context, BigInteger index)
    {
        context.Stack.Push(index);
        return await new OpcodeBlobHash().ExecuteAsync(context);
    }

    private static BigInteger Pop(EvmExecutionContext context)
    {
        Assert.True(context.Stack.TryPop(out var value));
        return value;
    }

    private static BigInteger ToWord(byte[] value) =>
        new(value, isUnsigned: true, isBigEndian: true);

    private sealed class CaptureBlobHashesOpcode(
        List<IReadOnlyList<byte[]>> observed) : IOpcode
    {
        public byte Code => 0xaa;
        public string Name => "CAPTURE_BLOB_HASHES";

        public ValueTask<(ExecutionResult, int)> ExecuteAsync(
            EvmExecutionContext context,
            CancellationToken ct = default)
        {
            observed.Add(context.BlobVersionedHashes);
            return ValueTask.FromResult(
                (ExecutionResult.Success(0), context.ProgramCounter + 1));
        }
    }
}
