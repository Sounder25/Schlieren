using System.Security.Cryptography;
using System.Text;
using Schlieren.Harvest.Fixtures;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Campaigns;

/// <summary>One case entry frozen into a campaign manifest.</summary>
public sealed record ManifestCase(
    string                         CaseId,
    string                         RelativePath,
    string                         SourceSha256,
    string                         Fork,
    IReadOnlyList<StorageDimension> Dimensions);

/// <summary>
/// Identity record for the EELS executable and revision used during a run.
/// Included in the manifest so every certificate binds to a specific oracle.
/// Present in both v1 and v2 manifests.
/// </summary>
public sealed record EelsIdentity(
    string ExecutableSha256,
    string ReportedVersion,
    string? CommitSha);

/// <summary>
/// Immutable campaign manifest. Once frozen it must not be mutated.
/// Replacing or reclassifying a case requires a new campaign version and manifest.
///
/// Schema v1: thin EelsIdentity only, no semantic provenance.
/// Schema v2: adds EelsProvenance (full semantic identity) and SemanticIdentityHash.
///            The v2 canonical hash includes the provenance block.
///
/// <see cref="ManifestHash"/> is the SHA-256 fingerprint of the manifest content
/// (computed via <see cref="Freeze"/>), excluded from the hashed bytes.
/// </summary>
public sealed record CampaignManifest(
    string                      SchemaVersion,
    string                      CampaignId,
    string                      CampaignVersion,
    string                      FamilyName,
    int                         BatchSize,
    DateTime                    CreatedUtc,
    string                      SelectionPolicyVersion,
    EelsIdentity?               EelsIdentity,
    Configuration.EelsSemanticIdentity? EelsProvenance,
    string?                     SemanticIdentityHash,
    string?                     FixtureRootSha256,
    IReadOnlyList<string>       RequiredComparisonFields,
    string                      ToolVersion,
    IReadOnlyList<ManifestCase> Cases,
    string                      ManifestHash)
{
    public const string SchemaV1                        = "1";
    public const string SchemaV2                        = "2";
    public const string CurrentSelectionPolicyVersion = "storage-lifecycle-v1";
    public const string CurrentFamilyName             = "storage-lifecycle";
    public const string CurrentToolVersion            = "schlieren-harvest-1";

    public static readonly IReadOnlyList<string> StorageLifecycleComparisonFields =
        new[] { "status", "gasUsed", "logs", "postState.storage" };

    /// <summary>
    /// Freezes an ordered list of admitted cases into an immutable manifest.
    ///
    /// Schema selection:
    ///   - If <paramref name="eelsProvenance"/> is supplied → schema v2.
    ///   - Otherwise → schema v1 (backward-compatible, no provenance block).
    ///
    /// <paramref name="eelsIdentity"/> is required for both v1 and v2. For v2,
    /// the thin identity must be consistent with the semantic provenance — this
    /// is validated here at freeze time.
    /// </summary>
    public static CampaignManifest Freeze(
        IReadOnlyList<FixtureCaseMetadata> cases,
        string campaignId,
        DateTime createdUtc,
        string campaignVersion = "1",
        EelsIdentity? eelsIdentity = null,
        string? fixtureRootSha256 = null,
        bool allowNullIdentity = false,
        string? familyName = null,
        IReadOnlyList<string>? comparisonFields = null,
        Configuration.EelsSemanticIdentity? eelsProvenance = null)
    {
        if (eelsIdentity is null && !allowNullIdentity)
            throw new InvalidOperationException(
                "EelsIdentity is required to freeze a manifest. " +
                "If this is a test-only manifest that intentionally omits the oracle identity, " +
                "pass allowNullIdentity: true explicitly.");

        // v2 consistency: thin identity must bind to semantic provenance
        if (eelsProvenance is not null && eelsIdentity is not null)
        {
            if (!eelsProvenance.BindsTo(eelsIdentity))
                throw new InvalidOperationException(
                    $"EelsIdentity (version={eelsIdentity.ReportedVersion}, " +
                    $"launcher={eelsIdentity.ExecutableSha256[..Math.Min(12, eelsIdentity.ExecutableSha256.Length)]}...) " +
                    $"does not match EelsProvenance (version={eelsProvenance.PackageVersion}, " +
                    $"launcher={eelsProvenance.LauncherSha256[..Math.Min(12, eelsProvenance.LauncherSha256.Length)]}...). " +
                    "The thin identity must be derived from the semantic provenance.");
        }

        var isV2 = eelsProvenance is not null;
        var schemaVersion = isV2 ? SchemaV2 : SchemaV1;

        var manifestCases = cases.Select(c => new ManifestCase(
            CaseId:       c.CaseId,
            RelativePath: c.RelativePath,
            SourceSha256: c.SourceSha256,
            Fork:         c.Fork,
            Dimensions:   c.Dimensions.OrderBy(d => d.ToString(), StringComparer.Ordinal).ToList()
        )).ToList();

        var stub = new CampaignManifest(
            SchemaVersion:           schemaVersion,
            CampaignId:              campaignId,
            CampaignVersion:         campaignVersion,
            FamilyName:              familyName ?? CurrentFamilyName,
            BatchSize:               manifestCases.Count,
            CreatedUtc:              createdUtc,
            SelectionPolicyVersion:  CurrentSelectionPolicyVersion,
            EelsIdentity:            eelsIdentity,
            EelsProvenance:          isV2 ? eelsProvenance : null,
            SemanticIdentityHash:    isV2 ? eelsProvenance!.CanonicalHash : null,
            FixtureRootSha256:       fixtureRootSha256,
            RequiredComparisonFields: comparisonFields ?? StorageLifecycleComparisonFields,
            ToolVersion:             CurrentToolVersion,
            Cases:                   manifestCases,
            ManifestHash:            "");

        var hash = ComputeHash(stub, schemaVersion);
        return stub with { ManifestHash = hash };
    }

    /// <summary>
    /// Computes the canonical hash of the manifest content.
    /// For v1: excludes manifestHash and the v2-only fields (eelsProvenance, semanticIdentityHash).
    /// For v2: excludes only manifestHash.
    /// </summary>
    private static string ComputeHash(CampaignManifest stub, string schemaVersion)
    {
        var json     = HarvestJson.Serialize(stub);
        var node     = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject
                       ?? throw new InvalidOperationException("Manifest did not serialize to a JSON object");
        node.Remove("manifestHash");

        if (schemaVersion == SchemaV1)
        {
            // v1 manifests never had these fields — exclude them to preserve
            // hash stability for already-frozen v1 manifests.
            node.Remove("eelsProvenance");
            node.Remove("semanticIdentityHash");
        }

        var stripped = node.ToJsonString(HarvestJson.Options);
        var hash     = SHA256.HashData(Encoding.UTF8.GetBytes(stripped));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
