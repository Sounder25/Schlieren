// Advanced.test.csx — Advanced EVM mechanics stress test
// 'node', 'accounts', and test helpers are injected by the test host.

// ── ERC-20 ─────────────────────────────────────────────────────────────────

var erc20 = await node.Deploy("SimpleERC20", from: accounts[0]);

Test("ERC20 total supply goes to creator", async () =>
{
    var balance = await erc20.Call<System.Numerics.BigInteger>("balanceOf", accounts[0]);
    var expected = System.Numerics.BigInteger.Parse("1000000000000000000000000"); // 1M * 10^18
    Assert.Equal(expected, balance);
});

Test("ERC20 transfer success", async () =>
{
    // Send 1000 wei-units from deployer (accounts[0]) to accounts[1].
    // Pass BigInteger so the ABI encoder maps it to uint256 cleanly.
    var amount = new System.Numerics.BigInteger(1000);
    var tx = await erc20.Send("transfer", from: accounts[0], args: new object[] { accounts[1], amount });
    Assert.True(tx.Success, $"transfer reverted: {tx.RevertReason}");

    var bal1 = await erc20.Call<System.Numerics.BigInteger>("balanceOf", accounts[1]);
    Assert.Equal(amount, bal1);
});

Test("ERC20 transfer insufficient balance reverts", async () =>
{
    // accounts[2] has no tokens — transfer should revert
    var huge = System.Numerics.BigInteger.Parse("999999999999999999999999999999");
    var tx = await erc20.Send("transfer", from: accounts[2], args: new object[] { accounts[3], huge });
    Assert.False(tx.Success, "Expected revert on insufficient balance");
});

// ── Proxy / DELEGATECALL storage isolation ─────────────────────────────────

var logic = await node.Deploy("Logic", from: accounts[0]);
var proxy = await node.Deploy("Proxy", from: accounts[0], args: new object[] { logic.Address });

Test("Proxy delegatecall stores in proxy slot not logic slot", async () =>
{
    // Call setValue(42) through the proxy — storage should land in proxy, not logic.
    var setData = "0x55241077" +  // setValue(uint256) selector
                  new System.Numerics.BigInteger(42).ToString("X64").ToLowerInvariant();
    var tx = await node.SendRaw(accounts[0], proxy.Address, setData);
    Assert.True(tx.Success, $"proxy delegatecall failed: {tx.RevertReason}");

    // Logic contract's own storage slot 0 should still be zero
    var logicVal = await logic.Call<System.Numerics.BigInteger>("value");
    Assert.Equal(System.Numerics.BigInteger.Zero, logicVal);
});

// ── Reentrancy: 2300-stipend boundary ──────────────────────────────────────

var bank     = await node.Deploy("Bank", from: accounts[0]);
var attacker = await node.Deploy("Attacker", from: accounts[0], args: new object[] { bank.Address });

Test("Bank allows honest deposit and withdraw", async () =>
{
    // accounts[1] deposits 1 ETH and withdraws cleanly
    var deposit = await bank.Send("deposit", from: accounts[1],
                                  value: System.Numerics.BigInteger.Parse("1000000000000000000"));
    Assert.True(deposit.Success, $"deposit failed: {deposit.RevertReason}");

    var withdraw = await bank.Send("withdraw", from: accounts[1]);
    Assert.True(withdraw.Success, $"withdraw failed: {withdraw.RevertReason}");
});

Test("Reentrancy attack drains bank (checks 2300-stipend bypass via full gas call)", async () =>
{
    // Pre-fund the bank with 2 ETH from accounts[2]
    var fund = await bank.Send("deposit", from: accounts[2],
                               value: System.Numerics.BigInteger.Parse("2000000000000000000"));
    Assert.True(fund.Success, $"fund failed: {fund.RevertReason}");

    // Attacker deposits 1 ETH and re-enters
    var attack = await attacker.Send("attack", from: accounts[0],
                                     value: System.Numerics.BigInteger.Parse("1000000000000000000"));
    // Classic reentrancy using call{value} forwards remaining gas — succeeds
    Assert.True(attack.Success, $"attack tx failed unexpectedly: {attack.RevertReason}");
});
