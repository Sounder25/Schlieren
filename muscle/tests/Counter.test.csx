// schlieren test file — runs with: schlieren test
// 'node', 'accounts', and test helpers are injected by the test host.

Test("Counter starts at zero", async () =>
{
    var counter = await node.Deploy("Counter");
    var count = await counter.Call<ulong>("count");
    Assert.Equal(0UL, count);
});

Test("Increment increases count", async () =>
{
    var counter = await node.Deploy("Counter");
    await counter.Send("increment", from: accounts[0]);
    var count = await counter.Call<ulong>("count");
    Assert.Equal(1UL, count);
});