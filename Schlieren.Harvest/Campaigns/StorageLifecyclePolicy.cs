namespace Schlieren.Harvest.Fixtures;

/// <summary>
/// Storage-related dimensions that the storage-lifecycle campaign policy tracks.
/// Each admitted case is scored against these dimensions; the selector performs
/// a deterministic greedy set-cover to maximise dimension breadth.
/// </summary>
public enum StorageDimension
{
    // Opcode coverage
    Sload,
    Sstore,

    // Access cost tiers (EIP-2929)
    WarmAccess,
    ColdAccess,

    // Value transition families
    ZeroToNonzero,
    NonzeroToZero,
    NonzeroToNonzero,
    UnchangedWrite,
    RepeatedWrite,

    // Pre-populated storage
    NonZeroInitialStorage,

    // Frame geometry
    RootFrame,
    NestedFrame,

    // Call types with storage context
    CallWithStorage,
    StaticCallWithStorage,
    DelegateCallWithStorage,
    CallCodeWithStorage,

    // Commit/rollback semantics
    ChildCommit,
    ChildRollback,
    AncestorRollback,

    // Simulation
    SimulationDiscarded,

    // Gas refund
    GasRefund,

    // Fork-sensitive behaviour
    ForkSensitive
}
