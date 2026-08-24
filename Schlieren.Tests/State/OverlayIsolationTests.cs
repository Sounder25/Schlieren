using System.Numerics;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;

namespace Schlieren.Tests.State;

/// <summary>
/// Verifies that StateOverlay provides correct transactional isolation
/// (tombstone semantics) for DeleteAccount, and that TransientStorageOverlay
/// provides correct isolation for CREATE frames (EIP-1153).
///
/// Each overlay may mutate only its immediate parent during its own Commit().
///
/// Root cause: DeleteAccount used to bypass the overlay buffer and
/// directly mutate _parent. SetNonce/SetCode left orphan entries in
/// the buffer that got committed later, resurrecting ghost accounts.
/// The concrete failure was the EIP-170/CREATE2 ghost-account bug in
/// the tstore_rollback_on_failed_create EELS fixture.
/// </summary>
public sealed class OverlayIsolationTests
{
    private static Address Addr(string hex) =>
        new(Convert.FromHexString(hex.Replace("0x", "").PadLeft(40, '0')));

    private static GlobalState ParentWith(Address addr, ulong nonce = 1, string code = "")
    {
        var gs = new GlobalState();
        gs.SetNonce(addr, nonce);
        if (code.Length > 0) gs.SetCode(addr, Convert.FromHexString(code));
        return gs;
    }

    private static Address TestAddr => new(Convert.FromHexString("AA".PadLeft(40, '0')));

    // ── 1: discard path ────────────────────────────────────────────────────────
    [Fact]
    public async Task DeleteAccount_DiscardedOverlay_DoesNotDeleteParent()
    {
        var addr = Addr("0xAA");
        var gs   = ParentWith(addr, nonce: 5);
        var ov   = new StateOverlay(gs);

        ov.DeleteAccount(addr);

        // discard overlay (no Commit) — parent must still contain the account
        Assert.Equal(5UL, await gs.GetNonceAsync(addr));
        Assert.True(await gs.AccountExistsAsync(addr));
    }

    // ── 2: commit path ─────────────────────────────────────────────────────────
    [Fact]
    public async Task DeleteAccount_CommittedOverlay_DeletesParent()
    {
        var addr = Addr("0xBB");
        var gs   = ParentWith(addr, nonce: 3);
        var ov   = new StateOverlay(gs);

        ov.DeleteAccount(addr);
        ov.Commit();

        Assert.False(await gs.AccountExistsAsync(addr));
        Assert.Equal(0UL, await gs.GetNonceAsync(addr));
    }

    // ── 3: the EIP-170/CREATE2 ghost-account regression ────────────────────────
    [Fact]
    public async Task CreateThenDelete_CommittedOverlay_DoesNotCreateGhostAccount()
    {
        var addr = Addr("0xCC");
        var gs   = new GlobalState();          // addr does NOT exist in parent
        var ov   = new StateOverlay(gs);

        // Simulates CreateRevertHelper: set nonce=0, set code=empty, delete
        ov.SetNonce(addr, 0);
        ov.SetCode(addr, Array.Empty<byte>());
        ov.DeleteAccount(addr);

        ov.Commit();

        // Ghost account MUST NOT exist in parent after commit
        Assert.False(await gs.AccountExistsAsync(addr));
        Assert.Equal(0UL, await gs.GetNonceAsync(addr));
    }

    // ── 4: delete then write = resurrection ────────────────────────────────────
    [Fact]
    public async Task DeleteThenWrite_CommittedOverlay_RecreatesAccount()
    {
        var addr = Addr("0xDD");
        var gs   = ParentWith(addr, nonce: 7);
        var ov   = new StateOverlay(gs);

        ov.DeleteAccount(addr);
        // Subsequent write removes the tombstone and recreates locally
        ov.SetNonce(addr, 42);

        // Overlay sees new nonce (NOT the old one, NOT zero)
        Assert.Equal(42UL, await ov.GetNonceAsync(addr));

        ov.Commit();

        Assert.Equal(42UL, await gs.GetNonceAsync(addr));
    }

