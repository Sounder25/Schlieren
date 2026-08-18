using System;
using System.Collections.Generic;
using System.Linq;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Generates ~650 pathological EVM test cases across 7 failure families.
///
/// Invariant under test:
///   A legal EVM input must produce an EVM-defined result.
///   .NET exceptions, crashes, and hangs are defects.
///
/// Target allocation:
///   BigInteger/narrowing (memory offsets)  : ~100
///   Memory boundaries                      : ~100
///   Copy/returndata                        : ~100
///   Precompile pathological                : ~125
///   Exceptional halts                      : ~75
///   CREATE/CREATE2 lifecycle               : ~75
///   Stack/depth pressure                   : ~75
///   Arithmetic boundaries                  : ~(remainder)
/// </summary>
public static class PathologicalCaseGenerator
{
    private static int _serial = 0;
    private static readonly HashSet<string> _seen = new();
    private static readonly List<PathologicalCase> _out = new();

    // ── Public entry point ────────────────────────────────────────────────────

    public static List<PathologicalCase> Generate(string fork = "Cancun")
    {
        _serial = 0;
        _seen.Clear();
        _out.Clear();

        BigIntegerNarrowing(fork);
        MemoryBoundaries(fork);
        CopyReturndata(fork);
        PrecompilePathological(fork);
        ExceptionalHalts(fork);
        CreateLifecycle(fork);
        StackDepth(fork);
        ArithmeticBoundaries(fork);

        return _out.ToList();
    }

    // ── Family 1: BigInteger / narrowing (offset/size on the EVM stack → int/ulong) ──
    //
    // The EVM stack holds 256-bit words. Any opcode that interprets a word as
    // a memory offset, copy length, gas value, etc. must not throw when handed
    // pathological values. The allowed response is OOG or INVALID, never an
    // OverflowException / ArgumentOutOfRangeException.

