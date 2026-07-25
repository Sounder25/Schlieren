using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Xunit;

namespace Scrutor.Tests.Execution;

/// <summary>
/// Regression suite for transaction-value journaling (EIP-4844 / Yellow Paper §6.2).
///
/// Invariants under test:
///   SUCCESS:  sender loses value exactly once; recipient gains value exactly once.
///   FAILURE:  sender does not lose value; recipient gains nothing; gas+blob fees charged.
///   EVM-VISIBLE BALANCE: BALANCE(sender) excludes tx.Value before first opcode executes.
/// </summary>
public sealed class TransactionValueJournalingTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static EvmMachine BuildMachine() =>
        new(typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!));

    private static Address Addr(string hex) => Address.FromHex(hex);

    private static readonly Address Sender    = Addr("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
    private static readonly Address Recipient = Addr("0x0000000000000000000000000000000000001000");
    private static readonly Address Coinbase  = Addr("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");

    /// <summary>
    /// Bare-minimum type-3 transaction: one blob hash, no access list.
    /// Pass <paramref name="code"/> to plant bytecode at the recipient before execution.
    /// </summary>
    private static (GlobalState state, Transaction tx, BlockContext block)
        MakeBlob3Tx(
            BigInteger initialBalance,
            BigInteger txValue,
            ulong gasLimit,
            byte maxFeePerGas,
            byte maxPriorityFee,
            byte maxFeePerBlobGas,
            ulong excessBlobGas,
            byte[]? code = null)
    {
        var state = new GlobalState();
        state.SetBalance(Sender, initialBalance);
        if (code != null)
            state.SetCode(Recipient, code);

        var tx = new Transaction
        {
            From                 = Sender,
            To                   = Recipient,
            Nonce                = 0,
            GasLimit             = gasLimit,
            Value                = txValue,
            MaxFeePerGas         = maxFeePerGas,
            MaxPriorityFeePerGas = maxPriorityFee,
            MaxFeePerBlobGas     = maxFeePerBlobGas,
            TxType               = 3,
            BlobVersionedHashes  = [BlobHash(1)],
            Authorization        = TransactionAuthorization.Impersonated
        };
        var block = new BlockContext
        {
            Coinbase      = Coinbase,
            BaseFeePerGas = 7,
            ExcessBlobGas = excessBlobGas
        };
        return (state, tx, block);
    }

    private static byte[] BlobHash(byte suffix)
    {
        var h = new byte[32];
        h[0]  = 1;
        h[^1] = suffix;
        return h;
    }

    // ── 1. Zero-value type-3: no behaviour change ─────────────────────────────

    [Fact]
    public async Task ZeroValue_BlobTx_SenderOnlyPaysGasAndBlobFee()
    {
        BigInteger initialBalance = 50_000_000;
        ulong gasLimit = 21_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue: 0,
            gasLimit: gasLimit,
            maxFeePerGas: 14, maxPriorityFee: 7, maxFeePerBlobGas: 10,
            excessBlobGas: 0);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);
        var senderFinal    = await state.GetBalanceAsync(Sender);
        var recipientFinal = await state.GetBalanceAsync(Recipient);

        Assert.True(senderFinal < initialBalance, "Sender should have paid gas and blob fees");
        Assert.Equal(BigInteger.Zero, recipientFinal);
    }

    // ── 2. Successful type-3 with value=1: one debit, one credit ─────────────

    [Fact]
    public async Task SuccessfulBlobTx_Value1_SenderLosesOnce_RecipientGainsOnce()
    {
        const ulong   gasLimit      = 21_000;
        const byte    maxFee        = 14;
        const byte    maxPriority   = 7;
        const byte    maxBlobFee    = 10;
        BigInteger    txValue       = 1;
        BigInteger    initialBalance = 10_000_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue, gasLimit, maxFee, maxPriority, maxBlobFee, excessBlobGas: 0);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);

        var senderFinal    = await state.GetBalanceAsync(Sender);
        var recipientFinal = await state.GetBalanceAsync(Recipient);

        Assert.Equal(txValue, recipientFinal);

        BigInteger effectiveGasPrice = Math.Min((int)maxFee, 7 + (int)maxPriority); // 14
        BigInteger blobBaseFee       = BigInteger.One;                      // FakeExp(1,0,…)=1
        BigInteger blobCharge        = 131_072 * blobBaseFee;
        BigInteger executionCharge   = result.GasUsed * effectiveGasPrice;
        BigInteger expectedFinal     = initialBalance - txValue - executionCharge - blobCharge;

        Assert.Equal(expectedFinal, senderFinal);
    }

    // ── 3. REVERT: sender value restored, recipient gets nothing ──────────────

    [Fact]
    public async Task RevertingBlobTx_SenderValueRestored_RecipientGetsNothing()
    {
        // PUSH1 0 PUSH1 0 REVERT
        byte[] revertCode = [0x60, 0x00, 0x60, 0x00, 0xfd];

        const ulong   gasLimit      = 100_000;
        BigInteger    txValue       = 1;
        BigInteger    initialBalance = 20_000_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue, gasLimit,
            maxFeePerGas: 14, maxPriorityFee: 7, maxFeePerBlobGas: 1,
            excessBlobGas: 0, code: revertCode);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.False(result.IsSuccess);

        var senderFinal    = await state.GetBalanceAsync(Sender);
        var recipientFinal = await state.GetBalanceAsync(Recipient);

        Assert.Equal(BigInteger.Zero, recipientFinal);

        BigInteger effectiveGasPrice = Math.Min(14, 7 + 7); // 14
        BigInteger blobCharge        = 131_072 * BigInteger.One;
        BigInteger executionCharge   = result.GasUsed * effectiveGasPrice;
        BigInteger expectedFinal     = initialBalance - executionCharge - blobCharge; // value NOT deducted

        Assert.Equal(expectedFinal, senderFinal);
    }

    // ── 4. OOG: same rollback invariants as REVERT ────────────────────────────

    [Fact]
    public async Task OogBlobTx_SenderValueRestored_RecipientGetsNothing()
    {
        // PUSH1 0 JUMP (tight infinite loop, will OOG)
        byte[] oogCode = [0x60, 0x00, 0x56];

        // intrinsic = 21000 + 300 (one blob hash) = 21300; EVM gets 700 gas → OOG quickly
        const ulong   gasLimit      = 22_000;
        BigInteger    txValue       = 5;
        BigInteger    initialBalance = 5_000_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue, gasLimit,
            maxFeePerGas: 14, maxPriorityFee: 7, maxFeePerBlobGas: 1,
            excessBlobGas: 0, code: oogCode);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.False(result.IsSuccess);

        var senderFinal    = await state.GetBalanceAsync(Sender);
        var recipientFinal = await state.GetBalanceAsync(Recipient);

        Assert.Equal(BigInteger.Zero, recipientFinal);

        BigInteger blobCharge     = 131_072 * BigInteger.One;
        // OOG: all gas_limit consumed
        BigInteger executionCharge = (BigInteger)gasLimit * 14;
        BigInteger expectedFinal   = initialBalance - executionCharge - blobCharge;

        Assert.Equal(expectedFinal, senderFinal);
    }

    // ── 5. EVM-visible BALANCE excludes tx.Value before first opcode ──────────

    [Fact]
    public async Task BlobTx_EvmBalance_ExcludesValueBeforeFirstOpcode()
    {
        // ORIGIN BALANCE PUSH1 0 SSTORE STOP
        // Stores BALANCE(origin/sender) in slot 0 at the very first moment of execution.
        byte[] storeBalanceCode = [0x32, 0x31, 0x60, 0x00, 0x55, 0x00];

        const ulong   gasLimit      = 200_000;
        BigInteger    txValue       = 100;
        BigInteger    initialBalance = 5_000_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue, gasLimit,
            maxFeePerGas: 14, maxPriorityFee: 7, maxFeePerBlobGas: 1,
            excessBlobGas: 0, code: storeBalanceCode);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);

        // Expected EVM-visible sender balance:
        //   initial - (gasLimit * maxFeePerGas) - (blob_gas_used * blob_base_fee) - tx_value
        BigInteger maxExecReservation = (BigInteger)gasLimit * 14;
        BigInteger actualBlobFee      = 131_072 * BigInteger.One; // 1 blob, blob_base_fee=1
        BigInteger expectedEvmBalance = initialBalance - maxExecReservation - actualBlobFee - txValue;

        var slot0 = await state.GetStorageAtAsync(Recipient, BigInteger.Zero);
        Assert.Equal(expectedEvmBalance, slot0);
    }

    // ── 6. Multiple blobs: EVM-visible balance correct ────────────────────────

    [Fact]
    public async Task BlobTx_SixBlobs_EvmBalanceCorrect()
    {
        // ORIGIN BALANCE PUSH1 0 SSTORE STOP
        byte[] storeBalanceCode = [0x32, 0x31, 0x60, 0x00, 0x55, 0x00];

        var blobHashes = Enumerable.Range(1, 6)
            .Select(i => BlobHash((byte)i))
            .ToList();

        const ulong   gasLimit      = 200_000;
        BigInteger    txValue       = 1;
        BigInteger    initialBalance = 10_000_000;

        var state = new GlobalState();
        state.SetBalance(Sender, initialBalance);
        state.SetCode(Recipient, storeBalanceCode);

        var tx = new Transaction
        {
            From                 = Sender,
            To                   = Recipient,
            Nonce                = 0,
            GasLimit             = gasLimit,
            Value                = txValue,
            MaxFeePerGas         = 14,
            MaxPriorityFeePerGas = 7,
            MaxFeePerBlobGas     = 1,
            TxType               = 3,
            BlobVersionedHashes  = blobHashes,
            Authorization        = TransactionAuthorization.Impersonated
        };
        var block = new BlockContext
        {
            Coinbase      = Coinbase,
            BaseFeePerGas = 7,
            ExcessBlobGas = 0
        };

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);

        BigInteger maxExecReservation = (BigInteger)gasLimit * 14;
        BigInteger actualBlobFee      = 6 * 131_072 * BigInteger.One; // 6 blobs
        BigInteger expectedEvmBalance = initialBalance - maxExecReservation - actualBlobFee - txValue;

        var slot0 = await state.GetStorageAtAsync(Recipient, BigInteger.Zero);
        Assert.Equal(expectedEvmBalance, slot0);
    }

    // ── 7. Final sender balance matches settlement equation ───────────────────

    [Fact]
    public async Task SuccessfulBlobTx_FinalBalance_MatchesSettlementEquation()
    {
        const ulong   gasLimit      = 21_000;
        const byte    maxFee        = 14;
        const byte    maxPriority   = 7;
        const byte    maxBlobFee    = 1;
        BigInteger    txValue       = 1;
        BigInteger    initialBalance = 1_000_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue, gasLimit, maxFee, maxPriority, maxBlobFee, excessBlobGas: 0);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);

        BigInteger effectiveGasPrice = Math.Min((int)maxFee, 7 + (int)maxPriority);
        BigInteger blobCharge        = 131_072 * BigInteger.One;
        BigInteger executionCharge   = result.GasUsed * effectiveGasPrice;
        BigInteger expectedFinal     = initialBalance - txValue - executionCharge - blobCharge;

        Assert.Equal(expectedFinal, await state.GetBalanceAsync(Sender));
    }

    // ── 8. Coinbase receives no blob fee ──────────────────────────────────────

    [Fact]
    public async Task BlobTx_Coinbase_ReceivesNoBlobFee()
    {
        const ulong   gasLimit      = 21_000;
        const byte    maxFee        = 14;
        const byte    maxPriority   = 7;
        BigInteger    initialBalance = 5_000_000;

        var (state, tx, block) = MakeBlob3Tx(
            initialBalance, txValue: 0, gasLimit, maxFee, maxPriority,
            maxFeePerBlobGas: 10, excessBlobGas: 0);

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);

        var coinbaseBalance = await state.GetBalanceAsync(Coinbase);
        BigInteger effectiveGasPrice = Math.Min((int)maxFee, 7 + (int)maxPriority); // 14
        BigInteger priorityFee       = effectiveGasPrice - 7;              // 7
        BigInteger expectedCoinbase  = result.GasUsed * priorityFee;

        // Coinbase receives exactly the priority-fee component; no blob fee included
        Assert.Equal(expectedCoinbase, coinbaseBalance);
    }

    // ── 9. Legacy (type-0) tx: non-regression ────────────────────────────────

    [Fact]
    public async Task LegacyTx_NonZeroValue_SenderLosesValue_RecipientGains()
    {
        BigInteger initialBalance = 5_000_000;
        BigInteger txValue        = 500;

        var state = new GlobalState();
        state.SetBalance(Sender, initialBalance);

        var tx = new Transaction
        {
            From          = Sender,
            To            = Recipient,
            Nonce         = 0,
            GasLimit      = 21_000,
            GasPrice      = 10,
            Value         = txValue,
            TxType        = 0,
            Authorization = TransactionAuthorization.Impersonated
        };
        var block = new BlockContext { Coinbase = Coinbase, BaseFeePerGas = 7 };

        var result = await new StateTransition(BuildMachine())
            .ApplyTransactionAsync(tx, state, block);

        Assert.True(result.IsSuccess);

        var senderFinal    = await state.GetBalanceAsync(Sender);
        var recipientFinal = await state.GetBalanceAsync(Recipient);

        Assert.Equal(txValue, recipientFinal);
        BigInteger executionCharge = result.GasUsed * 10;
        Assert.Equal(initialBalance - txValue - executionCharge, senderFinal);
    }
}
