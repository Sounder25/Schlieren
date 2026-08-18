using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchFixtureLoaderTests
{
    [Fact]
    public void Parse_OfficialStateTest_ReadsPreTxAndPost()
    {
        var path = Path.Combine(
            @"C:\projects\Schlieren\state_tests\osaka\eip7825_transaction_gas_limit_cap",
            "test_transaction_gas_limit_cap.json");
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        Assert.True(WorkbenchFixtureLoader.LooksLikeStateTest(json));
        var parsed = WorkbenchFixtureLoader.Parse(json, "Osaka");
        Assert.True(parsed.Ok, parsed.Error);
        var fx = parsed.Fixture!;
        Assert.False(string.IsNullOrWhiteSpace(fx.SenderHex));
        Assert.NotEmpty(fx.PreAccounts);
        Assert.True(fx.GasLimit > 0);
        Assert.False(string.IsNullOrWhiteSpace(fx.Fork));
    }

    [Fact]
    public void Vm_ImportFixture_FillsTxAndPrestate()
    {
        var path = Path.Combine(
            @"C:\projects\Schlieren\state_tests\osaka\eip7825_transaction_gas_limit_cap",
            "test_transaction_gas_limit_cap.json");
        if (!File.Exists(path))
            return;

        using var vm = new WorkbenchViewModel();
        var msg = vm.ImportContractSource(File.ReadAllText(path), "cap.json");
        Assert.Contains("fixture", msg, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(vm.TxFrom);
        Assert.NotEmpty(vm.PrestateAccountRows);
    }

    [Fact]
    public void Quantity_ParsesHexAndDecimal()
    {
        Assert.True(WorkbenchQuantity.TryBigInteger("0x0a", out var hex));
        Assert.Equal(10, hex);
        Assert.True(WorkbenchQuantity.TryBigInteger("21", out var dec));
        Assert.Equal(21, dec);
    }
}
