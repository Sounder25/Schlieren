using System.Numerics;

namespace Scrutor.Core.Forks;

// ═══════════════════════════════════════════════════════════════════════════
//  Abstract base — Frontier defaults
//  Every concrete fork class inherits the previous and overrides only what
//  changed — exactly mirroring the EELS ethereum/forks/ structure.
//  IMPORTANT: No class in this chain should be sealed — each must be
//  inheritable by the next fork.
// ═══════════════════════════════════════════════════════════════════════════

public abstract class ForkRules : IForkRules
{
    public abstract Fork Fork { get; }

    // SLOAD: Frontier = 50 flat (no warm/cold concept yet)
    public virtual ulong SloadCost(bool isWarm) => 50;

    // SSTORE: Frontier — flat costs, no EIP-2200, no EIP-2929
    public virtual bool HasEip2200Reentrancy => false;
    public virtual bool HasEip2929WarmCold   => false;

    // Frontier SSTORE: SET=20000, RESET=5000, CLEAR gives 15000 refund
    public virtual (ulong cost, long refundDelta) SstoreBaseCost(
        BigInteger originalValue, BigInteger currentValue, BigInteger newValue)
    {
        if (newValue == currentValue) return (0, 0);
        if (newValue != BigInteger.Zero) return (20_000, 0);
        return (5_000, 15_000); // clearing → 5000 gas + 15000 refund
    }

    public virtual ulong RefundQuotient => 2; // 50% before London

    // Transaction rules
    public virtual bool HasEip1559BaseFee          => false;
    public virtual bool HasEip2930AccessLists       => false;
    public virtual bool HasEip3529RefundCap         => false;
    public virtual bool HasEip3541EfPrefix          => false;
    public virtual bool HasEip3860InitcodeLimit     => false;
    public virtual bool HasEip4844BlobTx            => false;
    public virtual bool HasEip7623CalldataFloor     => false;
    public virtual bool HasEip7702SetCode           => false;

    // Intrinsic gas calldata costs
    public virtual ulong CalldataZeroByteCost       => 4;   // unchanged all forks
    public virtual ulong CalldataNonZeroByteCost    => 68;  // EIP-2028 (Istanbul) drops to 16

    // External account/code opcode gas
    // Frontier: BALANCE/EXTCODESIZE/EXTCODECOPY = 20 flat (no warm/cold)
    public virtual ulong ExtAccountCost(bool isWarm)  => 20;
    public virtual ulong ExtCodeHashCost(bool isWarm) => 20; // not available pre-Constantinople, but safe default

    // CALL base cost: Frontier/Homestead = 40 flat
    public virtual ulong CallBaseCost => 40;

    // Opcode availability
    public virtual bool HasDelegateCall             => false;
    public virtual bool HasRevert                   => false;
    public virtual bool HasStaticCall               => false;
    public virtual bool HasReturnDataOps            => false;
    public virtual bool HasCreate2                  => false;
    public virtual bool HasBitwiseShift             => false;
    public virtual bool HasExtCodeHash              => false;
    public virtual bool HasChainId                  => false;
    public virtual bool HasSelfBalance              => false;
    public virtual bool HasPush0                    => false;
    public virtual bool HasPrevRandao               => false;
    public virtual bool HasMcopy                    => false;
    public virtual bool HasBlobHash                 => false;
    public virtual bool HasTloadTstore              => false;
    public virtual bool HasEip6780SelfdestructRestriction => false; // Pre-Cancun: always delete

    // Precompiles: Frontier = 0x01–0x04
    public virtual int PrecompileCount => 4;

