using System.Security.Cryptography;
using System.Text;
using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Harvest.Clustering;

/// <summary>
/// Typed fingerprint for a Harvest failure family.
///
/// The key is built from typed causal facts only:
///   fork + first discrepancy layer + first discrepancy kind
///
/// It intentionally excludes:
///   - Human-readable summaries or rendered mismatch text
///   - Test names or source paths
///   - Journal-only evidence (journal may enrich diagnosis but not decide the key)
///   - Expected or actual values (those are evidence, not geometry)
///
/// Two cases with identical (fork, layer, kind) geometry belong to the same family
/// regardless of their specific expected/actual values or any human annotation.
/// Different forks always produce different keys.
/// </summary>
public sealed record FailureFingerprint(
    string              Fork,
    DiscrepancyLayer    PrimaryLayer,
    DiscrepancyKind     PrimaryKind)
{
    /// <summary>
    /// Stable string key for clustering. Contains only typed enum names and the fork.
    /// Safe to use as a dictionary key across runs.
    /// </summary>
    public string Key => $"{Fork}/{PrimaryLayer}/{PrimaryKind}";

    /// <summary>
    /// SHA-256 hash of the canonical JSON representation of this fingerprint.
    /// Used as an artifact identifier in ledger records.
    /// </summary>
    public string Hash
    {
        get
        {
            var json    = HarvestJson.Serialize(new { fork = Fork, layer = PrimaryLayer.ToString(), kind = PrimaryKind.ToString() });
            var bytes   = Encoding.UTF8.GetBytes(json);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Build a fingerprint from the first (earliest-layer) FieldDelta in a list.
    /// If deltas is empty, returns a sentinel fingerprint for no-delta cases.
    /// </summary>
    public static FailureFingerprint FromDeltas(string fork, IReadOnlyList<FieldDelta> deltas)
    {
        if (deltas.Count == 0)
            return new FailureFingerprint(fork, DiscrepancyLayer.Journal, DiscrepancyKind.JournalConservation);

        // Use the first delta — comparator already emits them in stable order
        // (Validity → Gas → ReturnData → Logs → Account → Storage → Journal)
        var first = deltas[0];
        return new FailureFingerprint(fork, first.Layer, first.Kind);
    }
}
