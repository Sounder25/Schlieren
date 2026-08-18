using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Forks;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Conformance;

/// <summary>
/// Spot-checks EIP-7702 authorization processing:
/// signer code set, signer nonce bumped, sender nonce bumped.
/// </summary>
public sealed class Eip7702BasicTest
{
    private static List<IOpcode> BuildOpcodes() =>
        typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!)
            .ToList();

    [Fact]
    public async Task Authorization_SetsSignerCodeAndBumpsNonces()
    {
        var st = new StateTransition(new EvmMachine(BuildOpcodes()));
        var globalState = new GlobalState();

        // Sender: 0x0c4848... nonce=0, balance=large
        var sender   = Address.FromHex("0x0c4848f99786f3a750cfc6d0cb62e9edf64d31bb");
        // Signer: 0x4c27a7... nonce=0, not in pre (empty account)
        var signer   = Address.FromHex("0x4c27a74c25067330da74487a844de15e7747b7ee");
        // Delegate: 0xfbe95a...
        var delegate_ = Address.FromHex("0xfbe95a58917d8536842271e6c811ad8ab4d12280");
        // To: 0x308d4f...
        var toAddr   = Address.FromHex("0x308d4fb82c3d5fc05022dad61266a0759ec4b73d");
        var coinbase = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");

        globalState.SetBalance(sender, BigInteger.Parse("1000000000000000000000"));
        // to has code (just set nonce=1 to match fixture)
        globalState.SetNonce(toAddr, 1);
        globalState.SetCode(toAddr, new byte[] { 0x5A }); // GAS opcode

        var block = new BlockContext
        {
            ChainId = 1, Number = 1, Timestamp = 1000, GasLimit = 30_000_000,
            Coinbase = coinbase, BaseFeePerGas = 7, Rules = ForkRulesFactory.For("Prague"),
        };

        var tx = new Transaction
        {
            From = sender, To = toAddr, Nonce = 0,
            GasPrice = new BigInteger(10), MaxFeePerGas = new BigInteger(10),
            MaxPriorityFeePerGas = BigInteger.Zero,
            GasLimit = 100_000, Value = BigInteger.Zero,
            Data = Array.Empty<byte>(), TxType = 4,
            Authorization = TransactionAuthorization.Impersonated,
            AuthorizationList = new[]
            {
                new Eip7702Authorization
                {
                    ChainId = 0, // any chain
                    DelegateAddress = delegate_,
                    Nonce = 0,   // signer current nonce = 0
                    Signer = signer,
                    IsValid = true,
                }
            }
        };

        await st.ApplyTransactionAsync(tx, globalState, block, commit: true);

        // Sender nonce bumped
        var senderNonce = await globalState.GetNonceAsync(sender);
        Assert.Equal(1UL, senderNonce);

        // Signer code = 0xEF0100 || delegate address
        var signerCode = await globalState.GetCodeAsync(signer);
        Assert.Equal(23, signerCode.Length);
        Assert.Equal(0xEF, signerCode[0]);
        Assert.Equal(0x01, signerCode[1]);
        Assert.Equal(0x00, signerCode[2]);
        Assert.Equal(delegate_.Bytes, signerCode[3..]);

        // Signer nonce bumped to 1
        var signerNonce = await globalState.GetNonceAsync(signer);
        Assert.Equal(1UL, signerNonce);
    }
}
