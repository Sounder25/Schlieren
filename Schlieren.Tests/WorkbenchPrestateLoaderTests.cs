using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchPrestateLoaderTests
{
    private const string Sample = """
        {
          "accounts": [
            {
              "address": "0x00000000000000000000000000000000000000bb",
              "balance": "1000",
              "nonce": 2,
              "code": "0x600160020160005260206000f3",
              "storage": { "0x0": "0x1" }
            }
          ]
        }
        """;

    [Fact]
    public void Parse_AccountsObject_ReadsCodeBalanceStorage()
    {
        var r = WorkbenchPrestateLoader.Parse(Sample);
        Assert.True(r.Ok, r.Error);
        Assert.Single(r.Accounts);
        Assert.Equal("0x00000000000000000000000000000000000000bb", r.Accounts[0].AddressHex);
        Assert.Equal("1000", r.Accounts[0].BalanceWei);
        Assert.Equal(2ul, r.Accounts[0].Nonce);
        Assert.Contains("6001", r.Accounts[0].CodeHex);
        Assert.Equal("0x1", r.Accounts[0].StorageHex!["0x0"]);
    }

    [Fact]
    public void Parse_BareArray_Works()
    {
        var r = WorkbenchPrestateLoader.Parse(
            """[{ "address": "0x00000000000000000000000000000000000000cc", "code": "0x00" }]""");
        Assert.True(r.Ok, r.Error);
        Assert.Equal("0x00000000000000000000000000000000000000cc", r.Accounts[0].AddressHex);
    }

    [Fact]
    public void Vm_LoadPrestate_FeedsExtraAccountsOnRunOptions()
    {
        using var vm = new WorkbenchViewModel();
        var msg = vm.LoadPrestateJson(Sample, "sample.json");
        Assert.Contains("1 account", msg);
        Assert.Single(vm.PrestateAccountRows);
        Assert.Contains("00bb", vm.PrestateAccountRows[0]);
    }

    [Fact]
    public async Task ExtraAccount_CalleeCode_ExecutesViaStateTransition()
    {
        const string caller =
            "600060006000600060007300000000000000000000000000000000000000bb61fffff100";
        using var vm = new WorkbenchViewModel();
        vm.LoadPrestateJson(Sample, "bb.json");
        var run = await BytecodeExecutionService.RunAsync(caller, new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            ExtraAccounts = WorkbenchPrestateLoader.Parse(Sample).Accounts
        });
        Assert.NotNull(run);
        Assert.True(run!.Result.IsSuccess);
        Assert.Contains(run.Result.TraceSteps, s => s.Op == "CALL");
    }
}
