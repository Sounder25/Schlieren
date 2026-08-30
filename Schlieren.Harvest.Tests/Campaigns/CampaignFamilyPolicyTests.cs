using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Fixtures;
using Xunit;

namespace Schlieren.Harvest.Tests.Campaigns;

public sealed class CampaignFamilyPolicyTests
{
    [Fact]
    public void StratifiedSelection_DominatedCorpus_ReturnsEveryExactQuota()
    {
        var policy = Policy(
            new CampaignSelectionStratum("valid", 2, ["::test_valid["], []),
            new CampaignSelectionStratum("invalid", 2, ["::test_invalid["], []));
        var cases = new[]
        {
            Case("suite.py::test_valid[fork_Prague-vector_a]"),
            Case("suite.py::test_valid[fork_Prague-vector_z]"),
            Case("suite.py::test_invalid[fork_Prague-vector_a]"),
            Case("suite.py::test_invalid[fork_Prague-vector_b]"),
            Case("suite.py::test_invalid[fork_Prague-vector_c]"),
            Case("suite.py::test_invalid[fork_Prague-vector_d]"),
            Case("suite.py::test_invalid[fork_Prague-vector_e]"),
            Case("suite.py::test_invalid[fork_Prague-vector_z]"),
        };

        var result = policy.TrySelect(cases, count: 4);

        Assert.True(result.IsSuccess);
        var selected = Assert.IsAssignableFrom<IReadOnlyList<FixtureCaseMetadata>>(result.Cases);
        Assert.Equal(2, selected.Count(c => c.CaseId.Contains("::test_valid[", StringComparison.Ordinal)));
        Assert.Equal(2, selected.Count(c => c.CaseId.Contains("::test_invalid[", StringComparison.Ordinal)));
    }

    [Fact]
    public void StratifiedSelection_ShuffledEnumeration_ReturnsSameOrderedCases()
    {
        var policy = Policy(
            new CampaignSelectionStratum("valid", 2, ["::test_valid["], []),
            new CampaignSelectionStratum("invalid", 2, ["::test_invalid["], []));
        var ordered = new[]
        {
            Case("suite.py::test_valid[fork_Prague-vector_a]"),
            Case("suite.py::test_valid[fork_Prague-vector_m]"),
            Case("suite.py::test_valid[fork_Prague-vector_z]"),
            Case("suite.py::test_invalid[fork_Prague-vector_a]"),
            Case("suite.py::test_invalid[fork_Prague-vector_m]"),
            Case("suite.py::test_invalid[fork_Prague-vector_z]"),
        };
        var shuffled = new[] { ordered[4], ordered[1], ordered[5], ordered[0], ordered[3], ordered[2] };

        var first = policy.TrySelect(ordered, count: 4);
        var second = policy.TrySelect(shuffled, count: 4);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var firstCases = Assert.IsAssignableFrom<IReadOnlyList<FixtureCaseMetadata>>(first.Cases);
        var secondCases = Assert.IsAssignableFrom<IReadOnlyList<FixtureCaseMetadata>>(second.Cases);
        Assert.Equal(
            new[]
            {
                "suite.py::test_valid[fork_Prague-vector_a]",
                "suite.py::test_valid[fork_Prague-vector_z]",
                "suite.py::test_invalid[fork_Prague-vector_a]",
                "suite.py::test_invalid[fork_Prague-vector_z]",
            },
            firstCases.Select(c => c.CaseId));
        Assert.Equal(firstCases.Select(c => c.CaseId), secondCases.Select(c => c.CaseId));
    }

    [Fact]
    public void StratifiedSelection_OneStratumIsShort_FailsInsteadOfBorrowing()
    {
        var policy = Policy(
            new CampaignSelectionStratum("valid", 2, ["::test_valid["], []),
            new CampaignSelectionStratum("invalid", 2, ["::test_invalid["], []));
        var cases = new[]
        {
            Case("suite.py::test_valid[fork_Prague-only]"),
            Case("suite.py::test_invalid[fork_Prague-a]"),
            Case("suite.py::test_invalid[fork_Prague-b]"),
            Case("suite.py::test_invalid[fork_Prague-c]"),
            Case("suite.py::test_invalid[fork_Prague-d]"),
        };

        var result = policy.TrySelect(cases, count: 4);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Cases);
        Assert.Contains("valid", result.InsufficientReport!.Reason, StringComparison.Ordinal);
        Assert.Equal(2, result.InsufficientReport.RequestedCount);
        Assert.Equal(1, result.InsufficientReport.AvailableCount);
    }

    private static CampaignFamilyPolicy Policy(params CampaignSelectionStratum[] strata) => new(
        FamilyName: "test-family",
        FamilyVersion: "1",
        Description: "test",
        PathFilters: ["suite.py"],
        ScoreDimensions: [],
        SelectionStrata: strata);

    private static FixtureCaseMetadata Case(string caseId) => new(
        CaseId: caseId,
        RelativePath: "fixtures/suite.py/case.json",
        SourceSha256: new string('a', 64),
        Fork: "Prague",
        Dimensions: new HashSet<StorageDimension>(),
        Admission: AdmissionReasonCode.Admitted,
        Detail: null);
}
