using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Schlieren.CLI.Commands;

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

    // ── Test 5: campaign run requires args ────────────────────────────────

    [Fact]
    public async Task CampaignRun_MissingRequired_ReturnsNonZero()
    {
        var exit = await InvokeAsync("campaign run");
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task CampaignRun_AllArgs_Returns0()
    {
        var exit = await InvokeAsync("campaign run /tmp/manifest.json --ledger /tmp/l");
        Assert.Equal(0, exit);
    }

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
