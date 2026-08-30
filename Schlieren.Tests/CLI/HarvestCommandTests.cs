using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Text.Json;
using Schlieren.CLI.Commands;
using Schlieren.Harvest.Campaigns;
using Schlieren.Harvest.Serialization;

namespace Schlieren.Tests.CLI;

/// <summary>
/// Proves the Harvest CLI command tree:
///   - harvest --help returns 0 and lists subcommands.
///   - harvest calibrate requires --ledger, --eels, --eels-version.
///   - harvest catalog requires --fixtures, --eels, --eels-version.
///   - harvest campaign create requires family + --fixtures + --eels + --eels-version + --ledger.
///   - harvest campaign run requires manifest + --ledger.
///   - harvest compare requires before-run + after-run + --ledger.
///   - harvest repair open requires family-id + --run + --ledger.
///   - harvest repair close requires repair-id + --commit + --run + --test + --ledger.
///   - harvest certify requires run-id + --ledger + --suite-gate.
/// </summary>
public class HarvestCommandTests
{
    private static Command BuildHarvest() => HarvestCommand.Build();

    private static Command BuildHarvest(Func<string, string?> getEnvironmentVariable) =>
        HarvestCommand.Build(getEnvironmentVariable);

    private static async Task<int> InvokeAsync(string args)
    {
        var cmd = BuildHarvest();
        return await cmd.InvokeAsync(args.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ── Test 1: help returns 0 ────────────────────────────────────────────

    [Fact]
    public async Task Harvest_Help_Returns0()
    {
        var exit = await InvokeAsync("--help");
        Assert.Equal(0, exit);
    }

    // ── Test 2: calibrate requires all options ────────────────────────────

    [Fact]
    public async Task Calibrate_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("calibrate");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Calibrate_AllOptions_Returns0()
    {
        var exit = await InvokeAsync("calibrate --ledger /tmp/l --eels /tmp/e --eels-version v1");
        Assert.Equal(0, exit);
    }

    // ── Test 3: catalog requires options ──────────────────────────────────

    [Fact]
    public async Task Catalog_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("catalog");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Catalog_AllOptions_Returns0()
    {
        var exit = await InvokeAsync("catalog --fixtures /tmp/f --eels /tmp/e --eels-version v1");
        Assert.Equal(0, exit);
    }

    // ── Test 4: campaign create requires args ─────────────────────────────

    [Fact]
    public async Task CampaignCreate_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("campaign create");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task CampaignCreate_AllArgs_Returns0()
    {
        var exit = await InvokeAsync(
            "campaign create storage-lifecycle --count 50 --fixtures /f --eels /e --eels-version v1 --ledger /l");
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task CampaignCreate_G1Add_StoresSelectedFamilyAndPolicyInManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"harvest-create-{Guid.NewGuid():N}");
        var fixtures = Path.Combine(root, "fixtures", "bls12_g1add");
        var ledger = Path.Combine(root, "ledger");
        var eels = Path.Combine(root, "ethereum-spec-evm.exe");
        Directory.CreateDirectory(fixtures);
        await File.WriteAllTextAsync(eels, "test oracle identity");
        await File.WriteAllTextAsync(
            Path.Combine(fixtures, "campaign.json"),
            BuildG1AddFixtureJson());

        try
        {
            var exit = await BuildHarvest().InvokeAsync(
            [
                "campaign", "create", "precompiles-bls12-g1add",
                "--count", "50",
                "--fixtures", Path.Combine(root, "fixtures"),
                "--eels", eels,
                "--eels-version", "2.19.0",
                "--ledger", ledger,
            ]);

            Assert.Equal(0, exit);
            var manifestPath = Assert.Single(Directory.GetFiles(
                Path.Combine(ledger, "campaigns", "precompiles-bls12-g1add-v1"),
                "manifest.json",
                SearchOption.AllDirectories));
            var manifest = HarvestJson.Deserialize<CampaignManifest>(
                await File.ReadAllTextAsync(manifestPath));
            Assert.NotNull(manifest);
            Assert.Equal("precompiles-bls12-g1add", manifest.FamilyName);
            Assert.Equal("stratified-v1", manifest.SelectionPolicyVersion);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string BuildG1AddFixtureJson()
    {
        var cases = new Dictionary<string, object?>();
        AddCases(cases, "test_bls12_g1add.py::test_valid", "Prague", 7);
        AddCases(cases, "test_bls12_g1add.py::test_valid", "Osaka", 8);
        AddCases(cases, "test_bls12_g1add.py::test_invalid", "Prague", 9);
        AddCases(cases, "test_bls12_g1add.py::test_invalid", "Osaka", 9);
        AddCases(cases, "test_bls12_g1add.py::test_call_types", "Prague", 6);
        AddCases(cases, "test_bls12_g1add.py::test_call_types", "Osaka", 6);
        AddCases(cases, "test_bls12_g1add.py::test_gas", "Prague", 2);
        AddCases(cases, "test_bls12_g1add.py::test_gas", "Osaka", 2);
        cases[
            "test_bls12_precompiles_before_fork.py::test_precompile_before_fork" +
            "[fork_Cancun-state_test--G1ADD]"] = FixtureCase("Cancun");
        return JsonSerializer.Serialize(cases);
    }

    private static void AddCases(
        IDictionary<string, object?> destination,
        string testName,
        string fork,
        int count)
    {
        for (var i = 0; i < count; i++)
            destination[$"{testName}[fork_{fork}-state_test-vector_{i:D3}]"] = FixtureCase(fork);
    }

    private static object FixtureCase(string fork) => new
    {
        _info = new Dictionary<string, string> { ["fixture-format"] = "state_test" },
        pre = new Dictionary<string, object>(),
        post = new Dictionary<string, object>
        {
            [fork] = new[]
            {
                new
                {
                    state = new Dictionary<string, object>(),
                    receipt = new { status = "0x1", cumulativeGasUsed = "0x5208" },
                }
            }
        }
    };

    // ── Test 5: campaign run requires args ────────────────────────────────

    [Fact]
    public async Task CampaignRun_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("campaign run");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task CampaignRun_MissingManifest_ReturnsInputError()
    {
        var exit = await InvokeAsync("campaign run /tmp/manifest.json --ledger /tmp/l");
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task CampaignRun_WithoutEelsExe_RefusesBeforeCreatingRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"harvest-cli-{Guid.NewGuid():N}");
        var fixtures = Path.Combine(root, "fixtures");
        var ledger = Path.Combine(root, "ledger");
        Directory.CreateDirectory(fixtures);
        var manifest = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifest, MinimalManifestJson());
        try
        {
            var command = BuildHarvest(key => key switch
            {
                "EELS_FIXTURES_ROOT" => fixtures,
                "EELS_EXE" => null,
                _ => null
            });

            var exit = await command.InvokeAsync(
                ["campaign", "run", manifest, "--ledger", ledger]);

            Assert.Equal(3, exit);
            Assert.False(Directory.Exists(Path.Combine(ledger, "runs")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string MinimalManifestJson() => """
        {
          "schemaVersion":"1",
          "campaignId":"cli-config-test",
          "campaignVersion":"1",
          "familyName":"test",
          "batchSize":0,
          "createdUtc":"2026-08-28T00:00:00Z",
          "selectionPolicyVersion":"test-v1",
          "eelsIdentity":{
            "executableSha256":"pinned-sha",
            "reportedVersion":"2.19.0",
            "commitSha":null
          },
          "fixtureRootSha256":"fixture-root",
          "requiredComparisonFields":[],
          "toolVersion":"test",
          "cases":[],
          "manifestHash":"manifest-hash"
        }
        """;

    // ── Test 6: compare requires args ─────────────────────────────────────

    [Fact]
    public async Task Compare_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("compare");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Compare_AllArgs_Returns0()
    {
        var exit = await InvokeAsync("compare run-before run-after --ledger /tmp/l");
        Assert.Equal(0, exit);
    }

    // ── Test 7: repair open requires args ─────────────────────────────────

    [Fact]
    public async Task RepairOpen_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("repair open");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task RepairOpen_AllArgs_Returns0()
    {
        var exit = await InvokeAsync("repair open fam-001 --run run-1 --ledger /tmp/l");
        Assert.Equal(0, exit);
    }

    // ── Test 8: repair close requires args ────────────────────────────────

    [Fact]
    public async Task RepairClose_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("repair close");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task RepairClose_AllArgs_Returns0()
    {
        var exit = await InvokeAsync(
            "repair close rep-001 --commit abc123 --run run-2 --test MyTest --ledger /tmp/l");
        Assert.Equal(0, exit);
    }

    // ── Test 9: certify requires args ─────────────────────────────────────

    [Fact]
    public async Task Certify_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("certify");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task Certify_AllArgs_Returns0()
    {
        var exit = await InvokeAsync("certify run-final --ledger /tmp/l --suite-gate /tmp/gate.json");
        Assert.Equal(0, exit);
    }
}