    // ── 5: nested overlay — discard child, outer unchanged ─────────────────────
    [Fact]
    public async Task NestedOverlay_DeleteThenDiscard_DoesNotAffectParentOverlay()
    {
        var addr  = Addr("0xEE");
        var gs    = ParentWith(addr, nonce: 9);
        var outer = new StateOverlay(gs);
        var inner = new StateOverlay(outer);

        inner.DeleteAccount(addr);

        // inner sees deletion
        Assert.False(await inner.AccountExistsAsync(addr));

        // discard inner (no inner.Commit()) — outer must be untouched
        Assert.True(await outer.AccountExistsAsync(addr));
        Assert.Equal(9UL, await outer.GetNonceAsync(addr));

        // global also untouched
        Assert.True(await gs.AccountExistsAsync(addr));
    }

    // ── 6: deletion is invisible inside overlay (tombstone blocks read-through) ─
    [Fact]
    public async Task DeletedAccount_IsInvisibleInsideOverlayBeforeCommit()
    {
        var addr = Addr("0xFF");
        var gs   = ParentWith(addr, nonce: 99, code: "6001"); // non-empty code
        var ov   = new StateOverlay(gs);

        ov.DeleteAccount(addr);

        // All reads in the overlay must behave as though account is absent
        Assert.Equal(0UL,                  await ov.GetNonceAsync(addr));
        Assert.Equal(BigInteger.Zero,      await ov.GetBalanceAsync(addr));
        Assert.Equal(Array.Empty<byte>(),  await ov.GetCodeAsync(addr));
        Assert.False(await ov.AccountExistsAsync(addr));
        // Storage must also be absent
        Assert.Equal(BigInteger.Zero, await ov.GetStorageAtAsync(addr, BigInteger.One));
    }

    // ── 7: nested commit propagates one level at a time ────────────────────────
    [Fact]
    public async Task NestedOverlay_CommittedDeletion_PropagatesOnlyOneLevel()
    {
        var addr        = Addr("0x11");
        var gs          = ParentWith(addr, nonce: 11);
        var parentOv    = new StateOverlay(gs);
        var childOv     = new StateOverlay(parentOv);

        childOv.DeleteAccount(addr);
        childOv.Commit();

        // parentOv must now see the deletion
        Assert.False(await parentOv.AccountExistsAsync(addr));

        // global must STILL contain the account (parent overlay not committed yet)
        Assert.True(await gs.AccountExistsAsync(addr));
        Assert.Equal(11UL, await gs.GetNonceAsync(addr));

        // now commit the outer overlay
        parentOv.Commit();

        // global finally loses the account
        Assert.False(await gs.AccountExistsAsync(addr));
    }

    // ── 8: Reset(Address) must not leak an EIP-6780 deletion mark ──────────────
    [Fact]
    public void ResetAddress_ClearsMarkForDeletion_DoesNotLeakThroughCommit()
    {
        // Regression test: Reset(Address) cleared _buffer/_createdAccounts/_tombstones for
        // the address but not _accountsMarkedForDeletion. A per-address rollback (undoing a
        // CREATE whose SELFDESTRUCT should be fully discarded) would still leak the deletion
        // mark: Commit() would call MarkForDeletion on the parent for an address whose
        // creation was supposed to be entirely undone.
        var addr  = Addr("0x22");
        var gs    = new GlobalState();
        var outer = new StateOverlay(gs);
        var inner = new StateOverlay(outer);

        inner.SetNonce(addr, 1);
        inner.MarkForDeletion(addr);
        Assert.True(inner.IsMarkedForDeletion(addr));

        inner.Reset(addr);

        Assert.False(inner.IsMarkedForDeletion(addr));

        inner.Commit();

        Assert.DoesNotContain(addr, outer.GetAccountsMarkedForDeletion());
    }

    // ── 9: deep traversal — inherited value through 8,192 overlays ───────────────
    [Fact]
    public async Task DeepTraversal_InheritedValue_ReturnsWithoutStackOverflow()
    {
        // Proves GetStorageAtAsync handles arbitrary depth without stack overflow.
        // This is a regression test for the taxonomy host crash caused by
        // recursive parent-chain walks.
        const int depth = 8192;
        var root = new GlobalState();
        root.SetStorageAt(TestAddr, BigInteger.One, 42);

        // Build chain: root → ov_1 → ov_2 → ... → ov_depth
        IGlobalState current = root;
        for (int i = 0; i < depth; i++)
            current = new StateOverlay(current);

        // Read inherited value from the deepest overlay
        var result = await current.GetStorageAtAsync(TestAddr, BigInteger.One);
        Assert.Equal(42, result);
    }

