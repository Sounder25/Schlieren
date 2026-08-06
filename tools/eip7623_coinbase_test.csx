using System;
using System.Numerics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

// Quick inline test: EIP-7623 coinbase credit check
// gasLimit=21010, gasPrice=10, baseFee=7, 1 zero-byte calldata

var opcodes = typeof(IOpcode).Assembly
    .GetTypes()
    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
    .Select(t => (IOpcode)Activator.CreateInstance(t)!)
    .ToList();

var machine = new EvmMachine(opcodes);
var st = new StateTransition(machine);
var globalState = new GlobalState();

var sender = Address.FromHex("0x407a21fc34e8578196479e5021603efcf0e635a1");
var coinbase = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
var toAddr = Address.FromHex("0xaac3c7926fab4a661250c5e4ddf20414313e07aa");

globalState.SetBalance(sender, BigInteger.Parse("1000000000000000000000"));

var block = new BlockContext
{
    ChainId = 1,
    Number = 1,
    Timestamp = 1000,
    GasLimit = 30_000_000,
    Coinbase = coinbase,
    BaseFeePerGas = 7,
    BlobHashEnabled = true,
    Eip7623Enabled = true,
};

var tx = new Transaction
{
    From = sender,
    To = toAddr,
    Nonce = 0,
    GasPrice = new BigInteger(10),
    MaxFeePerGas = new BigInteger(10),
    MaxPriorityFeePerGas = new BigInteger(0),
    GasLimit = 21010,
    Value = BigInteger.Zero,
    Data = new byte[] { 0x00 },  // 1 zero byte
    TxType = 0,
    Authorization = TransactionAuthorization.Impersonated,
};

var result = await st.ApplyTransactionAsync(tx, globalState, block, commit: true);

Console.WriteLine($"Success: {result.IsSuccess}");
Console.WriteLine($"GasUsed: {result.GasUsed}");

var senderBal = await globalState.GetBalanceAsync(sender);
var coinbaseBal = await globalState.GetBalanceAsync(coinbase);

Console.WriteLine($"Sender  bal: {senderBal}  (expected: 999999999999999789900)");
Console.WriteLine($"Coinbase bal: {coinbaseBal}  (expected: 63030 = 0xf636)");
Console.WriteLine($"Coinbase delta: {coinbaseBal - 0}  (expected 63030)");
