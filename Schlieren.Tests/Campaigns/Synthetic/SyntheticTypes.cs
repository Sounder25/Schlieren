using System;
using System.Collections.Generic;
using System.Numerics;

namespace Schlieren.Tests.Campaigns.Synthetic;

// ── Semantic dimensions ──────────────────────────────────────────────────────

public enum CallKind        { Call, StaticCall, DelegateCall, CallCode }
public enum TargetKind      { ExistingCode, EmptyAccount, Nonexistent, Precompile, Self }
public enum ChildBehavior   { Stop, Return, Revert, SStore, SStoreRevert,
                              Log, LogRevert, SelfDestruct, NestedCall, OutOfGas, InvalidOpcode }
public enum GasClass        { Minimal, BelowStipend, Stipend, AboveStipend,
                              ExactMinus1, Exact, ExactPlus1, Boundary6364, High }
public enum ValueClass      { Zero, One, Byte255, Byte256, OneEther, InsufficientBalance }
public enum ReturnSize      { Zero, One, Byte31, Byte32, Byte33, Byte255, Byte256, Byte257 }
public enum StoragePattern  { None, ZeroToX, XToY, XToZero, MultiSlot, SameSlotTwice }
public enum RevertMode      { None, ExplicitRevert, OutOfGas, InvalidOpcode }

// ── Case record ──────────────────────────────────────────────────────────────

/// <summary>
/// Semantic identity of one synthetic test case.
/// Deterministic, hashable, serializable. Everything needed to reproduce an execution.
/// </summary>
public sealed record SyntheticCase
{
    public required string CaseId  { get; init; }
    public required string Fork    { get; init; }

    public CallKind       CallKind       { get; init; }
    public TargetKind     TargetKind     { get; init; }
    public ChildBehavior  ChildBehavior  { get; init; }

    public GasClass       GasClass       { get; init; }
    public ValueClass     ValueClass     { get; init; }
    public ReturnSize     ReturnSize     { get; init; }

    public int            Depth          { get; init; }

    public StoragePattern StoragePattern { get; init; }
    public RevertMode     RevertMode     { get; init; }

    public bool           WarmTarget     { get; init; }
    public bool           WarmStorage    { get; init; }

    public int            Seed           { get; init; }

    /// <summary>
    /// Canonical fingerprint for deduplication. Based purely on execution semantics —
    /// two cases with the same fingerprint produce identical bytecode and prestate.
    /// </summary>
    public string CanonicalFingerprint() =>
        $"{Fork}|{CallKind}|{TargetKind}|{ChildBehavior}|{GasClass}|{ValueClass}" +
        $"|{ReturnSize}|{Depth}|{StoragePattern}|{RevertMode}" +
        $"|W{(WarmTarget ? 1 : 0)}{(WarmStorage ? 1 : 0)}";
}

// ── Failure types ─────────────────────────────────────────────────────────────

/// <summary>
/// Coarse signature — deliberately excludes case-specific dimensions (exact gas, exact depth,
/// exact value, fork) from the grouping key. Those belong in cluster metadata.
/// </summary>
public sealed record FailureSignature(
    string   Category,             // e.g. "Call-SStore"
    string   DifferenceKind,       // e.g. "StateMismatch"
    string?  FirstDivergentOpcode, // e.g. "SSTORE"
    string?  FrameType,            // e.g. "Call"
    bool     SuccessMismatch,
    bool     StateMismatch,
    bool     GasMismatch,
    bool     ReturnDataMismatch,
    bool     LogsMismatch)
{
    public static FailureSignature Infrastructure(SyntheticCase c, Exception ex) =>
        new($"{c.CallKind}-{c.ChildBehavior}", "Exception",
            null, null, false, false, false, false, false);
}

/// <summary>Full record of one divergence.</summary>
public sealed record SyntheticFailureRecord
{
    public required SyntheticCase             Case          { get; init; }
    public          CampaignExecutionRequest? Request       { get; init; }
    public          CampaignExecutionResult?  Schlieren     { get; init; }
    public          CampaignExecutionResult?  Oracle        { get; init; }
    public          ExecutionComparator.ExecutionDiff? ExecutionDiff { get; init; }
    public          DivergenceAnalyzer.Divergence?     Diff          { get; init; }
    public required FailureSignature          Signature     { get; init; }
    public          string?                   Exception     { get; init; }
}

/// <summary>One cluster of structurally-similar failures.</summary>
public sealed record FailureCluster
{
    public required string                        FamilyId       { get; init; }
    public required int                           Count          { get; init; }
    public required FailureSignature              Signature      { get; init; }
    public required IReadOnlyList<SyntheticFailureRecord> Cases  { get; init; }
    public required string[]                      Forks          { get; init; }
    public required int[]                         Depths         { get; init; }
    public required CallKind[]                    CallKinds      { get; init; }
    public required ChildBehavior[]               ChildBehaviors { get; init; }
}

/// <summary>Complete result from one campaign run.</summary>
public sealed record SyntheticCampaignResult
{
    public required int                                   Total                    { get; init; }
    public required int                                   Passed                   { get; init; }
    public required int                                   InvariantFailureCount    { get; init; }
    public required int                                   DifferentialFailureCount { get; init; }
    public required IReadOnlyList<SyntheticFailureRecord> Failures                 { get; init; }
    public required IReadOnlyList<FailureCluster>         Clusters                 { get; init; }

    public int Failed                => InvariantFailureCount + DifferentialFailureCount;
    public int UniqueFailureFamilies => Clusters.Count;
}
