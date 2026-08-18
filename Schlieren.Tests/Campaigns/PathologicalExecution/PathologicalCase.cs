using System;
using System.Collections.Generic;
using System.Numerics;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

// ── Failure family identifiers ────────────────────────────────────────────────
// One family per orthogonal failure surface.
// These label *expected failure modes*, not observed defects.

public static class FailureFamily
{
    public const string OverflowModexpGas   = "FAM-OVERFLOW-MODEXP-GAS";
    public const string OverflowMemoryOffset = "FAM-OVERFLOW-MEMORY-OFFSET";
    public const string CopyRange           = "FAM-COPY-RANGE";
    public const string StackLimit          = "FAM-STACK-LIMIT";
    public const string DepthLimit          = "FAM-DEPTH-LIMIT";
    public const string PrecompileMalformed = "FAM-PRECOMPILE-MALFORMED";
    public const string CreateLifecycle     = "FAM-CREATE-LIFECYCLE";
    public const string ReturndataRange     = "FAM-RETURNDATA-RANGE";
    public const string UnhandledEngineException = "FAM-UNHANDLED-ENGINE-EXCEPTION";
    public const string ArithmeticBoundary  = "FAM-ARITHMETIC-BOUNDARY";
    public const string ExceptionalHalt     = "FAM-EXCEPTIONAL-HALT";
}

// ── Dimension enums ───────────────────────────────────────────────────────────

/// <summary>Top-level category matching one of the ~650-case families.</summary>
public enum PathFamily
{
    BigIntegerNarrowing,   // FAM-OVERFLOW-MEMORY-OFFSET / FAM-OVERFLOW-MODEXP-GAS
    MemoryBoundary,        // FAM-OVERFLOW-MEMORY-OFFSET
    CopyReturndata,        // FAM-COPY-RANGE / FAM-RETURNDATA-RANGE
    PrecompilePathological,// FAM-PRECOMPILE-MALFORMED
    ExceptionalHalt,       // FAM-EXCEPTIONAL-HALT
    CreateLifecycle,       // FAM-CREATE-LIFECYCLE
    StackDepth,            // FAM-STACK-LIMIT / FAM-DEPTH-LIMIT
    ArithmeticBoundary,    // FAM-ARITHMETIC-BOUNDARY
}

/// <summary>The opcode (group) being exercised.</summary>
public enum PathOpcode
{
    // Memory / copy
    Mload, Mstore, Mstore8,
    Calldatacopy, Codecopy, Extcodecopy, Returndatacopy,
    // Control
    Return, Revert,
    // Call variants
    Call, Staticcall, Delegatecall, Callcode,
    // Create
    Create, Create2,
    // Stack
    Push1, Dup, Swap,
    // Arithmetic
    Add, Sub, Mul, Div, Sdiv, Mod, Smod, Exp,
    Sar, Shr, Shl,
    // Precompile dispatch (opcode = CALL to 0x01..0x09)
    PrecompileEcrecover,
    PrecompileSha256,
    PrecompileRipemd160,
    PrecompileIdentity,
    PrecompileModexp,
    PrecompileEcadd,
    PrecompileEcmul,
    PrecompileEcpairing,
    PrecompileBlake2f,
    // Exceptional
    InvalidOpcode, JumpBad,
    // Selfdestruct
    Selfdestruct,
}

/// <summary>Numeric boundary class pushed onto the stack.</summary>
public enum BoundaryValue : ulong
{
    Zero            = 0,
    One             = 1,
    Thirty_one      = 31,
    Thirty_two      = 32,
    Thirty_three    = 33,
    Two_fifty_five  = 255,
    Two_fifty_six   = 256,
    Two_fifty_seven = 257,
    OneKm_3         = 1023,
    OneKm            = 1024,
    OneKm_1         = 1025,
    TenK            = 10_240,
    SixtyFourK      = 65_536,
    OneMB           = 1_048_576,
    U32Max          = 0xFFFF_FFFF,   // 2^32-1
    // Note: U64Max, U255, U256Max emitted as BigInteger strings
    U64Max_Minus1   = 0xFFFF_FFFF_FFFF_FFFE,
    U64Max          = 0xFFFF_FFFF_FFFF_FFFF,
    // Symbolic sentinel values resolved to BigInteger in materializer:
    U255            = unchecked((ulong)-2),   // sentinel → 2^255-1
    U256Max         = unchecked((ulong)-1),   // sentinel → 2^256-1
    OffsetPlusLengthOverflow = unchecked((ulong)-3), // sentinel → 2^256-32
}

// ── Precompile sub-case kinds ─────────────────────────────────────────────────