    // ── 10: deep traversal — ancestor override found through chain ─────────────
    [Fact]
    public async Task DeepTraversal_AncestorOverride_ReturnsAncestorValue()
    {
        // Prove that traversal actually walks the chain to find an ancestor override.
        // Override is placed at an ancestor overlay, then 2,048+ overlays are stacked
        // above it. The deepest overlay must traverse and find the ancestor's value.
        const int ancestorDepth = 100;
        const int chainAbove = 2048;
        const int totalDepth = ancestorDepth + chainAbove;

        var root = new GlobalState();
        root.SetStorageAt(TestAddr, BigInteger.One, 1); // root value

        IGlobalState current = root;
        for (int i = 0; i < totalDepth; i++)
        {
            current = new StateOverlay(current);

            // Place override at the ancestor position
            if (i == ancestorDepth)
                ((StateOverlay)current).SetStorageAt(TestAddr, BigInteger.One, 999);
        }

        // Read from deepest overlay; must find ancestor override, not root
        var result = await current.GetStorageAtAsync(TestAddr, BigInteger.One);
        Assert.Equal(999, result);
    }

    // ── 11: deep traversal — cancellation throws ───────────────────────────────
    [Fact]
    public async Task DeepTraversal_Cancellation_ThrowsOperationCanceled()
    {
        const int depth = 8192;
        var root = new GlobalState();
        root.SetStorageAt(TestAddr, BigInteger.One, 1);

        IGlobalState current = root;
        for (int i = 0; i < depth; i++)
            current = new StateOverlay(current);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            current.GetStorageAtAsync(TestAddr, BigInteger.One, cts.Token).AsTask());
    }

    // ── 12: shallow chain — behavior unchanged after fix ────────────────────────
    [Fact]
    public async Task ShallowChain_CommitPropagation_WorksCorrectly()
    {
        var root = new GlobalState();
        root.SetStorageAt(TestAddr, BigInteger.One, 10);

        var ov1 = new StateOverlay(root);
        var ov2 = new StateOverlay(ov1);

        ov1.SetStorageAt(TestAddr, BigInteger.One, 20);
        ov2.SetStorageAt(TestAddr, BigInteger.One, 30);

        // ov2 sees its own override
        Assert.Equal(30, await ov2.GetStorageAtAsync(TestAddr, BigInteger.One));

        // ov1 sees its own override
        Assert.Equal(20, await ov1.GetStorageAtAsync(TestAddr, BigInteger.One));

        // root is unchanged before commit
        Assert.Equal(10, await root.GetStorageAtAsync(TestAddr, BigInteger.One));

        // Commit ov2 → ov1 → root
        ov2.Commit();
        Assert.Equal(30, await ov1.GetStorageAtAsync(TestAddr, BigInteger.One));

        ov1.Commit();
        Assert.Equal(30, await root.GetStorageAtAsync(TestAddr, BigInteger.One));
    }
}

// Note: EIP-1153 transient storage isolation for CREATE frames is covered by the
// EELS conformance fixture:
//   tests/cancun/eip1153_tstore/test_tstorage_create_contexts.py
//     ::test_tstore_rollback_on_failed_create[fork_Osaka-state_test-create_opcode_CREATE2]
//
// The fixture encodes this invariant:
//   CREATE2 #1: TLOAD(1)=0, TSTORE(1, 24576), RETURN(0, 24586) → EIP-170 FAIL
//   CREATE2 #2: TLOAD(1) MUST still be 0 (not 24576 leaked from #1)
//
// Root cause fixed: ExecuteInternalAsync was calling transientFrame.Commit() for
// CREATE frames before EIP-170/deposit-OOG/EIP-3541 validation ran, leaking
// transient writes into the parent. Fix: gate transientFrame.Commit() on
// !creationAddress.HasValue so CREATE frames never auto-commit transient storage.