    // Account rules
    public virtual bool HasEip161EmptyAccountDeletion => false;
    public virtual bool HasEip155ReplayProtection      => false;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Frontier — concrete base (ForkRules is abstract, needs a concrete class)
// ═══════════════════════════════════════════════════════════════════════════
public class FrontierRules : ForkRules
{
    public static readonly FrontierRules Instance = new();
    public override Fork Fork => Fork.Frontier;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Homestead — EIP-7: DELEGATECALL
// ═══════════════════════════════════════════════════════════════════════════
public class HomesteadRules : FrontierRules
{
    public static new readonly HomesteadRules Instance = new();
    public override Fork Fork => Fork.Homestead;
    public override bool HasDelegateCall => true;
}

// ═══════════════════════════════════════════════════════════════════════════
//  TangerineWhistle — EIP-150: SLOAD repriced 50→200
// ═══════════════════════════════════════════════════════════════════════════
public class TangerineWhistleRules : HomesteadRules
{
    public static new readonly TangerineWhistleRules Instance = new();
    public override Fork Fork => Fork.TangerineWhistle;
    public override ulong SloadCost(bool isWarm) => 200;
    // EIP-150: BALANCE/EXTCODESIZE/EXTCODECOPY repriced from 20 → 700
    public override ulong ExtAccountCost(bool isWarm) => 700;
    public override ulong ExtCodeHashCost(bool isWarm) => 700;
    // EIP-150: OPCODE_CALL_BASE repriced 40 → 700
    public override ulong CallBaseCost => 700;
}

// ═══════════════════════════════════════════════════════════════════════════
//  SpuriousDragon — EIP-155: replay protection, EIP-161: empty account cleanup
// ═══════════════════════════════════════════════════════════════════════════
public class SpuriousDragonRules : TangerineWhistleRules
{
    public static new readonly SpuriousDragonRules Instance = new();
    public override Fork Fork => Fork.SpuriousDragon;
    public override bool HasEip161EmptyAccountDeletion => true;
    public override bool HasEip155ReplayProtection      => true;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Byzantium — EIP-140: REVERT, EIP-214: STATICCALL, EIP-211: RETURNDATA*
//              EIP-196/197: BN254 precompiles (+0x05–0x08)
// ═══════════════════════════════════════════════════════════════════════════
public class ByzantiumRules : SpuriousDragonRules
{
    public static new readonly ByzantiumRules Instance = new();
    public override Fork Fork => Fork.Byzantium;
    public override bool HasRevert        => true;
    public override bool HasStaticCall    => true;
    public override bool HasReturnDataOps => true;
    public override int  PrecompileCount  => 8; // +MODEXP(0x05)+BN254(0x06-0x08)
}

// ═══════════════════════════════════════════════════════════════════════════
//  Constantinople — EIP-145: SHL/SHR/SAR, EIP-1014: CREATE2,
//                   EIP-1052: EXTCODEHASH
//  (EIP-1283 was added and immediately reverted; we match ConstantinopleFix/EELS)
// ═══════════════════════════════════════════════════════════════════════════
public class ConstantinopleRules : ByzantiumRules
{
    public static new readonly ConstantinopleRules Instance = new();
    public override Fork Fork => Fork.Constantinople;
    public override bool HasCreate2      => true;
    public override bool HasBitwiseShift => true;
    public override bool HasExtCodeHash  => true;
    // EIP-1052: EXTCODEHASH added at 400 gas (Istanbul later raises to 700)
    public override ulong ExtCodeHashCost(bool isWarm) => 400;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Istanbul — EIP-1884: SLOAD=800, SELFBALANCE, CHAINID
//             EIP-2028: calldata non-zero byte 68→16
//             EIP-2200: SSTORE net-metering + reentrancy guard
//             EIP-152: BLAKE2F precompile (+0x09)
// ═══════════════════════════════════════════════════════════════════════════
public class IstanbulRules : ConstantinopleRules
{
    public static new readonly IstanbulRules Instance = new();
    public override Fork Fork => Fork.Istanbul;

    public override ulong SloadCost(bool isWarm) => 800; // EIP-1884
    public override bool  HasChainId             => true;
    public override bool  HasSelfBalance         => true;
    public override ulong CalldataNonZeroByteCost => 16; // EIP-2028
    public override bool  HasEip2200Reentrancy   => true;
    public override int   PrecompileCount        => 9;   // +BLAKE2F (0x09)
    // EIP-1884: BALANCE/EXTCODESIZE/EXTCODECOPY repriced 700 (already inherited), EXTCODEHASH 700
    public override ulong ExtCodeHashCost(bool isWarm) => 700;

