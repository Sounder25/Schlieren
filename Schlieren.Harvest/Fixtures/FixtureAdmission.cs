namespace Schlieren.Harvest.Fixtures;

/// <summary>
/// Reason a fixture case was admitted or rejected from a campaign manifest.
/// Per Task 5 spec — these codes are stable and must not be renamed.
/// </summary>
public enum AdmissionReasonCode
{
    Admitted,
    MissingRoot,
    OutsideRoot,
    MalformedJson,
    DuplicateCaseId,
    UnsupportedFormat,
    UnsupportedFork,
    MissingPreState,
    MissingPostState,
    MissingStatusAuthority,
    MissingGasAuthority,
    MissingLogsAuthority,
    AmbiguousVariant,
    ChecksumMismatch
}

/// <summary>
/// Immutable metadata record for one admitted (or rejected) fixture case.
///
/// Paths are persisted as slash-normalized relative paths from the catalog root.
/// The absolute path can be reconstructed from root + RelativePath.
/// </summary>
public sealed record FixtureCaseMetadata(
    string                   CaseId,
    string                   RelativePath,
    string                   SourceSha256,
    string                   Fork,
    IReadOnlySet<StorageDimension> Dimensions,
    AdmissionReasonCode      Admission,
    string?                  Detail);
