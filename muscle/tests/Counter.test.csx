// Counter.test.csx — scrutor test smoke suite
// Uses Test("name", async () => { ... }) + Assert.* injected by the test runner.

var counter = await node.Deploy("Counter");

Test("initial count() == 0", async () =>
{
    var val = await counter.Call<ulong>("count");
    Assert.Equal(0UL, val);
});

Test("increment() persists storage", async () =>
{
    var tx = await counter.Send("increment", null, null);
    Assert.True(tx.Success, $"increment tx reverted: {tx.TxHash}");
    var val = await counter.Call<ulong>("count");
    Assert.Equal(1UL, val);
});

Test("second increment() reaches 2", async () =>
{
    var tx = await counter.Send("increment", null, null);
    Assert.True(tx.Success, "second increment tx reverted");
    var val = await counter.Call<ulong>("count");
    Assert.Equal(2UL, val);
});

Test("reset() brings count back to 0", async () =>
{
    var tx = await counter.Send("reset", null, null);
    Assert.True(tx.Success, "reset tx reverted");
    var val = await counter.Call<ulong>("count");
    Assert.Equal(0UL, val);
});
