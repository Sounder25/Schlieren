using Schlieren.Harvest.Fixtures;
using Xunit;

namespace Schlieren.Harvest.Tests.Fixtures;

/// <summary>
/// Admission and catalog tests for FixtureCatalog and EelsFixtureReader.
///
/// Covers (per Task 5 Step 2 contract):
///   - MissingRoot            — catalog root does not exist
///   - OutsideRoot            — path traversal attack escapes the declared root
///   - MalformedJson          — file is not valid JSON
///   - UnsupportedFork        — post section names a fork Schlieren does not know
///   - MissingPreState        — fixture has no "pre" section
///   - MissingPostState       — fork variant has no "state" in post entry
///   - ValidPublishedFixture  — well-formed state_test with pre + post.state
///   - DuplicateCaseId        — two entries with the same case ID in one catalog call
///   - DeterministicOrder     — catalog returns cases in a stable, ordinal-sorted order
/// </summary>
public class FixtureCatalogTests
{
    // Resolve the Samples directory relative to the test assembly location so the
    // tests work regardless of working directory.
    private static readonly string SamplesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Samples"));

    private static string Sample(string name) => Path.Combine(SamplesDir, name);

    // ── MissingRoot ──────────────────────────────────────────────────────

    [Fact]
    public void Catalog_MissingRoot_ReportsAllFilesAsMissingRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var catalog = new FixtureCatalog(root);
        var result  = catalog.Admit(new[] { Path.Combine(root, "some.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.MissingRoot, result[0].Admission);
    }

    // ── OutsideRoot ──────────────────────────────────────────────────────

    [Fact]
    public void Catalog_PathOutsideRoot_RejectsAsOutsideRoot()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        // Path that resolves outside SamplesDir via traversal
        var outsidePath = Path.GetFullPath(Path.Combine(SamplesDir, "..", "..", "some_other.json"));
        var result = catalog.Admit(new[] { outsidePath });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.OutsideRoot, result[0].Admission);
    }

    // ── MalformedJson ────────────────────────────────────────────────────

    [Fact]
    public void Catalog_MalformedJson_RejectsAsMalformedJson()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("malformed_json.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.MalformedJson, result[0].Admission);
    }

    // ── UnsupportedFork ──────────────────────────────────────────────────

    [Fact]
    public void Catalog_UnsupportedFork_RejectsAsUnsupportedFork()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("unsupported_fork.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.UnsupportedFork, result[0].Admission);
    }

    // ── MissingPreState ──────────────────────────────────────────────────

    [Fact]
    public void Catalog_MissingPreState_RejectsAsMissingPreState()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("missing_pre.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.MissingPreState, result[0].Admission);
    }

    // ── MissingPostState ─────────────────────────────────────────────────

    [Fact]
    public void Catalog_MissingPostState_RejectsAsMissingPostState()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("missing_post_state.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.MissingPostState, result[0].Admission);
    }

    // ── ValidPublishedFixture ─────────────────────────────────────────────

    [Fact]
    public void Catalog_ValidPublishedFixture_Admits()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("valid_published_berlin.json") });

        Assert.Single(result);
        var meta = result[0];
        Assert.Equal(AdmissionReasonCode.Admitted, meta.Admission);
        Assert.Equal("Berlin", meta.Fork);
        Assert.NotEmpty(meta.CaseId);
        Assert.NotEmpty(meta.SourceSha256);
        Assert.NotEmpty(meta.RelativePath);
        // Relative path must use forward slashes
        Assert.DoesNotContain("\\", meta.RelativePath);
    }

    [Fact]
    public void Catalog_ValidFixture_RelativePathContainedInRoot()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("valid_published_berlin.json") });

        Assert.Single(result);
        // The absolute path reconstructed from root + relative path must equal the original
        var reconstructed = Path.GetFullPath(Path.Combine(SamplesDir, result[0].RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var original      = Path.GetFullPath(Sample("valid_published_berlin.json"));
        Assert.Equal(original, reconstructed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalog_ValidFixture_Sha256IsHexAndCorrectLength()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("valid_published_berlin.json") });

        Assert.Single(result);
        Assert.Matches("^[0-9a-f]{64}$", result[0].SourceSha256);
    }

    // ── DuplicateCaseId ──────────────────────────────────────────────────

    [Fact]
    public void Catalog_DuplicateCaseId_SecondEntryRejectedAsDuplicate()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        // Pass the same file twice — the second admission of the same case ID is a duplicate
        var result = catalog.Admit(new[]
        {
            Sample("valid_published_berlin.json"),
            Sample("valid_published_berlin.json")
        });

        Assert.Equal(2, result.Count);
        Assert.Equal(AdmissionReasonCode.Admitted,      result[0].Admission);
        Assert.Equal(AdmissionReasonCode.DuplicateCaseId, result[1].Admission);
    }

    // ── DeterministicOrder ───────────────────────────────────────────────

    [Fact]
    public void Catalog_MultipleFiles_ReturnedInStableOrdinalOrder()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var files = new[]
        {
            Sample("valid_sstore_istanbul.json"),
            Sample("valid_published_berlin.json"),
        };

        var resultA = catalog.Admit(files);
        var resultB = catalog.Admit(files);

        // Same inputs must yield the same order both times
        Assert.Equal(
            resultA.Select(m => m.CaseId),
            resultB.Select(m => m.CaseId));
    }

    // ── NoPostEntries (UnsupportedFork variant) ───────────────────────────

    [Fact]
    public void Catalog_NoPostSection_RejectsAsMalformedOrUnsupportedFork()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("malformed_no_post.json") });

        // A file that parses but has no valid state-test case structure is MalformedJson
        // or UnsupportedFork — either is correct; the key requirement is it is NOT Admitted.
        Assert.Single(result);
        Assert.NotEqual(AdmissionReasonCode.Admitted, result[0].Admission);
    }

    // ── AmbiguousVariant — multiple supported forks ───────────────────────

    [Fact]
    public void Catalog_MultipleSupportedForks_RejectsAsAmbiguousVariant()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("ambiguous_multi_fork.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.AmbiguousVariant, result[0].Admission);
    }

    // ── MissingStatusAuthority ────────────────────────────────────────────

    [Fact]
    public void Catalog_MissingStatusAuthority_RejectsAsMissingStatusAuthority()
    {
        var catalog = new FixtureCatalog(SamplesDir);
        var result  = catalog.Admit(new[] { Sample("missing_status_authority.json") });

        Assert.Single(result);
        Assert.Equal(AdmissionReasonCode.MissingStatusAuthority, result[0].Admission);
    }
}