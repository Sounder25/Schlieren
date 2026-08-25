using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// Immutable campaign manifest. Once frozen it must not be mutated.
/// Replacing or reclassifying a case requires a new version with a new manifest.
///
/// <see cref="ManifestHash"/> is the canonical SHA-256 fingerprint of the manifest
/// content (computed via <see cref="Freeze"/>). It is excluded from the bytes that
/// are hashed (same convention as <c>ContentHasher</c>).
/// </summary>
public sealed record CampaignManifest(
    string                    SchemaVersion,
    string                    CampaignId,
    DateTime                  CreatedUtc,
    string                    SelectionPolicyVersion,
    IReadOnlyList<ManifestCase> Cases,
    string                    ManifestHash)
{
    public const string CurrentSchemaVersion         = "1";
    public const string CurrentSelectionPolicyVersion = "storage-lifecycle-v1";

    /// <summary>
    /// Freezes an ordered list of admitted cases into an immutable manifest.
    /// The <paramref name="createdUtc"/> timestamp is supplied by the caller (injected
    /// from a <c>TimeProvider</c> at the call site) — never read from the wall clock here.
    /// </summary>
    public static CampaignManifest Freeze(
        IReadOnlyList<FixtureCaseMetadata> cases,
        string campaignId,
        DateTime createdUtc)
    {
        var manifestCases = cases.Select(c => new ManifestCase(
            CaseId:       c.CaseId,
            RelativePath: c.RelativePath,
            SourceSha256: c.SourceSha256,
            Fork:         c.Fork,
            Dimensions:   c.Dimensions.OrderBy(d => d.ToString(), StringComparer.Ordinal).ToList()
        )).ToList();

        // Build a deterministic hash over canonical JSON with manifestHash field blank
        var stub = new CampaignManifest(
            SchemaVersion:          CurrentSchemaVersion,
            CampaignId:             campaignId,
            CreatedUtc:             createdUtc,
            SelectionPolicyVersion: CurrentSelectionPolicyVersion,
            Cases:                  manifestCases,
            ManifestHash:           "");

        var hash = ComputeHash(stub);

        return stub with { ManifestHash = hash };
    }

    private static string ComputeHash(CampaignManifest stub)
    {
        // Serialize to canonical JSON (sorted keys, camelCase, no indent)
        var json  = HarvestJson.Serialize(stub);

        // Parse and remove the manifestHash field
        var node  = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject
                    ?? throw new InvalidOperationException("Manifest did not serialize to a JSON object");
        node.Remove("manifestHash");

        var stripped = node.ToJsonString(HarvestJson.Options);
        var hash     = SHA256.HashData(Encoding.UTF8.GetBytes(stripped));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
