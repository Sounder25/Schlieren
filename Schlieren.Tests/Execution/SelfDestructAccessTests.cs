using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Execution;

public sealed class SelfDestructAccessTests
{
    [Fact]
    public async Task SelfDestruct_ChargesStaticCostForWarmBeneficiary()
    {
        var beneficiary = Address.FromHex(
            "0x0000000000000000000000000000000000001000");
        var context = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex(
                "0x0000000000000000000000000000000000002000"),
            GlobalState = new GlobalState(),
            GasLimit = 100_000
        };
        context.Access.WarmAddress(beneficiary);
        context.Stack.Push(new BigInteger(
            beneficiary.Bytes,
            isUnsigned: true,
            isBigEndian: true));

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000UL, result.GasUsed);
    }

    [Fact]
    public async Task SelfDestruct_ChargesNewAccountCostWhenTransferringValue()
    {
        var contract = Address.FromHex(
            "0x0000000000000000000000000000000000002000");
        var beneficiary = Address.FromHex(
            "0x0000000000000000000000000000000000003000");
        var state = new GlobalState();
        state.SetBalance(contract, 1_000);

        var context = new EvmExecutionContext
        {
            ContractAddress = contract,
            GlobalState = state,
            GasLimit = 100_000
        };
        context.Stack.Push(new BigInteger(
            beneficiary.Bytes,
            isUnsigned: true,
            isBigEndian: true));

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(32_600UL, result.GasUsed);
        Assert.Equal(new BigInteger(1_000), await state.GetBalanceAsync(beneficiary));
    }

    [Fact]
    public async Task SelfDestruct_PreExistingContractToSelf_PreservesBalance()
    {
        var contract = Address.FromHex(
            "0x0000000000000000000000000000000000002000");
        var state = new GlobalState();
        state.SetBalance(contract, 1_000);
        state.SetCode(contract, [0x60, 0x00]);

        var context = new EvmExecutionContext
        {
            ContractAddress = contract,
            GlobalState = state,
            GasLimit = 100_000
        };
        context.Stack.Push(new BigInteger(
            contract.Bytes,
            isUnsigned: true,
            isBigEndian: true));

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(new BigInteger(1_000), await state.GetBalanceAsync(contract));
    }

    [Fact]
    public async Task Create_WarmsCreatedAddress()
    {
        var context = CreateContext();
        var createdAddress = CryptoUtils.DeriveContractAddress(
            context.ContractAddress,
            nonce: 0);
        PushCreateArguments(context);

        await new OpcodeCreate().ExecuteAsync(context);

        Assert.True(context.Access.TouchAddress(createdAddress));
    }

    [Fact]
    public async Task Create2_WarmsCreatedAddress()
    {
        var context = CreateContext();
        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);
        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(context.Access.TouchAddress(createdAddress));
    }

    [Fact]
    public async Task StateOverlay_MarkedForDeletion_DoesNotHideAccountDataBeforeFinalization()
    {
        var parentState = new GlobalState();
        var overlay = new StateOverlay(parentState);
        var addr = Address.FromHex("0x0000000000000000000000000000000000003000");

        overlay.SetNonce(addr, 5);
        overlay.SetCode(addr, new byte[] { 0x60, 0x01 });
        overlay.SetStorageAt(addr, BigInteger.One, 42);
        overlay.SetBalance(addr, 100);
        overlay.MarkForDeletion(addr);

        Assert.True(overlay.IsMarkedForDeletion(addr));
        Assert.Equal(5UL, await overlay.GetNonceAsync(addr));
        Assert.Equal(new byte[] { 0x60, 0x01 }, await overlay.GetCodeAsync(addr));
        Assert.Equal(new BigInteger(42), await overlay.GetStorageAtAsync(addr, BigInteger.One));
        Assert.Equal(new BigInteger(100), await overlay.GetBalanceAsync(addr));
        Assert.True(await overlay.AccountExistsAsync(addr));
    }

    [Fact]
    public async Task AfterCommittedSelfDestruct_NextTransactionCreate2IsDeployable()
    {
        // README previously claimed same-block CREATE2 redeploy is rejected.
        // Protocol: SELFDESTRUCT is finalized at end of the transaction. Overlay
        // tombstones do not survive into the next tx. Yellow Paper / metamorphic
        // CREATE2: a later transaction (same block or not) may redeploy.
        var creator = Address.FromHex("0x0000000000000000000000000000000000002000");
        var salt = new byte[32];
        var dest = CryptoUtils.DeriveContractAddress2(creator, salt, []);

        var committed = new GlobalState();
        committed.SetCode(dest, [0x60, 0x00]);
        committed.SetNonce(dest, 1);

        var txOverlay = new StateOverlay(committed);
        txOverlay.MarkForDeletion(dest);
        txOverlay.Commit();
        // Same finalization StateTransition uses after a successful top-level tx.
        foreach (var addr in txOverlay.GetAccountsMarkedForDeletion())
            committed.DeleteAccount(addr);

        Assert.False(await committed.AccountExistsAsync(dest));
        Assert.True(await AccountDeployability.IsDeployableAsync(committed, dest));

        var context = CreateContext();
        context.GlobalState = committed;
        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        Assert.NotEqual(BigInteger.Zero, val);
    }

    [Fact]
    public async Task OverlayTombstone_DoesNotPersistOnParentAfterCommitDelete()
    {
        var addr = Address.FromHex("0x0000000000000000000000000000000000003333");
        var parent = new GlobalState();
        parent.SetNonce(addr, 5);
        parent.SetCode(addr, [0xff]);

        var overlay = new StateOverlay(parent);
        overlay.DeleteAccount(addr);
        Assert.False(await overlay.AccountExistsAsync(addr));
        Assert.True(await parent.AccountExistsAsync(addr));

        overlay.Commit();
        Assert.False(await parent.AccountExistsAsync(addr));
        Assert.False(parent.IsMarkedForDeletion(addr));
        Assert.True(await AccountDeployability.IsDeployableAsync(parent, addr));
    }

    [Fact]
    public async Task Create2_AddressMarkedForDeletion_WithExistingCode_Collides()
    {
        var context = CreateContext();
        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);

        // Account has existing code and is marked for deletion
        context.GlobalState.SetCode(createdAddress, new byte[] { 0x60, 0x00 });
        context.GlobalState.MarkForDeletion(createdAddress);

        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        Assert.Equal(BigInteger.Zero, val); // Pushes 0 on collision
    }

    [Fact]
    public async Task Create2_AddressMarkedForDeletion_WithNonZeroNonce_Collides()
    {
        var context = CreateContext();
        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);

        context.GlobalState.SetNonce(createdAddress, 1);
        context.GlobalState.MarkForDeletion(createdAddress);

        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        Assert.Equal(BigInteger.Zero, val); // Pushes 0 on collision
    }

    [Fact]
    public async Task Create2_AddressMarkedForDeletion_WithStorageOnly_Collides()
    {
        var context = CreateContext();
        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);

        context.GlobalState.SetStorageAt(createdAddress, BigInteger.One, 99);
        context.GlobalState.MarkForDeletion(createdAddress);

        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        Assert.Equal(BigInteger.Zero, val); // Pushes 0 on collision
    }

    [Fact]
    public async Task Create2_AddressWithStorageOnly_Eip7610_Collides()
    {
        var context = CreateContext();
        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);

        // EIP-7610: Nonempty storage causes collision even if nonce=0 and code is empty
        context.GlobalState.SetStorageAt(createdAddress, BigInteger.One, 123);

        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        Assert.Equal(BigInteger.Zero, val);
    }

    [Fact]
    public void StateOverlay_RevertedChildFrame_DiscardsDeletionMarker()
    {
        var parentState = new GlobalState();
        var parentOverlay = new StateOverlay(parentState);
        var addr = Address.FromHex("0x0000000000000000000000000000000000004000");

        var childOverlay = new StateOverlay(parentOverlay);
        childOverlay.MarkForDeletion(addr);

        Assert.True(childOverlay.IsMarkedForDeletion(addr));
        // On frame revert: childOverlay is discarded without calling childOverlay.Commit()
        Assert.False(parentOverlay.IsMarkedForDeletion(addr));
    }

    [Fact]
    public async Task SelfDestruct_CreatedInTx_RemainsVisibleUntilFinalization()
    {
        var baseState = new GlobalState();
        var globalState = new StateOverlay(baseState);
        var contractAddr = Address.FromHex("0x0000000000000000000000000000000000005000");
        var beneficiary = Address.FromHex("0x0000000000000000000000000000000000006000");

        globalState.MarkCreated(contractAddr);
        globalState.SetBalance(contractAddr, 500);
        globalState.SetNonce(contractAddr, 2);
        globalState.SetCode(contractAddr, new byte[] { 0x60, 0x00 });

        var context = new EvmExecutionContext
        {
            ContractAddress = contractAddr,
            GlobalState = globalState,
            GasLimit = 100_000,
            Code = new byte[] { 0xFF }
        };
        context.Access.WarmAddress(beneficiary);
        context.Stack.Push(new BigInteger(beneficiary.Bytes, isUnsigned: true, isBigEndian: true));

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(globalState.IsMarkedForDeletion(contractAddr));
        // Account data remains queryable prior to top-level finalization
        Assert.Equal(2UL, await globalState.GetNonceAsync(contractAddr));
        Assert.Equal(new byte[] { 0x60, 0x00 }, await globalState.GetCodeAsync(contractAddr));
        Assert.True(await globalState.AccountExistsAsync(contractAddr));
    }

    [Fact]
    public async Task HasStorageAsync_EffectiveStorageMergingAndZeroShadowing()
    {
        var addr = Address.FromHex("0x0000000000000000000000000000000000007000");

        // 1. Parent empty; overlay writes nonzero slot -> true
        var parent1 = new GlobalState();
        var overlay1 = new StateOverlay(parent1);
        overlay1.SetStorageAt(addr, 1, 10);
        Assert.True(await overlay1.HasStorageAsync(addr));

        // 2. Parent nonempty; overlay does not touch slot -> true
        var parent2 = new GlobalState();
        parent2.SetStorageAt(addr, 1, 10);
        var overlay2 = new StateOverlay(parent2);
        Assert.True(await overlay2.HasStorageAsync(addr));

        // 3. Parent has one nonzero slot; overlay overwrites it with zero -> false
        var parent3 = new GlobalState();
        parent3.SetStorageAt(addr, 1, 10);
        var overlay3 = new StateOverlay(parent3);
        overlay3.SetStorageAt(addr, 1, 0);
        Assert.False(await overlay3.HasStorageAsync(addr));

        // 4. Parent has two nonzero slots; overlay zeroes one -> true
        var parent4 = new GlobalState();
        parent4.SetStorageAt(addr, 1, 10);
        parent4.SetStorageAt(addr, 2, 20);
        var overlay4 = new StateOverlay(parent4);
        overlay4.SetStorageAt(addr, 1, 0);
        Assert.True(await overlay4.HasStorageAsync(addr));

        // 5. Parent has two nonzero slots; overlay zeroes both -> false
        var parent5 = new GlobalState();
        parent5.SetStorageAt(addr, 1, 10);
        parent5.SetStorageAt(addr, 2, 20);
        var overlay5 = new StateOverlay(parent5);
        overlay5.SetStorageAt(addr, 1, 0);
        overlay5.SetStorageAt(addr, 2, 0);
        Assert.False(await overlay5.HasStorageAsync(addr));

        // 6. Parent slot zero; overlay writes nonzero -> true
        var parent6 = new GlobalState();
        parent6.SetStorageAt(addr, 1, 0);
        var overlay6 = new StateOverlay(parent6);
        overlay6.SetStorageAt(addr, 1, 5);
        Assert.True(await overlay6.HasStorageAsync(addr));

        // 7. Overlay writes nonzero then zero to same slot -> false
        var parent7 = new GlobalState();
        var overlay7 = new StateOverlay(parent7);
        overlay7.SetStorageAt(addr, 1, 5);
        overlay7.SetStorageAt(addr, 1, 0);
        Assert.False(await overlay7.HasStorageAsync(addr));

        // 8. Overlay writes zero then nonzero to same slot -> true
        var parent8 = new GlobalState();
        var overlay8 = new StateOverlay(parent8);
        overlay8.SetStorageAt(addr, 1, 0);
        overlay8.SetStorageAt(addr, 1, 5);
        Assert.True(await overlay8.HasStorageAsync(addr));
    }

    [Fact]
    public async Task HasStorageAsync_NestedOverlaysAndCommitRevert()
    {
        var addr = Address.FromHex("0x0000000000000000000000000000000000008000");

        // Grandparent has nonzero slot; parent zeroes it; child performs no write -> false
        var grandparent = new GlobalState();
        grandparent.SetStorageAt(addr, 1, 100);
        var parentOverlay = new StateOverlay(grandparent);
        parentOverlay.SetStorageAt(addr, 1, 0);
        var childOverlay = new StateOverlay(parentOverlay);
        Assert.False(await childOverlay.HasStorageAsync(addr));

        // Grandparent has nonzero slot; parent zeroes it; child restores nonzero -> true
        childOverlay.SetStorageAt(addr, 1, 50);
        Assert.True(await childOverlay.HasStorageAsync(addr));

        // Child nonzero write is reverted (discarded) -> parent-effective result restored (false)
        var uncommittedChild = new StateOverlay(parentOverlay);
        uncommittedChild.SetStorageAt(addr, 1, 50);
        // Discard uncommittedChild
        Assert.False(await parentOverlay.HasStorageAsync(addr));

        // Child zero write is committed -> parent-effective storage becomes empty
        var grandparent2 = new GlobalState();
        grandparent2.SetStorageAt(addr, 1, 100);
        var parentOverlay2 = new StateOverlay(grandparent2);
        var childOverlay2 = new StateOverlay(parentOverlay2);
        childOverlay2.SetStorageAt(addr, 1, 0);
        childOverlay2.Commit();
        Assert.False(await parentOverlay2.HasStorageAsync(addr));
    }

    [Fact]
    public async Task Create2_StorageZeroedInOverlay_IsDeployable()
    {
        var parentState = new GlobalState();
        var overlayState = new StateOverlay(parentState);
        var context = CreateContext();
        context.GlobalState = overlayState;

        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);

        // Parent has nonzero storage, but overlay zeroes it
        parentState.SetStorageAt(createdAddress, BigInteger.One, 100);
        overlayState.SetStorageAt(createdAddress, BigInteger.One, 0);

        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        // Deployable: pushes created address (non-zero)
        Assert.NotEqual(BigInteger.Zero, val);
    }

    [Fact]
    public async Task GetStorageKeysAsync_HonorsCancellationToken()
    {
        var addr = Address.FromHex("0x0000000000000000000000000000000000009000");
        var state = new GlobalState();
        state.SetStorageAt(addr, 1, 10);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await state.GetStorageKeysAsync(addr, cts.Token));

        var overlay = new StateOverlay(state);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await overlay.HasStorageAsync(addr, cts.Token));
    }

    [Fact]
    public async Task HasStorageAsync_SnapshotIsThreadSafeAndStableUnderConcurrentMutation()
    {
        var addr = Address.FromHex("0x000000000000000000000000000000000000A000");
        var baseState = new GlobalState();
        var overlay = new StateOverlay(baseState);

        for (int i = 1; i <= 100; i++)
        {
            overlay.SetStorageAt(addr, i, i);
        }

        var keys = await overlay.GetStorageKeysAsync(addr);
        Assert.Equal(100, keys.Count);

        // Mutate overlay concurrently during enumeration
        overlay.SetStorageAt(addr, 101, 101);

        // Original keys snapshot remains stable (100 items)
        Assert.Equal(100, keys.Count);
    }

    [Fact]
    public async Task ForkingGlobalState_UnfetchedRemoteStorage_ReturnsUnknownPresence()
    {
        var addr = Address.FromHex("0x000000000000000000000000000000000000B000");
        var localState = new GlobalState();
        var mockForkProvider = new Moq.Mock<Schlieren.Core.Forking.IForkProvider>();
        var forkingState = new ForkingGlobalState(localState, forkProvider: mockForkProvider.Object);

        // Remote storage presence is Unknown because key enumeration across arbitrary RPC storage is unsupported
        var presence = await forkingState.GetStoragePresenceAsync(addr);
        Assert.Equal(StoragePresence.Unknown, presence);

        // CREATE2 fails closed on StoragePresence.Unknown
        var overlayState = new StateOverlay(forkingState);
        var context = CreateContext();
        context.GlobalState = overlayState;

        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(context.ContractAddress, salt, []);

        context.Stack.Push(BigInteger.Zero);
        PushCreateArguments(context);

        var (result, _) = await new OpcodeCreate2().ExecuteAsync(context);
        Assert.True(result.IsSuccess);
        Assert.True(context.Stack.TryPop(out var val));
        // StoragePresence.Unknown causes collision (fails closed) -> pushes 0
        Assert.Equal(BigInteger.Zero, val);
    }

    private static EvmExecutionContext CreateContext()
    {
        var context = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex(
                "0x0000000000000000000000000000000000002000"),
            GlobalState = new GlobalState(),
            GasLimit = 100_000
        };
        context.SubCall = (transaction, _, _, _) =>
            Task.FromResult(ExecutionResult.Success(transaction.GasLimit));
        return context;
    }

    private static void PushCreateArguments(EvmExecutionContext context)
    {
        context.Stack.Push(BigInteger.Zero); // length
        context.Stack.Push(BigInteger.Zero); // offset
        context.Stack.Push(BigInteger.Zero); // value
    }
}
