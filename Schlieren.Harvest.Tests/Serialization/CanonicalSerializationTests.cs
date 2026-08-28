using Schlieren.Harvest.Domain;
using Schlieren.Harvest.Serialization;
using System.Text.Json;
using Xunit;

namespace Schlieren.Harvest.Tests.Serialization;

/// <summary>
/// Proves canonical JSON serialization and content-hash contracts for the
/// Harvest domain. Contracts per the Task 4 specification:
///   - UTF-8, camelCase property names, enum values as strings.
///   - UTC round-trip timestamps (DateTimeKind.Utc).
///   - Dictionary keys sorted lexicographically.
///   - No indentation.
///   - ContentHash = lowercase SHA-256 over canonical JSON with the
///     contentHash field itself omitted from the hashed bytes.
///   - Semantically identical payloads (same data, different insertion order)
///     produce identical hashes.
///   - A one-field change produces a different hash.
/// </summary>
public class CanonicalSerializationTests
{
    // ── Enum serialization ────────────────────────────────────────────────

    [Theory]
    [InlineData(CaseStatus.Pass,          "pass")]
    [InlineData(CaseStatus.Divergence,    "divergence")]
    [InlineData(CaseStatus.FixtureInvalid,"fixtureInvalid")]
    [InlineData(CaseStatus.HarnessError,  "harnessError")]
    [InlineData(CaseStatus.Aborted,       "aborted")]
    [InlineData(CaseStatus.Quarantined,   "quarantined")]
    public void CaseStatus_SerializesAsCamelCaseString(CaseStatus status, string expected)
    {
        var json = HarvestJson.Serialize(new { status });
        Assert.Contains($"\"status\":\"{expected}\"", json);
    }

    [Theory]
    [InlineData(RunKind.Calibration,  "calibration")]
    [InlineData(RunKind.Inspection,   "inspection")]
    [InlineData(RunKind.Reinspection, "reinspection")]
    public void RunKind_SerializesAsCamelCaseString(RunKind kind, string expected)
    {
        var json = HarvestJson.Serialize(new { kind });
        Assert.Contains($"\"kind\":\"{expected}\"", json);
    }

    [Theory]
    [InlineData(RunState.Staging,          "staging")]
    [InlineData(RunState.ApparatusFailed,  "apparatusFailed")]
    [InlineData(RunState.InspectionFailed, "inspectionFailed")]
    [InlineData(RunState.Completed,        "completed")]
    [InlineData(RunState.Certified,        "certified")]
    public void RunState_SerializesAsCamelCaseString(RunState state, string expected)
    {
        var json = HarvestJson.Serialize(new { state });
        Assert.Contains($"\"state\":\"{expected}\"", json);
    }

    // ── No indentation ────────────────────────────────────────────────────

    [Fact]
    public void Serialization_ProducesNoIndentation()
    {
        var obj = new { a = 1, b = new { c = 2 } };
        var json = HarvestJson.Serialize(obj);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("  ", json);
    }

    // ── UTC timestamp round-trip ──────────────────────────────────────────

    [Fact]
    public void Serialization_UtcTimestamps_RoundTripCorrectly()
    {
        var ts = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var envelope = new ContentEnvelope<string>(
            SchemaVersion: "1",
            CreatedUtc: ts,
            ContentHash: "",
            Payload: "test");

        var json = HarvestJson.Serialize(envelope);
        var round = HarvestJson.Deserialize<ContentEnvelope<string>>(json)!;
        Assert.Equal(DateTimeKind.Utc, round.CreatedUtc.Kind);
        Assert.Equal(ts, round.CreatedUtc);
    }

    // ── Dictionary key sorting ────────────────────────────────────────────

    [Fact]
    public void Serialization_DictionaryKeys_AreSortedLexicographically()
    {
        // Build two dicts with the same keys/values but different insertion order
        var dictA = new Dictionary<string, int>
        {
            ["zebra"] = 1, ["apple"] = 2, ["mango"] = 3
        };
        var dictB = new Dictionary<string, int>
        {
            ["mango"] = 3, ["zebra"] = 1, ["apple"] = 2
        };

        var jsonA = HarvestJson.Serialize(dictA);
        var jsonB = HarvestJson.Serialize(dictB);

        // Both must produce the same sorted output
        Assert.Equal(jsonA, jsonB);
        // And the sorted order must be: apple, mango, zebra
        var posApple = jsonA.IndexOf("apple", StringComparison.Ordinal);
        var posMango = jsonA.IndexOf("mango", StringComparison.Ordinal);
        var posZebra = jsonA.IndexOf("zebra", StringComparison.Ordinal);
        Assert.True(posApple < posMango && posMango < posZebra,
            $"Expected apple < mango < zebra in: {jsonA}");
    }

