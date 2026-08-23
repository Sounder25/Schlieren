using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Gas;
using Schlieren.Core.State;

namespace Schlieren.Tests.Gas;

public sealed class GasMathTests
{
    [Fact]
    public void AddChecked_RejectsUlongOverflow()
    {
        Assert.Throws<OverflowException>(() => GasMath.AddChecked(ulong.MaxValue, 1));
    }

    [Fact]
    public void MultiplyChecked_RejectsUlongOverflow()
    {
        Assert.Throws<OverflowException>(() => GasMath.MultiplyChecked(ulong.MaxValue, 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(31, 1)]
    [InlineData(32, 1)]
    [InlineData(33, 2)]
    public void WordCount_RoundsUpWithoutHostNarrowing(long bytes, long expected)
    {
        Assert.Equal(new BigInteger(expected), GasMath.WordCount(new BigInteger(bytes)));
    }

    [Fact]
    public void WordCount_AcceptsValuesBeyondUlong()
    {
        var bytes = BigInteger.One << 200;

        Assert.Equal((bytes + 31) / 32, GasMath.WordCount(bytes));
    }

    [Fact]
    public void TryGetHostMemoryEnd_ZeroLengthDoesNotExpandHugeOffset()
    {
        var success = GasMath.TryGetHostMemoryEnd(
            BigInteger.One << 200,
            BigInteger.Zero,
            16 * 1024 * 1024,
            out var end);

        Assert.True(success);
        Assert.Equal(0, end);
    }

    [Fact]
    public void TryGetHostMemoryEnd_RejectsEndBeyondConfiguredLimit()
    {
        var success = GasMath.TryGetHostMemoryEnd(
            16 * 1024 * 1024,
            1,
            16 * 1024 * 1024,
            out _);

        Assert.False(success);
    }

    [Fact]
    public void TryGetHostMemoryEnd_AcceptsExactConfiguredLimit()
    {
        var success = GasMath.TryGetHostMemoryEnd(
            16 * 1024 * 1024 - 32,
            32,
            16 * 1024 * 1024,
            out var end);

        Assert.True(success);
        Assert.Equal(16 * 1024 * 1024, end);
    }

    [Fact]
    public void FormulaContexts_AreValueTypesWithoutMutableExecutionState()
    {
        Type[] contexts =
        [
            typeof(TransactionGasContext),
            typeof(MemoryGasContext),
            typeof(AccessGasContext),
            typeof(SstoreGasContext),
            typeof(CallGasContext),
            typeof(CreateGasContext),
            typeof(PrecompileGasContext),
            typeof(ExceptionalHaltGasContext),
            typeof(SettlementGasContext)
        ];

        Assert.All(contexts, context => Assert.True(context.IsValueType, context.FullName));
        Assert.All(contexts.SelectMany(context => context.GetProperties()), property =>
        {
            Assert.False(typeof(IGlobalState).IsAssignableFrom(property.PropertyType));
            Assert.NotEqual(typeof(Transaction), property.PropertyType);
            Assert.NotEqual(typeof(Schlieren.Core.Execution.ExecutionContext), property.PropertyType);
        });
    }
}