    private static void BigIntegerNarrowing(string fork)
    {
        // Memory offset operands — MLOAD / MSTORE with values that overflow int/ulong
        foreach (var bv in OffsetBoundaries())
        {
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Mload,
                FailureFamily.OverflowMemoryOffset,
                $"MLOAD offset={bv}", boundary: bv);
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Mstore,
                FailureFamily.OverflowMemoryOffset,
                $"MSTORE offset={bv}", boundary: bv);
        }

        // RETURN / REVERT with huge offset or size
        foreach (var bv in OffsetBoundaries())
        {
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Return,
                FailureFamily.OverflowMemoryOffset,
                $"RETURN offset={bv}", boundary: bv, memKind: MemoryVariant.ReturnHugeOffset);
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Revert,
                FailureFamily.OverflowMemoryOffset,
                $"REVERT offset={bv}", boundary: bv, memKind: MemoryVariant.RevertHugeOffset);
        }

        // CALL input/output with huge offsets
        foreach (var bv in new[] { BoundaryValue.U32Max, BoundaryValue.U64Max, BoundaryValue.U256Max })
        {
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Call,
                FailureFamily.OverflowMemoryOffset,
                $"CALL argsOffset={bv}", boundary: bv, memKind: MemoryVariant.CallArgsHugeOffset);
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Call,
                FailureFamily.OverflowMemoryOffset,
                $"CALL retOffset={bv}", boundary: bv, memKind: MemoryVariant.CallRetHugeOffset);
        }

        // CALLDATACOPY / CODECOPY / EXTCODECOPY with huge dest
        foreach (var bv in new[] { BoundaryValue.U32Max, BoundaryValue.U64Max, BoundaryValue.U256Max })
        {
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Calldatacopy,
                FailureFamily.CopyRange,
                $"CALLDATACOPY destOffset={bv}", boundary: bv,
                copyKind: CopyVariant.HugeDestOffset, copySource: CopySource.Calldata);
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Codecopy,
                FailureFamily.CopyRange,
                $"CODECOPY destOffset={bv}", boundary: bv,
                copyKind: CopyVariant.HugeDestOffset, copySource: CopySource.Code);
        }

        // RETURNDATACOPY offset past return buffer
        foreach (var bv in new[] { BoundaryValue.One, BoundaryValue.Thirty_two, BoundaryValue.U32Max, BoundaryValue.U256Max })
        {
            Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Returndatacopy,
                FailureFamily.ReturndataRange,
                $"RETURNDATACOPY offset={bv} (returndata=0)", boundary: bv,
                copyKind: CopyVariant.HugeOffset, copySource: CopySource.Returndata);
        }

        // KECCAK256 with huge range
        Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Mload,
            FailureFamily.OverflowMemoryOffset,
            "KECCAK256 hugeRange", boundary: BoundaryValue.U64Max,
            memKind: MemoryVariant.KeccakHugeRange);

        // LOG with huge range
        Add(fork, PathFamily.BigIntegerNarrowing, PathOpcode.Mload,
            FailureFamily.OverflowMemoryOffset,
            "LOG hugeRange", boundary: BoundaryValue.U64Max,
            memKind: MemoryVariant.LogHugeRange);
    }

    // ── Family 2: Memory expansion boundaries ────────────────────────────────

    private static void MemoryBoundaries(string fork)
    {
        var offsets = new[]
        {
            BoundaryValue.Zero, BoundaryValue.Thirty_one, BoundaryValue.Thirty_two,
            BoundaryValue.Thirty_three, BoundaryValue.Two_fifty_five, BoundaryValue.Two_fifty_six,
            BoundaryValue.Two_fifty_seven, BoundaryValue.OneKm_3, BoundaryValue.OneKm,
            BoundaryValue.OneKm_1, BoundaryValue.TenK, BoundaryValue.SixtyFourK,
            BoundaryValue.OneMB, BoundaryValue.U32Max, BoundaryValue.U64Max, BoundaryValue.U256Max,
        };

        foreach (var off in offsets)
        {
            Add(fork, PathFamily.MemoryBoundary, PathOpcode.Mload,
                FailureFamily.OverflowMemoryOffset,
                $"MLOAD@{off}", boundary: off, memKind: MemoryVariant.MloadAtBoundary);
            Add(fork, PathFamily.MemoryBoundary, PathOpcode.Mstore,
                FailureFamily.OverflowMemoryOffset,
                $"MSTORE@{off}", boundary: off, memKind: MemoryVariant.MstoreAtBoundary);
        }

        // Near-U32 specifically (common narrowing point)
        foreach (var delta in new[] { -1L, 0L, 1L, 31L, 32L })
        {
            var val = (ulong)((long)0xFFFF_FFFFL + delta);
            Add(fork, PathFamily.MemoryBoundary, PathOpcode.Mload,
                FailureFamily.OverflowMemoryOffset,
                $"MLOAD nearU32+{delta}", boundary: BoundaryValue.U32Max, param1: val,
                memKind: MemoryVariant.MloadNearU32);
        }

        // CALL with huge retOffset: output buffer expansion
        foreach (var off in new[] { BoundaryValue.U32Max, BoundaryValue.U64Max, BoundaryValue.U256Max })
        {
            Add(fork, PathFamily.MemoryBoundary, PathOpcode.Call,
                FailureFamily.OverflowMemoryOffset,
                $"CALL retOffset={off}", boundary: off, memKind: MemoryVariant.CallRetHugeOffset);
        }
    }

    // ── Family 3: Copy / returndata ───────────────────────────────────────────

    private static void CopyReturndata(string fork)
    {
        var copySizes = new[]
        {
            CopyVariant.OffsetZero, CopyVariant.OffsetExactEnd, CopyVariant.OffsetOnePastEnd,
            CopyVariant.SizeZero, CopyVariant.SizeOneByte, CopyVariant.Size31,
            CopyVariant.Size32, CopyVariant.Size33, CopyVariant.Size255,
            CopyVariant.Size256, CopyVariant.Size257, CopyVariant.Size10k,
            CopyVariant.HugeOffset, CopyVariant.HugeSize, CopyVariant.OverflowOffsetPlusSize,
            CopyVariant.HugeDestOffset,
        };

        // CALLDATACOPY
        foreach (var cv in copySizes)
        {
            Add(fork, PathFamily.CopyReturndata, PathOpcode.Calldatacopy,
                FailureFamily.CopyRange, $"CALLDATACOPY {cv}",
                copyKind: cv, copySource: CopySource.Calldata);
        }

        // CODECOPY
        foreach (var cv in copySizes)
        {
            Add(fork, PathFamily.CopyReturndata, PathOpcode.Codecopy,
                FailureFamily.CopyRange, $"CODECOPY {cv}",
                copyKind: cv, copySource: CopySource.Code);
        }

        // RETURNDATACOPY — requires prior sub-call to populate buffer
        var rdcSizes = new[]
        {
            CopyVariant.SizeZero, CopyVariant.SizeOneByte, CopyVariant.Size31, CopyVariant.Size32,
            CopyVariant.Size33, CopyVariant.Size255, CopyVariant.Size256, CopyVariant.Size257,
            CopyVariant.Size10k, CopyVariant.OffsetOnePastEnd, CopyVariant.HugeOffset,
            CopyVariant.HugeSize, CopyVariant.OverflowOffsetPlusSize,
        };
        foreach (var cv in rdcSizes)
        {
            Add(fork, PathFamily.CopyReturndata, PathOpcode.Returndatacopy,
                FailureFamily.ReturndataRange, $"RETURNDATACOPY {cv}",
                copyKind: cv, copySource: CopySource.Returndata);
        }

        // Child returns N bytes; parent tries to copy exact / short / oversized
        foreach (var retBytes in new ulong[] { 0, 1, 31, 32, 33, 255, 256, 257, 10_240 })
        {
            Add(fork, PathFamily.CopyReturndata, PathOpcode.Returndatacopy,
                FailureFamily.ReturndataRange,
                $"RETURNDATACOPY child-returns-{retBytes}", param1: retBytes,
                copyKind: CopyVariant.OffsetExactEnd, copySource: CopySource.Returndata);
        }
    }

    // ── Family 4: Precompile pathological inputs ──────────────────────────────

    private static void PrecompilePathological(string fork)
    {
        // ModExp — the seed-zero case and systematic relatives
        foreach (var mv in Enum.GetValues<ModexpVariant>())
        {
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileModexp,
                FailureFamily.OverflowModexpGas, $"MODEXP {mv}", modexpKind: mv);
        }

        // BN254 (EcAdd / EcMul / EcPairing)
        foreach (var bv in Enum.GetValues<Bn254Variant>())
        {
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileEcadd,
                FailureFamily.PrecompileMalformed, $"ECADD {bv}", bn254Kind: bv);
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileEcmul,
                FailureFamily.PrecompileMalformed, $"ECMUL {bv}", bn254Kind: bv);
        }

        // EcPairing — malformed tuple lengths
        foreach (var inputLen in new ulong[] { 0, 63, 64, 65, 127, 128, 191, 192, 255, 256, 383, 384 })
        {
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileEcpairing,
                FailureFamily.PrecompileMalformed,
                $"ECPAIRING inputLen={inputLen}", param1: inputLen);
        }

        // Blake2F
        foreach (var bv in Enum.GetValues<Blake2fVariant>())
        {
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileBlake2f,
                FailureFamily.PrecompileMalformed, $"BLAKE2F {bv}", blake2fKind: bv);
        }

        // ecrecover malformed inputs
        foreach (var piv in Enum.GetValues<PrecompileInputVariant>())
        {
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileEcrecover,
                FailureFamily.PrecompileMalformed, $"ECRECOVER {piv}", precompileInput: piv);
        }

        // SHA256 / RIPEMD160 / Identity: empty + huge
        foreach (var piv in new[] { PrecompileInputVariant.Empty, PrecompileInputVariant.Oversized, PrecompileInputVariant.AllZero })
        {
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileSha256,
                FailureFamily.PrecompileMalformed, $"SHA256 {piv}", precompileInput: piv);
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileRipemd160,
                FailureFamily.PrecompileMalformed, $"RIPEMD160 {piv}", precompileInput: piv);
            Add(fork, PathFamily.PrecompilePathological, PathOpcode.PrecompileIdentity,
                FailureFamily.PrecompileMalformed, $"IDENTITY {piv}", precompileInput: piv);
        }

        // Precompile called with 0 gas
        foreach (var addr in new[]
            {
                PathOpcode.PrecompileEcrecover, PathOpcode.PrecompileSha256,
                PathOpcode.PrecompileModexp, PathOpcode.PrecompileBlake2f
            })
        {
            Add(fork, PathFamily.PrecompilePathological, addr,
                FailureFamily.PrecompileMalformed, $"CALL precompile 0-gas {addr}",
                param1: 0);
        }
    }

    // ── Family 5: Exceptional halts ───────────────────────────────────────────

    private static void ExceptionalHalts(string fork)
    {
        foreach (var hk in Enum.GetValues<ExceptionalHaltKind>())
        {
            Add(fork, PathFamily.ExceptionalHalt, HaltOpcode(hk),
                FailureFamily.ExceptionalHalt, $"ExceptionalHalt {hk}", haltKind: hk);
        }

        // OOG at each opcode class: memory expansion OOG
        foreach (var bv in new[] { BoundaryValue.OneMB, BoundaryValue.U32Max })
        {
            Add(fork, PathFamily.ExceptionalHalt, PathOpcode.Mstore,
                FailureFamily.ExceptionalHalt,
                $"OOG-memory-expansion MSTORE@{bv}", haltKind: ExceptionalHaltKind.OutOfGas,
                boundary: bv, memKind: MemoryVariant.MstoreAtBoundary);
        }

        // Stack exactly at limit — verify no crash on the boundary itself
        Add(fork, PathFamily.ExceptionalHalt, PathOpcode.Push1,
            FailureFamily.StackLimit, "Stack exactly-1024 then PUSH overflow",
            haltKind: ExceptionalHaltKind.StackOverflow, stackKind: StackVariant.Push1024Items);

        // JUMP to non-JUMPDEST (various targets)
        foreach (var target in new ulong[] { 0, 1, 100, 0xFFFF })
        {
            Add(fork, PathFamily.ExceptionalHalt, PathOpcode.JumpBad,
                FailureFamily.ExceptionalHalt, $"BAD-JUMP to {target}",
                haltKind: ExceptionalHaltKind.BadJumpDestination, param1: target);
        }

        // STATICCALL → SSTORE (static context violation)
        Add(fork, PathFamily.ExceptionalHalt, PathOpcode.Staticcall,
            FailureFamily.ExceptionalHalt, "STATICCALL inner SSTORE",
            haltKind: ExceptionalHaltKind.StaticContextMutation);

        // STATICCALL → LOG0 (also forbidden)
        Add(fork, PathFamily.ExceptionalHalt, PathOpcode.Staticcall,
            FailureFamily.ExceptionalHalt, "STATICCALL inner LOG0",
            haltKind: ExceptionalHaltKind.StaticContextMutation, param1: 0);

        // Depth limit: exactly 1024
        Add(fork, PathFamily.ExceptionalHalt, PathOpcode.Call,
            FailureFamily.DepthLimit, "CALL depth exactly 1024",
            haltKind: ExceptionalHaltKind.DepthLimitExceeded,
            stackKind: StackVariant.NestedCallDepth1024);
    }

    // ── Family 6: CREATE / CREATE2 lifecycle ──────────────────────────────────

    private static void CreateLifecycle(string fork)
    {
        foreach (var cv in Enum.GetValues<CreateVariant>())
        {
            Add(fork, PathFamily.CreateLifecycle, PathOpcode.Create,
                FailureFamily.CreateLifecycle, $"CREATE {cv}", createKind: cv);
        }

        // CREATE2 salt boundary
        foreach (var saltBv in new[] { BoundaryValue.Zero, BoundaryValue.U256Max, BoundaryValue.U255 })
        {
            Add(fork, PathFamily.CreateLifecycle, PathOpcode.Create2,
                FailureFamily.CreateLifecycle, $"CREATE2 salt={saltBv}", boundary: saltBv,
                createKind: CreateVariant.Create2HugeSalt);
        }

        // CREATE with initcode size exactly at / around EIP-3860 limit (49152)
        foreach (var sz in new ulong[] { 0, 1, 49151, 49152, 49153, 65536 })
        {
            Add(fork, PathFamily.CreateLifecycle, PathOpcode.Create,
                FailureFamily.CreateLifecycle, $"CREATE initcode-size={sz}", param1: sz,
                createKind: CreateVariant.HugeInitcodeSize);
        }

        // RETURN from initcode with runtime size near EIP-170 limit (24576)
        foreach (var sz in new ulong[] { 0, 1, 24575, 24576, 24577, 65536 })
        {
            Add(fork, PathFamily.CreateLifecycle, PathOpcode.Create,
                FailureFamily.CreateLifecycle, $"CREATE runtime-size={sz}", param1: sz,
                createKind: CreateVariant.ReturnHugeRuntimeCode);
        }
    }

    // ── Family 7: Stack / depth pressure ─────────────────────────────────────

    private static void StackDepth(string fork)
    {
        foreach (var sv in Enum.GetValues<StackVariant>())
        {
            Add(fork, PathFamily.StackDepth,
                sv is StackVariant.NestedCallDepth1023
                    or StackVariant.NestedCallDepth1024
                    or StackVariant.NestedCallDepth1025
                    or StackVariant.DeepCreateChain
                    or StackVariant.DeepRevertUnwind
                    ? PathOpcode.Call : PathOpcode.Push1,
                sv is StackVariant.DeepCreateChain ? FailureFamily.DepthLimit : FailureFamily.StackLimit,
                $"Stack {sv}", stackKind: sv);
        }

        // Dup variants at stack 1023 (should succeed) vs 1024 (overflow on DUP)
        foreach (var depth in new[] { 1021, 1022, 1023, 1024 })
        {
            Add(fork, PathFamily.StackDepth, PathOpcode.Dup,
                FailureFamily.StackLimit, $"DUP at depth={depth}", param1: (ulong)depth);
        }

        // SWAP variants
        foreach (var depth in new[] { 1021, 1022, 1023, 1024 })
        {
            Add(fork, PathFamily.StackDepth, PathOpcode.Swap,
                FailureFamily.StackLimit, $"SWAP at depth={depth}", param1: (ulong)depth);
        }
    }

    // ── Family 8: Arithmetic boundaries ──────────────────────────────────────

    private static void ArithmeticBoundaries(string fork)
    {
        foreach (var av in Enum.GetValues<ArithVariant>())
        {
            Add(fork, PathFamily.ArithmeticBoundary, ArithOpcode(av),
                FailureFamily.ArithmeticBoundary, $"ARITH {av}", arithKind: av);
        }

        // 0 / 1 / 2^255-1 / 2^255 / 2^256-1 combinations for ADD/SUB/MUL
        foreach (var bv in new[] { BoundaryValue.Zero, BoundaryValue.One, BoundaryValue.U255, BoundaryValue.U256Max })
        {
            Add(fork, PathFamily.ArithmeticBoundary, PathOpcode.Add,
                FailureFamily.ArithmeticBoundary, $"ADD {bv}+{bv}", boundary: bv,
                arithKind: ArithVariant.AddOverflow256);
            Add(fork, PathFamily.ArithmeticBoundary, PathOpcode.Sub,
                FailureFamily.ArithmeticBoundary, $"SUB {bv}-{bv}", boundary: bv,
                arithKind: ArithVariant.SubWrap);
            Add(fork, PathFamily.ArithmeticBoundary, PathOpcode.Mul,
                FailureFamily.ArithmeticBoundary, $"MUL {bv}×{bv}", boundary: bv,
                arithKind: ArithVariant.MulWrap);
        }
    }

    // ── Internal helper: Add ──────────────────────────────────────────────────

    private static void Add(
        string fork,
        PathFamily family,
        PathOpcode opcode,
        string familyId,
        string label,
        BoundaryValue?          boundary       = null,
        ModexpVariant?          modexpKind     = null,
        Bn254Variant?           bn254Kind      = null,
        Blake2fVariant?         blake2fKind    = null,
        PrecompileInputVariant? precompileInput = null,
        ExceptionalHaltKind?    haltKind       = null,
        CreateVariant?          createKind     = null,
        CopyVariant?            copyKind       = null,
        CopySource?             copySource     = null,
        MemoryVariant?          memKind        = null,
        ArithVariant?           arithKind      = null,
        StackVariant?           stackKind      = null,
        ulong?                  param1         = null,
        ulong?                  param2         = null)
    {
        var c = new PathologicalCase
        {
            CaseId       = $"PATH-{++_serial:D4}",
            Fork         = fork,
            Family       = family,
            Opcode       = opcode,
            Label        = label,
            FamilyId     = familyId,
            Boundary     = boundary,
            ModexpKind   = modexpKind,
            Bn254Kind    = bn254Kind,
            Blake2fKind  = blake2fKind,
            PrecompileInput = precompileInput,
            HaltKind     = haltKind,
            CreateKind   = createKind,
            CopyKind     = copyKind,
            CopySource   = copySource,
            MemoryKind   = memKind,
            ArithKind    = arithKind,
            StackKind    = stackKind,
            Param1       = param1,
            Param2       = param2,
        };

        if (_seen.Add(c.Fingerprint()))
            _out.Add(c);
    }

    // ── Dimension helpers ─────────────────────────────────────────────────────

    /// <summary>Standard set of memory offset boundary values.</summary>
    private static BoundaryValue[] OffsetBoundaries() => new[]
    {
        BoundaryValue.Zero, BoundaryValue.Thirty_one, BoundaryValue.Thirty_two,
        BoundaryValue.Thirty_three, BoundaryValue.Two_fifty_five, BoundaryValue.Two_fifty_six,
        BoundaryValue.OneKm, BoundaryValue.TenK, BoundaryValue.SixtyFourK, BoundaryValue.OneMB,
        BoundaryValue.U32Max, BoundaryValue.U64Max,
        BoundaryValue.U255, BoundaryValue.U256Max, BoundaryValue.OffsetPlusLengthOverflow,
    };

    private static PathOpcode HaltOpcode(ExceptionalHaltKind hk) => hk switch
    {
        ExceptionalHaltKind.OutOfGas                => PathOpcode.Mstore,
        ExceptionalHaltKind.InvalidOpcode           => PathOpcode.InvalidOpcode,
        ExceptionalHaltKind.StackUnderflow          => PathOpcode.Mload,
        ExceptionalHaltKind.StackOverflow           => PathOpcode.Push1,
        ExceptionalHaltKind.BadJumpDestination      => PathOpcode.JumpBad,
        ExceptionalHaltKind.ReturndataCopyOob       => PathOpcode.Returndatacopy,
        ExceptionalHaltKind.StaticContextMutation   => PathOpcode.Staticcall,
        ExceptionalHaltKind.DepthLimitExceeded      => PathOpcode.Call,
        _                                            => PathOpcode.InvalidOpcode,
    };

    private static PathOpcode ArithOpcode(ArithVariant av) => av switch
    {
        ArithVariant.AddWrap or ArithVariant.AddOverflow256 => PathOpcode.Add,
        ArithVariant.SubWrap                                => PathOpcode.Sub,
        ArithVariant.MulWrap                                => PathOpcode.Mul,
        ArithVariant.DivByZero                              => PathOpcode.Div,
        ArithVariant.ModByZero                              => PathOpcode.Mod,
        ArithVariant.SdivByZero or ArithVariant.SdivNegativeOverflow => PathOpcode.Sdiv,
        ArithVariant.ModNeg                                 => PathOpcode.Smod,
        ArithVariant.SarOnMaxSigned or ArithVariant.SarOnMinSigned => PathOpcode.Sar,
        ArithVariant.ShlByMax                               => PathOpcode.Shl,
        ArithVariant.ShrByMax                               => PathOpcode.Shr,
        ArithVariant.ExpByZero or ArithVariant.ExpZeroBase
            or ArithVariant.ExpLargeBase                    => PathOpcode.Exp,
        ArithVariant.SignedCmpMaxMin                        => PathOpcode.Sar,  // uses SAR context
        _                                                    => PathOpcode.Add,
    };
}
