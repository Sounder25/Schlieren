using System.Numerics;
using Scrutor.Core.State;

namespace Scrutor.Core.Forks;

/// <summary>
/// All fork-variant behaviour that opcodes, the state transition, and the
/// intrinsic gas calculator need.  Exactly one implementation is attached to
/// each <see cref="Primitives.BlockContext"/> — no more boolean flags scattered
/// across the codebase.
///
/// Design mirrors EELS: each fork class inherits the previous fork and only
/// overrides what changed.  Adding a new fork = one new sealed class.
/// </summary>
public interface IForkRules
{
    // ── Identity ────────────────────────────────────────────────────────────
    Fork Fork { get; }

    // ── SLOAD gas ───────────────────────────────────────────────────────────
    /// <summary>
    /// Gas cost of SLOAD.
    /// Frontier=50, TangerineWhistle=200, Istanbul=800,
    /// Berlin+=cold:2100 / warm:100 (EIP-2929).
    /// </summary>
    ulong SloadCost(bool isWarm);

    // ── SSTORE gas ──────────────────────────────────────────────────────────
    /// <summary>EIP-2200 reentrancy guard (Istanbul+): OOG if gas_left ≤ 2300.</summary>
    bool HasEip2200Reentrancy { get; }

    /// <summary>EIP-2929 warm/cold slot surcharge on SSTORE (Berlin+).</summary>
    bool HasEip2929WarmCold { get; }

    /// <summary>
    /// Compute SSTORE base cost and refund delta.
    /// Caller applies cold surcharge separately when <see cref="HasEip2929WarmCold"/> is true.
    /// </summary>
    (ulong cost, long refundDelta) SstoreBaseCost(
        BigInteger originalValue, BigInteger currentValue, BigInteger newValue);

    /// <summary>Max gas refundable as a fraction of gasUsed denominator (2=50%, 5=20%).</summary>
    ulong RefundQuotient { get; }

    // ── Transaction rules ────────────────────────────────────────────────────
    bool HasEip1559BaseFee        { get; }  // London+
    bool HasEip2930AccessLists    { get; }  // Berlin+
    bool HasEip3529RefundCap      { get; }  // London+ (refund cap = gasUsed/5 instead of /2)
    bool HasEip3541EfPrefix       { get; }  // London+: reject code starting with 0xEF
    bool HasEip3860InitcodeLimit  { get; }  // Shanghai+: initcode size limit + cost
    bool HasEip4844BlobTx         { get; }  // Cancun+
    bool HasEip7623CalldataFloor  { get; }  // Prague+
    bool HasEip7702SetCode        { get; }  // Prague+
    bool HasEip7951P256Verify     { get; }  // Osaka+: P256VERIFY precompile at 0x0100
    bool HasEip7883ModExpIncrease  { get; }  // Osaka+: ModExp gas cost increase
    /// <summary>
    /// EIP-7825 (Osaka+): reject transactions whose gas limit exceeds
    /// <see cref="TxMaxGasLimit"/> (16_777_216). Pre-execution validity only —
    /// single-dimensional; not Amsterdam multi-dim reservoir.
    /// </summary>
    bool HasEip7825TxGasLimitCap  { get; }
    /// <summary>EIP-7825 cap when <see cref="HasEip7825TxGasLimitCap"/> is true; otherwise unused.</summary>
    ulong TxMaxGasLimit { get; }

    // ── Intrinsic gas ────────────────────────────────────────────────────────
    /// <summary>Cost per zero byte of calldata (Frontier–Istanbul=4, unchanged).</summary>
    ulong CalldataZeroByteCost    { get; }
    /// <summary>Cost per non-zero byte (Frontier=68, Istanbul+=16 via EIP-2028).</summary>
    ulong CalldataNonZeroByteCost { get; }

    // ── External account/code opcode gas ────────────────────────────────────
    /// <summary>
    /// Gas for BALANCE, EXTCODESIZE, EXTCODECOPY (base), EXTCODEHASH.
    /// Frontier=20, TangerineWhistle=700, Istanbul=700, Berlin+=warm/cold.
    /// </summary>
    ulong ExtAccountCost(bool isWarm);

    /// <summary>Gas for EXTCODEHASH (Constantinople=400, Istanbul+=700, Berlin+=warm/cold).</summary>
    ulong ExtCodeHashCost(bool isWarm);

