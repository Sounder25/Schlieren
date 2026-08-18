using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class ConformanceOpenWorkbenchTests
{
    [Fact]
    public void OpenFixturePath_RaisesJsonForWorkbench()
    {
        var path = Path.Combine(
            @"C:\projects\Schlieren\state_tests\osaka\eip7825_transaction_gas_limit_cap",
            "test_transaction_gas_limit_cap.json");
        if (!File.Exists(path))
            return;

        using var vm = new ConformanceViewModel();
        string? json = null;
        string? name = null;
        vm.OpenInWorkbenchRequested += (j, n, _, _) => { json = j; name = n; };

        Assert.True(vm.OpenFixturePath(path));
        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("\"pre\"", json, StringComparison.Ordinal);
        Assert.Contains("\"transaction\"", json, StringComparison.Ordinal);
        Assert.Equal(Path.GetFileName(path), name);
        Assert.Contains("workbench", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenFixturePath_MissingFile_FailsQuietly()
    {
        using var vm = new ConformanceViewModel();
        var fired = false;
        vm.OpenInWorkbenchRequested += (_, _, _, _) => fired = true;
        Assert.False(vm.OpenFixturePath(@"C:\does-not-exist-schlieren.json"));
        Assert.False(fired);
    }
}
