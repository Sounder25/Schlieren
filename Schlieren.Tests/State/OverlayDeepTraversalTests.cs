using System.Numerics;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;

namespace Schlieren.Tests.State;

/// <summary>
/// Proves StateOverlay.GetStorageAtAsync handles arbitrary depth without stack overflow.
/// This is a regression test for the taxonomy host crash caused by recursive parent-chain walks.
/// </summary>
public sealed class OverlayDeepTraversalTests
{
    private static Address TestAddr => new(Convert.FromHexString("AA".PadLeft(40, '0')));

    [Fact]
    public async Task InheritedValue_DeepOverlayChain_ReturnsWithoutStackOverflow()
    {
        // Use depth significantly exceeding stack capacity to prove non-recursive
        // implementation. 2048 still succeeded on the test host; 8192+ reliably
        // overflows a naive recursive walk.
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

    [Fact]
    public async Task OverriddenValue_DeepOverlayChain_ReturnsLocalWithoutTraversingChain()
    {
        const int depth = 8192;
        var root = new GlobalState();
        root.SetStorageAt(TestAddr, BigInteger.One, 100);

        IGlobalState current = root;
        for (int i = 0; i < depth; i++)
            current = new StateOverlay(current);

        // Override at the deepest level
        ((StateOverlay)current).SetStorageAt(TestAddr, BigInteger.One, 999);

        // Should return local override without walking the chain
        var result = await current.GetStorageAtAsync(TestAddr, BigInteger.One);
        Assert.Equal(999, result);
    }

    [Fact]
    public async Task Cancellation_DeepTraversal_ThrowsOperationCanceled()
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

    [Fact]
    public async Task ShallowChain_BehavesCorrectlyAfterFix()
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
