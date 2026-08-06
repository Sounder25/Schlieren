using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.EELS.Tests.Conformance;

public sealed class Eip7623CoinbaseTest
{
    private static List<IOpcode> BuildOpcodes() =>
        typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!)
            .ToList();

    /// <summary>
    /// Floor case: 1 zero-byte calldata, gasLimit=floor=21010, gasPrice=10, baseFee=7.
    /// Execution succeeds (call to empty account). Coinbase gets 21010×3=63030.
    /// </summary>
    [Fact]
    public async Task Coinbase_ReceivesPriorityFee_WhenFloorApplies()
    {
        var st = new StateTransition(new EvmMachine(BuildOpcodes()));
        var globalState = new GlobalState();

        var sender   = Address.FromHex("0x407a21fc34e8578196479e5021603efcf0e635a1");
        var coinbase = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
        var toAddr   = Address.FromHex("0xaac3c7926fab4a661250c5e4ddf20414313e07aa");

        var senderPre = BigInteger.Parse("1000000000000000000000");
        globalState.SetBalance(sender, senderPre);

        var block = new BlockContext
        {
            ChainId = 1, Number = 1, Timestamp = 1000, GasLimit = 30_000_000,
            Coinbase = coinbase, BaseFeePerGas = 7, BlobHashEnabled = true, Eip7623Enabled = true,
        };

        var tx = new Transaction
        {
            From = sender, To = toAddr, Nonce = 0,
            GasPrice = new BigInteger(10), MaxFeePerGas = new BigInteger(10),
            MaxPriorityFeePerGas = BigInteger.Zero,
            GasLimit = 21010, Value = BigInteger.Zero,
            Data = new byte[] { 0x00 },   // 1 zero-byte → tokens=1, floor=21010
            TxType = 0, Authorization = TransactionAuthorization.Impersonated,
        };

        var result = await st.ApplyTransactionAsync(tx, globalState, block, commit: true);

        var coinbaseBal = await globalState.GetBalanceAsync(coinbase);
        var senderBal   = await globalState.GetBalanceAsync(sender);
        var senderNonce = await globalState.GetNonceAsync(sender);

        // floor=21010, effectiveGasPrice=10, baseFee=7, priorityFee=3
        Assert.True(result.IsSuccess, $"tx failed: {result.Error}");
        Assert.Equal(21010UL, result.GasUsed);
        Assert.Equal(new BigInteger(63030), coinbaseBal);
        // sender: pre - gasLimit×effectiveGasPrice + 0 refund = pre - 21010×10
        Assert.Equal(senderPre - 21010 * 10, senderBal);
        Assert.Equal(1UL, senderNonce);
    }

    /// <summary>
    /// INVALID opcode case: to has code 0xFE, all execution gas consumed.
    /// tokens=8334 zero-bytes → floor=104340 = gasLimit. gasPrice=10, baseFee=7.
    /// Execution fails (INVALID), tx processed: coinbase gets 104340×3=313020.
    /// </summary>
    [Fact]
    public async Task Coinbase_ReceivesPriorityFee_WhenExecutionReverts_INVALID()
    {
        var st = new StateTransition(new EvmMachine(BuildOpcodes()));
        var globalState = new GlobalState();

        var sender   = Address.FromHex("0xcdb5deba2275e8a49dc6e95995de903a1adb4c86");
        var coinbase = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
        var toAddr   = Address.FromHex("0xb7dfa5a889d05d9b2daf11f9d50b0869986ff46c");

        var senderPre = BigInteger.Parse("1000000000000000000000");
        globalState.SetBalance(sender, senderPre);
        globalState.SetNonce(toAddr, 1);
        globalState.SetCode(toAddr, new byte[] { 0xFE }); // INVALID opcode

        var block = new BlockContext
        {
            ChainId = 1, Number = 1, Timestamp = 1000, GasLimit = 30_000_000,
            Coinbase = coinbase, BaseFeePerGas = 7, BlobHashEnabled = true, Eip7623Enabled = true,
        };

        var calldata = new byte[8334]; // 8334 zero-bytes → tokens=8334, floor=104340
        var tx = new Transaction
        {
            From = sender, To = toAddr, Nonce = 0,
            GasPrice = new BigInteger(10), MaxFeePerGas = new BigInteger(10),
            MaxPriorityFeePerGas = BigInteger.Zero,
            GasLimit = 104340, Value = BigInteger.Zero,
            Data = calldata, TxType = 0, Authorization = TransactionAuthorization.Impersonated,
        };

        var result = await st.ApplyTransactionAsync(tx, globalState, block, commit: true);

        var coinbaseBal = await globalState.GetBalanceAsync(coinbase);
        var senderBal   = await globalState.GetBalanceAsync(sender);
        var senderNonce = await globalState.GetNonceAsync(sender);

        // Execution reverts (INVALID) — all execution gas consumed
        Assert.False(result.IsSuccess);
        Assert.Equal(104340UL, result.GasUsed);
        // coinbase: 104340 × 3 = 313020 = 0x04c6bc
        Assert.Equal(new BigInteger(313020), coinbaseBal);
        // sender: pre - gasLimit×effectiveGasPrice (all consumed, no refund, value=0 so no restore)
        Assert.Equal(senderPre - 104340 * 10, senderBal);
        Assert.Equal(1UL, senderNonce);
    }
}