    /// <summary>
    /// Base gas for CALL/CALLCODE/DELEGATECALL/STATICCALL (the "extra_gas" before access surcharge).
    /// Frontier/Homestead=40; TangerineWhistle+=700; Berlin+=0 (absorbed into AccessCost warm/cold).
    /// Note: Berlin CALL charges ACCESS cost separately via ExtAccountCost(isWarm).
    /// </summary>
    ulong CallBaseCost { get; }

    /// <summary>
    /// True for Frontier/Homestead: CALL charges the gas argument to the parent
    /// (parent pays CALL_BASE + gas_arg + extras; child receives gas_arg).
    /// False from TangerineWhistle onwards (EIP-150 changed semantics: parent pays
    /// access costs only; child receives min(gas_arg, 63/64 * remaining)).
    /// </summary>
    bool HasPreEip150CallGas { get; }

    // ── Opcodes ──────────────────────────────────────────────────────────────
    bool HasDelegateCall          { get; }  // Homestead+
    bool HasRevert                { get; }  // Byzantium+
    bool HasStaticCall            { get; }  // Byzantium+
    bool HasReturnDataOps         { get; }  // Byzantium+: RETURNDATASIZE / RETURNDATACOPY
    bool HasCreate2               { get; }  // Constantinople+
    bool HasBitwiseShift          { get; }  // Constantinople+: SHL/SHR/SAR
    bool HasExtCodeHash           { get; }  // Constantinople+
    bool HasChainId               { get; }  // Istanbul+
    bool HasSelfBalance           { get; }  // Istanbul+
    bool HasPush0                 { get; }  // Shanghai+: PUSH0
    bool HasPrevRandao            { get; }  // Paris+: DIFFICULTY opcode → PREVRANDAO
    bool HasMcopy                 { get; }  // Cancun+
    bool HasBlobHash              { get; }  // Cancun+
    bool HasTloadTstore           { get; }  // Cancun+
    bool HasEip6780SelfdestructRestriction { get; }  // Cancun+: SELFDESTRUCT only deletes if created in same tx
    bool HasEip161ContractNonce            { get; }  // SpuriousDragon+: new contracts start at nonce 1
    bool HasEip2565ModExpPricing           { get; }  // Berlin+: EIP-2565 ModExp gas formula (GQUADDIVISOR=3, word-count complexity)
    
    // ── Precompile gas (fork-dependent) ──────────────────────────────────────
    /// <summary>Gas for BN254 Add (0x06). Byzantium=500, Istanbul+=150 (EIP-1108).</summary>
    ulong BnAddGas { get; }
    /// <summary>Gas for BN254 Scalar Mul (0x07). Byzantium=40000, Istanbul+=6000 (EIP-1108).</summary>
    ulong BnMulGas { get; }
    /// <summary>Base gas for BN254 Pairing (0x08). Byzantium=100000, Istanbul+=45000 (EIP-1108).</summary>
    ulong BnPairingBaseGas { get; }
    /// <summary>Per-point gas for BN254 Pairing (0x08). Byzantium=80000, Istanbul+=34000 (EIP-1108).</summary>
    ulong BnPairingPerPointGas { get; }
    
    /// <summary>Base gas for SELFDESTRUCT. Frontier/Homestead=0; TangerineWhistle+=5000.</summary>
    ulong SelfdestructBaseCost { get; }
    /// <summary>Extra gas for SELFDESTRUCT to a new (non-existent) account. Tangerine+=25000; Frontier/Homestead=0.</summary>
    ulong SelfdestructNewAccountCost { get; }

    // ── Precompiles ──────────────────────────────────────────────────────────
    /// <summary>
    /// Number of active precompile addresses (0x01..0xN).
    /// Frontier=4, Byzantium=8 (+MODEXP,BN254), Istanbul+=9 (+BLAKE2F),
    /// Cancun+=10 (+KZG), Prague+=19 (+BLS12-381 × 9).
    /// </summary>
    int PrecompileCount { get; }

    // ── Account rules ────────────────────────────────────────────────────────
    /// <summary>EIP-161 (SpuriousDragon+): delete empty accounts touched by state transition.</summary>
    bool HasEip161EmptyAccountDeletion { get; }

    /// <summary>EIP-155 (SpuriousDragon+): replay-protection chain-id in signatures.</summary>
    bool HasEip155ReplayProtection { get; }
}