public enum ModexpVariant
{
    Normal,
    ZeroLengths,            // bLen=0, eLen=0, mLen=0
    HugeDeclaredBase,       // bLen=2^64-1, eLen=1, mLen=1 (gas overflow)
    HugeDeclaredExp,        // bLen=1, eLen=2^64-1, mLen=1
    HugeDeclaredMod,        // bLen=1, eLen=1, mLen=2^64-1
    AllHuge,                // all three huge
    TruncatedInput,         // declared lengths exceed actual input bytes
    ZeroModulus,            // mLen=1 but modulus=0
}

public enum Bn254Variant
{
    ValidPoint,
    ZeroPoint,
    PointNotOnCurve,
    PointInfinityFirst,
    WrongInputLength,       // e.g. 127 bytes instead of 128
    AllZeroInput,
}

public enum Blake2fVariant
{
    ValidInput,
    InvalidFinalFlag,       // flag not 0 or 1
    WrongInputLength,       // ≠ 213 bytes
    AllZeroInput,
}

public enum PrecompileInputVariant
{
    Empty,
    OneByteShort,
    Exact,
    Oversized,
    AllZero,
    HighBitSet,
}

// ── Exceptional halt sub-cases ────────────────────────────────────────────────

public enum ExceptionalHaltKind
{
    OutOfGas,               // OOG during normal execution
    InvalidOpcode,          // 0xFE (INVALID)
    StackUnderflow,         // pop from empty stack
    StackOverflow,          // push beyond 1024
    BadJumpDestination,     // JUMP to non-JUMPDEST
    ReturndataCopyOob,      // RETURNDATACOPY with offset > returndata buffer
    StaticContextMutation,  // SSTORE inside STATICCALL
    DepthLimitExceeded,     // call chain exactly at 1024
}

// ── CREATE lifecycle sub-cases ────────────────────────────────────────────────

public enum CreateVariant
{
    NormalSmall,
    HugeInitcodeOffset,         // initcode at near-max memory offset
    HugeInitcodeSize,           // initcode size claim > available gas
    OogDuringInitcode,          // initcode executes just past available gas
    ReturnHugeRuntimeCode,      // initcode RETURNs >24576 bytes (EIP-170)
    CreateNested,               // initcode itself calls CREATE
    Create2HugeSalt,            // CREATE2 with 2^256-1 salt (hash still defined)
    Create2HugeOffset,          // CREATE2 with near-overflow offset
    RevertedCreate,             // initcode REVERTs — caller should see 0 on stack
    NonceRollover,              // sender nonce at 2^64-1 (create fails)
}

// ── Copy / returndata sub-cases ───────────────────────────────────────────────

public enum CopyVariant
{
    OffsetZero,
    OffsetExactEnd,
    OffsetOnePastEnd,
    SizeZero,
    SizeOneByte,
    Size31,
    Size32,
    Size33,
    Size255,
    Size256,
    Size257,
    Size10k,
    HugeOffset,                 // offset near 2^32
    HugeSize,                   // size near 2^32
    OverflowOffsetPlusSize,     // offset+size wraps
    HugeDestOffset,             // destination in memory at huge offset
}

public enum CopySource
{
    Calldata,
    Code,
    Returndata,                 // RETURNDATACOPY — requires a prior sub-call
    ExtcodeOf,                  // EXTCODECOPY of a known contract
}

// ── Memory boundary sub-cases ─────────────────────────────────────────────────

public enum MemoryVariant
{
    MloadAtBoundary,
    MstoreAtBoundary,
    MloadHighOffset,
    MstoreHighOffset,
    MloadNearU32,
    MstoreNearU32,
    ReturnHugeOffset,
    RevertHugeOffset,
    CallArgsHugeOffset,
    CallRetHugeOffset,
    KeccakHugeRange,
    LogHugeRange,
}

// ── Arithmetic boundary sub-cases ────────────────────────────────────────────

public enum ArithVariant
{
    AddWrap,
    SubWrap,
    MulWrap,
    AddOverflow256,
    DivByZero,
    ModByZero,
    SdivByZero,
    SdivNegativeOverflow,   // (2^255) / (-1)
    ModNeg,
    SarOnMaxSigned,         // SAR(255, 2^255-1) = -1
    SarOnMinSigned,         // SAR(1, 2^255) extends sign
    ShlByMax,               // SHL(256+, x) = 0
    ShrByMax,               // SHR(256+, x) = 0
    ExpByZero,              // x**0 = 1
    ExpZeroBase,            // 0**y (y>0) = 0
    ExpLargeBase,           // (2^256-1)**2 = wrap
    SignedCmpMaxMin,        // SGT/SLT at ±boundary
}

