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
/// </summary>
public sealed record EelsIdentity(
    string ExecutableSha256,
    string ReportedVersion,
    string? CommitSha);

/// <summary>
/// Immutable campaign manifest. Once frozen it must not be mutated.
/// Replacing or reclassifying a case requires a new campaign version and manifest.
///
/// Fields required by the approved spec (docs/superpowers/specs/...-design.md):
///   - schema version, campaign ID and version, family name, batch size
///   - selection-policy version
///   - EELS release/commit identity
///   - fixture root identity (SHA-256 of root path + corpus identity)
///   - ordered cases with checksums
///   - required comparison fields
///   - creation timestamp and tool version
///   - canonical manifest hash
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
    string?                     FixtureRootSha256,
    IReadOnlyList<string>       RequiredComparisonFields,
    string                      ToolVersion,
    IReadOnlyList<ManifestCase> Cases,
    string                      ManifestHash)
{
    public const string CurrentSchemaVersion          = "1";
    public const string CurrentSelectionPolicyVersion = "storage-lifecycle-v1";
    public const string CurrentFamilyName             = "storage-lifecycle";
    public const string CurrentToolVersion            = "schlieren-harvest-1";

    public static readonly IReadOnlyList<string> StorageLifecycleComparisonFields =
        new[] { "status", "gasUsed", "logs", "postState.storage" };

    /// <summary>
    /// Freezes an ordered list of admitted cases into an immutable manifest.
    ///
    /// <paramref name="createdUtc"/> is supplied by the caller (injected from a
    /// TimeProvider) — never read from the wall clock here.
    ///
    /// <paramref name="eelsIdentity"/> is required for Campaign 1 certification and must
    /// be provided whenever a real EELS oracle run is performed. Pass null only in unit
    /// tests that do not run the oracle. A manifest frozen without EELS identity will be
    /// rejected by the certification gate.
    ///
    /// <paramref name="allowNullIdentity"/> must be explicitly set to true in tests that
    /// intentionally freeze without oracle identity. This makes the omission visible.
    /// </summary>
    public static CampaignManifest Freeze(
        IReadOnlyList<FixtureCaseMetadata> cases,
        string campaignId,
        DateTime createdUtc,
        string campaignVersion = "1",
        EelsIdentity? eelsIdentity = null,
        string? fixtureRootSha256 = null,
        bool allowNullIdentity = false)
    {
        if (eelsIdentity is null && !allowNullIdentity)
            throw new InvalidOperationException(
                "EelsIdentity is required to freeze a Campaign 1 manifest. " +
                "If this is a test-only manifest that intentionally omits the oracle identity, " +
                "pass allowNullIdentity: true explicitly.");
        var manifestCases = cases.Select(c => new ManifestCase(
            CaseId:       c.CaseId,
            RelativePath: c.RelativePath,
            SourceSha256: c.SourceSha256,
            Fork:         c.Fork,
            Dimensions:   c.Dimensions.OrderBy(d => d.ToString(), StringComparer.Ordinal).ToList()
        )).ToList();

        var stub = new CampaignManifest(
            SchemaVersion:           CurrentSchemaVersion,
            CampaignId:              campaignId,
            CampaignVersion:         campaignVersion,
            FamilyName:              CurrentFamilyName,
            BatchSize:               manifestCases.Count,
            CreatedUtc:              createdUtc,
            SelectionPolicyVersion:  CurrentSelectionPolicyVersion,
            EelsIdentity:            eelsIdentity,
            FixtureRootSha256:       fixtureRootSha256,
            RequiredComparisonFields: StorageLifecycleComparisonFields,
            ToolVersion:             CurrentToolVersion,
            Cases:                   manifestCases,
            ManifestHash:            "");

        var hash = ComputeHash(stub);
        return stub with { ManifestHash = hash };
    }

    private static string ComputeHash(CampaignManifest stub)
    {
        var json     = HarvestJson.Serialize(stub);
        var node     = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject
                       ?? throw new InvalidOperationException("Manifest did not serialize to a JSON object");
        node.Remove("manifestHash");
        var stripped = node.ToJsonString(HarvestJson.Options);
        var hash     = SHA256.HashData(Encoding.UTF8.GetBytes(stripped));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
