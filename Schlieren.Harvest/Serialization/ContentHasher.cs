using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Schlieren.Harvest.Domain;

namespace Schlieren.Harvest.Serialization;

/// <summary>
/// Computes the canonical content hash for a <see cref="ContentEnvelope{T}"/>.
///
/// Algorithm (per Task 4 spec):
///   1. Serialize the envelope to canonical JSON (HarvestJson.Options).
///   2. Parse the JSON, remove the "contentHash" key at the root level.
///   3. Re-serialize the stripped object to canonical JSON (ensures
///      deterministic ordering even if the parsed node reorders keys).
///   4. SHA-256 the UTF-8 bytes.
///   5. Return lowercase hex string (64 chars).
///
/// This approach ensures the contentHash field itself is excluded from
/// the bytes that are hashed, so pre-filling the field does not affect
/// the computed hash.
/// </summary>
public static class ContentHasher
{
    private const string ContentHashProperty = "contentHash";

    public static string Compute<T>(ContentEnvelope<T> envelope)
    {
        // 1. Serialize to canonical JSON
        var json = HarvestJson.Serialize(envelope);

        // 2. Parse and remove the contentHash field
        var node = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidOperationException("Envelope did not serialize to a JSON object.");
        node.Remove(ContentHashProperty);

        // 3. Re-serialize without contentHash (canonical, sorted)
        var stripped = node.ToJsonString(HarvestJson.Options);

        // 4+5. SHA-256 → lowercase hex
        var bytes = Encoding.UTF8.GetBytes(stripped);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