// ── Stack / depth sub-cases ───────────────────────────────────────────────────

public enum StackVariant
{
    Push1022Items,          // stack at 1022 — next pushes still valid
    Push1023Items,          // stack at 1023
    Push1024Items,          // stack at 1024 — PUSH would overflow
    Push1025Items,          // explicit overflow
    NestedCallDepth1023,    // depth-1 call chain → should succeed
    NestedCallDepth1024,    // depth 1024 hit → CALL returns 0
    NestedCallDepth1025,    // same: 1024 enforcement is inclusive
    DeepCreateChain,        // CREATE inside CREATE 50× deep
    DeepRevertUnwind,       // 100-deep revert unwind
}

// ── The main case record ──────────────────────────────────────────────────────

/// <summary>
/// One pathological test case.
/// Deterministic from its fields — no randomness.
/// </summary>
public sealed record PathologicalCase
{
    public required string CaseId  { get; init; }
    public required string Fork    { get; init; }
    public required PathFamily  Family   { get; init; }
    public required PathOpcode  Opcode   { get; init; }
    public required string      Label    { get; init; }   // human description
    public required string      FamilyId { get; init; }   // FAM-* constant

    // Optional sub-kind fields — set to null when not applicable
    public BoundaryValue?   Boundary    { get; init; }
    public ModexpVariant?   ModexpKind  { get; init; }
    public Bn254Variant?    Bn254Kind   { get; init; }
    public Blake2fVariant?  Blake2fKind { get; init; }
    public PrecompileInputVariant? PrecompileInput { get; init; }
    public ExceptionalHaltKind?    HaltKind   { get; init; }
    public CreateVariant?          CreateKind { get; init; }
    public CopyVariant?            CopyKind   { get; init; }
    public CopySource?             CopySource { get; init; }
    public MemoryVariant?          MemoryKind { get; init; }
    public ArithVariant?           ArithKind  { get; init; }
    public StackVariant?           StackKind  { get; init; }

    // Extra integer parameters materializer may use
    public ulong?  Param1 { get; init; }
    public ulong?  Param2 { get; init; }

    /// <summary>Canonical fingerprint for deduplication.</summary>
    public string Fingerprint() =>
        $"{Fork}|{Family}|{Opcode}|{Boundary}|{ModexpKind}|{Bn254Kind}" +
        $"|{Blake2fKind}|{HaltKind}|{CreateKind}|{CopyKind}|{CopySource}" +
        $"|{MemoryKind}|{ArithKind}|{StackKind}|{Param1}|{Param2}";
}

// ── Allowed terminal outcomes ─────────────────────────────────────────────────

/// <summary>
/// The fundamental invariant of the pathological suite:
/// an EVM result must be one of these EVM-defined outcomes.
/// Any .NET exception is a defect.
/// </summary>
public enum PathologicalOutcome
{
    Success,
    Revert,
    OutOfGas,
    Invalid,
    StackUnderflow,
    StackOverflow,
    InvalidJumpDest,
    ReturndataOob,
    StaticContextViolation,
    DepthLimitReached,
    // Defect outcomes — should NEVER appear
    DotNetException,
    Hang,
    ProcessCrash,
}

/// <summary>Single execution result from the pathological runner.</summary>
public sealed record PathologicalResult
{
    public required PathologicalCase   Case         { get; init; }
    public required PathologicalOutcome Outcome     { get; init; }
    public required bool               IsDefect     { get; init; }

    // Set only on defect
    public string?   ExceptionType    { get; init; }
    public string?   ExceptionMessage { get; init; }
    public string?   StackTrace       { get; init; }

    // Raw engine output (null on crash)
    public bool?     EngineSuccess    { get; init; }
    public ulong?    GasUsed          { get; init; }
    public string?   ReturnData       { get; init; }
}

/// <summary>A cluster of structurally-similar defects.</summary>
public sealed record PathologicalCluster
{
    public required string   FamilyId  { get; init; }
    public required int      Count     { get; init; }
    public required string   ExceptionType { get; init; }
    public required IReadOnlyList<PathologicalResult> Cases { get; init; }
}

/// <summary>Summary of a full pathological campaign run.</summary>
public sealed record PathologicalCampaignResult
{
    public required int Total   { get; init; }
    public required int Passed  { get; init; }   // EVM-defined outcome, not .NET exception
    public required int Defects { get; init; }   // .NET exception or crash
    public required IReadOnlyList<PathologicalResult>  AllResults { get; init; }
    public required IReadOnlyList<PathologicalCluster> Clusters   { get; init; }
}
