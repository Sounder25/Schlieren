using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Schlieren.Core.Forks;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Execution;

/// <summary>
/// EIP-7610 / EELS account_deployable: CREATE and CREATE2 must not overwrite
/// addresses that already have nonce, code, or storage.
/// </summary>
public sealed class Eip7610CreateCollisionTests
{
    private static readonly Address Creator =
        Address.FromHex("0x0000000000000000000000000000000000002000");

    private static EvmExecutionContext CreateContext(GlobalState? state = null)
    {
        state ??= new GlobalState();
        state.SetBalance(Creator, 1_000_000);
        state.SetNonce(Creator, 0);
        return new EvmExecutionContext
        {
            ContractAddress = Creator,
            GlobalState = state,
            GasLimit = 1_000_000,
            SubCall = async (tx, isStatic, creation, codeAddr) =>
            {
                // Minimal successful create frame: empty runtime code, no gas use.
                if (creation.HasValue)
                {
                    state.SetNonce(creation.Value, 1);
                }
                return ExecutionResult.Success(0, Array.Empty<byte>());
            }
        };
    }

    private static void PushCreateArgs(EvmExecutionContext ctx, BigInteger value = default)
    {
        // CREATE stack: value, offset, length  (pops value, offset, length)
        ctx.Stack.Push(0); // length
        ctx.Stack.Push(0); // offset
        ctx.Stack.Push(value);
    }

    [Fact]
    public async Task Create_StorageOnly_Collides_Eip7610()
    {
        var state = new GlobalState();
        var dest = CryptoUtils.DeriveContractAddress(Creator, 0);
        state.SetStorageAt(dest, BigInteger.One, 42);

        var context = CreateContext(state);
        PushCreateArgs(context);

        var (result, _) = await new OpcodeCreate().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var pushed));
        Assert.Equal(BigInteger.Zero, pushed); // collision → 0
        Assert.Equal(1UL, await state.GetNonceAsync(Creator)); // creator nonce still bumped
        Assert.Equal(0UL, await state.GetNonceAsync(dest)); // dest not initialized as contract
    }

    [Fact]
    public async Task Create_CodeOnly_Collides()
    {
        var state = new GlobalState();
        var dest = CryptoUtils.DeriveContractAddress(Creator, 0);
        state.SetCode(dest, new byte[] { 0x00 });

        var context = CreateContext(state);
        PushCreateArgs(context);

        var (result, _) = await new OpcodeCreate().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var pushed));
        Assert.Equal(BigInteger.Zero, pushed);
    }

    [Fact]
    public async Task Create_EmptyAddress_Deploys()
    {
        var state = new GlobalState();
        var dest = CryptoUtils.DeriveContractAddress(Creator, 0);
        // balance-only is deployable (EIP-7610 only blocks storage/code/nonce)
        state.SetBalance(dest, 100);

        var context = CreateContext(state);
        PushCreateArgs(context);

        var (result, _) = await new OpcodeCreate().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var pushed));
        Assert.NotEqual(BigInteger.Zero, pushed);
    }

    [Fact]
    public async Task TopLevelCreate_StorageCollision_ConsumesAllExecutionGas()
    {
        var state = new GlobalState();
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        state.SetBalance(sender, BigInteger.Parse("1000000000000000000000"));
        state.SetNonce(sender, 0);

        var dest = CryptoUtils.DeriveContractAddress(sender, 0);
        state.SetStorageAt(dest, BigInteger.One, 7);

        var tx = new Transaction
        {
            From = sender,
            To = null, // CREATE
            Value = BigInteger.Zero,
            Nonce = 0,
            GasPrice = 1,
            GasLimit = 100_000,
            Data = Array.Empty<byte>(),
            TxType = 0,
            Authorization = TransactionAuthorization.Impersonated,
            EnableJournal = true,
        };

        var block = new BlockContext
        {
            Number = 1,
            BaseFeePerGas = 0,
            GasLimit = 30_000_000,
            Rules = CancunRules.Instance, // EIP-7610 active since Cancun/Paris era
        };

        var machine = new EvmMachine(typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!));

        var result = await new StateTransition(machine).ApplyTransactionAsync(tx, state, block);

        Assert.False(result.IsSuccess);
        Assert.Equal(1UL, await state.GetNonceAsync(sender)); // nonce bumped
        // All post-intrinsic gas consumed on AddressCollision
        Assert.True(result.GasUsed >= 21_000UL);
        Assert.Equal(0UL, await state.GetNonceAsync(dest));
        var collision = Assert.Single(result.Journal!.Events.OfType<GasComponentEvent>(),
            entry => entry.Component == GasComponents.TransactionCollisionBurn);
        Assert.Equal(GasSemantics.ExceptionalBurn, collision.Semantics);
        Assert.Equal(GasComponentScope.Transaction, collision.Scope);
    }

    [Fact]
    public async Task IsDeployable_FalseWhenStoragePresent()
    {
        var state = new GlobalState();
        var addr = Address.FromHex("0x00000000000000000000000000000000000000aa");
        state.SetStorageAt(addr, 0, 1);
        Assert.False(await AccountDeployability.IsDeployableAsync(state, addr));
    }

    [Fact]
    public async Task IsDeployable_TrueWhenOnlyBalance()
    {
        var state = new GlobalState();
        var addr = Address.FromHex("0x00000000000000000000000000000000000000bb");
        state.SetBalance(addr, 1);
        Assert.True(await AccountDeployability.IsDeployableAsync(state, addr));
    }
}