    // EIP-2200 tri-state SSTORE metering (no warm/cold yet — Berlin adds that)
    public override (ulong cost, long refundDelta) SstoreBaseCost(
        BigInteger originalValue, BigInteger currentValue, BigInteger newValue)
    {
        const ulong SET   = 20_000;
        const ulong RESET = 5_000;
        const ulong NOOP  = 800;   // SLOAD cost as no-op cost (EIP-2200 spec)

        if (currentValue == newValue) return (NOOP, 0);

        if (originalValue == currentValue)
        {
            if (originalValue == BigInteger.Zero) return (SET, 0);
            if (newValue == BigInteger.Zero)       return (RESET, 15_000);
            return (RESET, 0);
        }

        // Dirty slot (subsequent write this tx)
        long refund = 0;
        if (originalValue != BigInteger.Zero)
        {
            if (currentValue == BigInteger.Zero) refund -= 15_000;
            if (newValue     == BigInteger.Zero) refund += 15_000;
        }
        if (newValue == originalValue)
            refund += originalValue == BigInteger.Zero
                ? (long)(SET - NOOP)   // +19200
                : (long)(RESET - NOOP); // +4200
        return (NOOP, refund);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Berlin — EIP-2929: warm/cold SLOAD (warm=100, cold=2100)
//           EIP-2930: optional access lists (type-1 tx)
// ═══════════════════════════════════════════════════════════════════════════
public class BerlinRules : IstanbulRules
{
    public static new readonly BerlinRules Instance = new();
    public override Fork Fork => Fork.Berlin;

    public override bool  HasEip2929WarmCold     => true;
    public override bool  HasEip2930AccessLists   => true;
    public override ulong SloadCost(bool isWarm)  => isWarm ? 100UL : 2_100UL;
    // EIP-2929: BALANCE/EXTCODESIZE/EXTCODECOPY/EXTCODEHASH use warm=100/cold=2600
    public override ulong ExtAccountCost(bool isWarm)  => isWarm ? 100UL : 2_600UL;
    public override ulong ExtCodeHashCost(bool isWarm) => isWarm ? 100UL : 2_600UL;
    // EIP-2929: CALL base cost is 0 — the warm/cold ACCESS cost is charged directly
    // as accessCost=ExtAccountCost(isWarm), so CallBaseCost must be 0 to avoid double-charge.
    public override ulong CallBaseCost => 0;

    // Berlin SSTORE: EIP-2200 base costs adjusted for warm/cold world
    // NOOP=100 (warm), RESET=2900 (=COLD_WRITE-COLD_READ), SET=20000 unchanged
    // Clear refund stays at 15000 (EIP-3529 reduces it in London)
    public override (ulong cost, long refundDelta) SstoreBaseCost(
        BigInteger originalValue, BigInteger currentValue, BigInteger newValue)
    {
        const ulong SET   = 20_000;
        const ulong RESET = 2_900;
        const ulong NOOP  = 100;
        const long  CLEAR = 15_000; // still 15000 in Berlin; London drops to 4800

        if (currentValue == newValue) return (NOOP, 0);

        if (originalValue == currentValue)
        {
            if (originalValue == BigInteger.Zero) return (SET, 0);
            if (newValue == BigInteger.Zero)       return (RESET, CLEAR);
            return (RESET, 0);
        }

        long refund = 0;
        if (originalValue != BigInteger.Zero)
        {
            if (currentValue == BigInteger.Zero) refund -= CLEAR;
            if (newValue     == BigInteger.Zero) refund += CLEAR;
        }
        if (newValue == originalValue)
            refund += originalValue == BigInteger.Zero
                ? (long)(SET - NOOP)    // +19900
                : (long)(RESET - NOOP); // +2800
        return (NOOP, refund);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  London — EIP-1559: base fee, EIP-3529: refund cap 50%→20% + clear refund
//           15000→4800, EIP-3541: reject 0xEF-prefix code
// ═══════════════════════════════════════════════════════════════════════════
public class LondonRules : BerlinRules
{
    public static new readonly LondonRules Instance = new();
    public override Fork Fork => Fork.London;

    public override bool  HasEip1559BaseFee   => true;
    public override bool  HasEip3529RefundCap => true;
    public override bool  HasEip3541EfPrefix  => true;
    public override ulong RefundQuotient      => 5; // gasUsed/5 = 20% max refund

    // EIP-3529: clear refund 15000 → 4800
    public override (ulong cost, long refundDelta) SstoreBaseCost(
        BigInteger originalValue, BigInteger currentValue, BigInteger newValue)
    {
        const ulong SET   = 20_000;
        const ulong RESET = 2_900;
        const ulong NOOP  = 100;
        const long  CLEAR = 4_800; // EIP-3529

        if (currentValue == newValue) return (NOOP, 0);

        if (originalValue == currentValue)
        {
            if (originalValue == BigInteger.Zero) return (SET, 0);
            if (newValue == BigInteger.Zero)       return (RESET, CLEAR);
            return (RESET, 0);
        }

        long refund = 0;
        if (originalValue != BigInteger.Zero)
        {
            if (currentValue == BigInteger.Zero) refund -= CLEAR;
            if (newValue     == BigInteger.Zero) refund += CLEAR;
        }
        if (newValue == originalValue)
            refund += originalValue == BigInteger.Zero
                ? (long)(SET - NOOP)    // +19900
                : (long)(RESET - NOOP); // +2800
        return (NOOP, refund);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Paris — EIP-3675: proof-of-stake; DIFFICULTY opcode → PREVRANDAO
// ═══════════════════════════════════════════════════════════════════════════
public class ParisRules : LondonRules
{
    public static new readonly ParisRules Instance = new();
    public override Fork Fork => Fork.Paris;
    public override bool HasPrevRandao => true;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Shanghai — EIP-3855: PUSH0, EIP-3860: initcode size limit + cost,
//             EIP-4895: withdrawals
// ═══════════════════════════════════════════════════════════════════════════
public class ShanghaiRules : ParisRules
{
    public static new readonly ShanghaiRules Instance = new();
    public override Fork Fork => Fork.Shanghai;
    public override bool HasPush0               => true;
    public override bool HasEip3860InitcodeLimit => true;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Cancun — EIP-1153: TLOAD/TSTORE, EIP-4844: blob tx,
//           EIP-5656: MCOPY, EIP-4788: BLOBHASH,
//           EIP-7516: BLOBBASEFEE, KZG precompile (+0x0A)
// ═══════════════════════════════════════════════════════════════════════════
public class CancunRules : ShanghaiRules
{
    public static new readonly CancunRules Instance = new();
    public override Fork Fork => Fork.Cancun;
    public override bool HasTloadTstore   => true;
    public override bool HasMcopy         => true;
    public override bool HasBlobHash      => true;
    public override bool HasEip4844BlobTx => true;
    public override bool HasEip6780SelfdestructRestriction => true; // EIP-6780
    public override int  PrecompileCount  => 10; // +KZG point eval (0x0A)
}

// ═══════════════════════════════════════════════════════════════════════════
//  Prague — EIP-7702: set-code tx, EIP-7623: calldata floor,
//           EIP-2537: BLS12-381 precompiles (+0x0B–0x13)
// ═══════════════════════════════════════════════════════════════════════════
public class PragueRules : CancunRules
{
    public static new readonly PragueRules Instance = new();
    public override Fork Fork => Fork.Prague;
    public override bool HasEip7702SetCode      => true;
    public override bool HasEip7623CalldataFloor => true;
    public override int  PrecompileCount         => 19; // +9 BLS12-381
}

// ═══════════════════════════════════════════════════════════════════════════
//  Osaka — placeholder; inherits all Prague rules
// ═══════════════════════════════════════════════════════════════════════════
public class OsakaRules : PragueRules
{
    public static new readonly OsakaRules Instance = new();
    public override Fork Fork => Fork.Osaka;
}

// ═══════════════════════════════════════════════════════════════════════════
//  Factory — string fork name (from fixture JSON) → IForkRules singleton
// ═══════════════════════════════════════════════════════════════════════════
public static class ForkRulesFactory
{
    public static IForkRules For(string forkName) => forkName switch
    {
        "Frontier"                        => FrontierRules.Instance,
        "Homestead"                       => HomesteadRules.Instance,
        "TangerineWhistle" or "EIP150"    => TangerineWhistleRules.Instance,
        "SpuriousDragon"   or "EIP158"    => SpuriousDragonRules.Instance,
        "Byzantium"                       => ByzantiumRules.Instance,
        "Constantinople" or
        "ConstantinopleFix"               => ConstantinopleRules.Instance,
        "Istanbul"                        => IstanbulRules.Instance,
        "Berlin"                          => BerlinRules.Instance,
        "London"                          => LondonRules.Instance,
        "Paris"          or "Merge"       => ParisRules.Instance,
        "Shanghai"                        => ShanghaiRules.Instance,
        "Cancun"                          => CancunRules.Instance,
        "Prague"                          => PragueRules.Instance,
        "Osaka"                           => OsakaRules.Instance,
        _                                 => PragueRules.Instance, // latest as safe default
    };

    public static IForkRules Latest => PragueRules.Instance;
}