    // ── ContentHash contracts ─────────────────────────────────────────────

    [Fact]
    public void ContentHash_IsLowercaseSha256()
    {
        var envelope = new ContentEnvelope<string>(
            SchemaVersion: "1",
            CreatedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ContentHash: "",
            Payload: "hello");

        var hash = ContentHasher.Compute(envelope);
        // SHA-256 is 64 hex chars, lowercase
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ContentHash_IdenticalPayloads_ProduceSameHash()
    {
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var e1 = new ContentEnvelope<string>("1", ts, "", "same-data");
        var e2 = new ContentEnvelope<string>("1", ts, "", "same-data");

        Assert.Equal(ContentHasher.Compute(e1), ContentHasher.Compute(e2));
    }

    [Fact]
    public void ContentHash_DifferentPayload_ProducesDifferentHash()
    {
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var e1 = new ContentEnvelope<string>("1", ts, "", "data-A");
        var e2 = new ContentEnvelope<string>("1", ts, "", "data-B");

        Assert.NotEqual(ContentHasher.Compute(e1), ContentHasher.Compute(e2));
    }

    [Fact]
    public void ContentHash_ExcludesContentHashFieldFromHash()
    {
        // An envelope with contentHash already filled should hash identically
        // to one with it blank — the field is excluded from hashed bytes.
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var blank  = new ContentEnvelope<string>("1", ts, "",               "payload");
        var filled = new ContentEnvelope<string>("1", ts, "some-prior-hash","payload");

        Assert.Equal(ContentHasher.Compute(blank), ContentHasher.Compute(filled));
    }

    [Fact]
    public void ContentHash_DictionaryInsertionOrder_DoesNotAffectHash()
    {
        // Two dicts with identical logical content but different insertion order
        // must hash identically (key-sort guarantee passes through to hash).
        var dictA = new Dictionary<string, int> { ["z"] = 9, ["a"] = 1 };
        var dictB = new Dictionary<string, int> { ["a"] = 1, ["z"] = 9 };

        // Wrap in envelopes
        var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var eA = new ContentEnvelope<Dictionary<string, int>>("1", ts, "", dictA);
        var eB = new ContentEnvelope<Dictionary<string, int>>("1", ts, "", dictB);

        Assert.Equal(ContentHasher.Compute(eA), ContentHasher.Compute(eB));
    }

    // ── Record round-trips ────────────────────────────────────────────────

    [Fact]
    public void FieldDelta_RoundTrips()
    {
        var delta = new FieldDelta(
            Layer: DiscrepancyLayer.Gas,
            Kind:  DiscrepancyKind.GasUsed,
            Expected: JsonSerializer.SerializeToElement("21000"),
            Actual:   JsonSerializer.SerializeToElement("21050"));

        var json  = HarvestJson.Serialize(delta);
        var round = HarvestJson.Deserialize<FieldDelta>(json)!;

        Assert.Equal(delta.Layer, round.Layer);
        Assert.Equal(delta.Kind,  round.Kind);
    }

    [Fact]
    public void CaseOutcome_RoundTrips()
    {
        var outcome = new CaseOutcome(
            CaseId:    "test-case-001",
            Status:    CaseStatus.Pass,
            Deltas:    [],
            RunId:     "run-abc",
            CreatedUtc: new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc));

        var json  = HarvestJson.Serialize(outcome);
        var round = HarvestJson.Deserialize<CaseOutcome>(json)!;

        Assert.Equal(outcome.CaseId, round.CaseId);
        Assert.Equal(outcome.Status, round.Status);
        Assert.Equal(outcome.RunId,  round.RunId);
    }

    [Fact]
    public void CaseOutcome_WithoutAttemptEvidence_PreservesLegacyCanonicalShape()
    {
        var outcome = new CaseOutcome(
            "legacy-case", CaseStatus.Pass, [], "run-legacy",
            new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc));

        var json = HarvestJson.Serialize(outcome);

        Assert.DoesNotContain("attemptEvidence", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CaseOutcome_WithAttemptEvidence_RoundTripsTypedEvidence()
    {
        var evidence = new ExecutionAttemptEvidence(
            ApparatusFailureKind.OracleTimeout, TimeSpan.FromSeconds(3), -1,
            new string('a', 64), new string('b', 64), true, new string('c', 64));
        var outcome = new CaseOutcome(
            "failed-case", CaseStatus.HarnessError, [], "run-new",
            new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            "oracle timed out", evidence);

        var round = HarvestJson.Deserialize<CaseOutcome>(HarvestJson.Serialize(outcome));

        Assert.NotNull(round);
        Assert.Equal(evidence, round!.AttemptEvidence);
    }
}
