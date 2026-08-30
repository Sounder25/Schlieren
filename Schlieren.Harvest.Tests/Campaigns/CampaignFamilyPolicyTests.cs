using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Fixtures;
using Xunit;

namespace Schlieren.Harvest.Tests.Campaigns;

public sealed class CampaignFamilyPolicyTests
{
    [Fact]
    public void G1AddPolicy_SelectsExactFiveCategoryAndForkAllocation()
    {
        var policy = CampaignFamilyPolicy.PrecompilesBls12G1Add;
        var cases = new List<FixtureCaseMetadata>();
        AddCases(cases, "test_bls12_g1add.py::test_valid", "Prague", 7);
        AddCases(cases, "test_bls12_g1add.py::test_valid", "Osaka", 8);
        AddCases(cases, "test_bls12_g1add.py::test_invalid", "Prague", 9);
        AddCases(cases, "test_bls12_g1add.py::test_invalid", "Osaka", 20);
        AddCases(cases, "test_bls12_g1add.py::test_call_types", "Prague", 6);
        AddCases(cases, "test_bls12_g1add.py::test_call_types", "Osaka", 6);
        AddCases(cases, "test_bls12_g1add.py::test_gas", "Prague", 2);
        AddCases(cases, "test_bls12_g1add.py::test_gas", "Osaka", 2);
        cases.Add(Case(
            "test_bls12_precompiles_before_fork.py::test_precompile_before_fork" +
            "[fork_Cancun-state_test--G1ADD]",
            "fixtures/bls12_precompiles_before_fork/precompile_before_fork.json",
            "Cancun"));

        var result = policy.TrySelect(cases, count: 50);

        Assert.True(result.IsSuccess);
        var selected = Assert.IsAssignableFrom<IReadOnlyList<FixtureCaseMetadata>>(result.Cases);
        Assert.Equal(50, selected.Count);
        Assert.Equal(50, selected.Select(c => c.CaseId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(15, selected.Count(c => c.CaseId.Contains("::test_valid[", StringComparison.Ordinal)));
        Assert.Equal(18, selected.Count(c => c.CaseId.Contains("::test_invalid[", StringComparison.Ordinal)));
        Assert.Equal(12, selected.Count(c => c.CaseId.Contains("::test_call_types[", StringComparison.Ordinal)));
        Assert.Equal(4, selected.Count(c => c.CaseId.Contains("::test_gas[", StringComparison.Ordinal)));
        Assert.Single(selected, c => c.CaseId.Contains("precompiles_before_fork", StringComparison.Ordinal));
        Assert.Equal(24, selected.Count(c => c.Fork == "Prague"));
        Assert.Equal(25, selected.Count(c => c.Fork == "Osaka"));
        Assert.Single(selected, c => c.Fork == "Cancun");
        Assert.Same(policy, CampaignFamilyPolicy.TryGet("precompiles-bls12-g1add"));
    }

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

    private static void AddCases(
        ICollection<FixtureCaseMetadata> destination,
        string testName,
        string fork,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            destination.Add(Case(
                $"{testName}[fork_{fork}-state_test-vector_{i:D3}]",
                "fixtures/bls12_g1add/cases.json",
                fork));
        }
    }

    private static FixtureCaseMetadata Case(
        string caseId,
        string relativePath = "fixtures/suite.py/case.json",
        string fork = "Prague") => new(
        CaseId: caseId,
        RelativePath: relativePath,
        SourceSha256: new string('a', 64),
        Fork: fork,
        Dimensions: new HashSet<StorageDimension>(),
        Admission: AdmissionReasonCode.Admitted,
        Detail: null);
}
