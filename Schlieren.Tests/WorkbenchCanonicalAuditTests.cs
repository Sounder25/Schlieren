using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;

namespace Schlieren.Tests;

public sealed class WorkbenchCanonicalAuditTests
{
    [Fact]
    public async Task OsakaCalldataFloor_ReportUsesCanonicalSettledGas()
    {
        const string code = "00";
        var options = new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            CallDataHex = "0100",
            GasLimit = 100_000
        };
        var canonical = await BytecodeExecutionService.RunAsync(code, options);
        Assert.NotNull(canonical);

        using var vm = new WorkbenchViewModel
        {
            BytecodeInput = code,
            SelectedFork = options.ForkLabel,
            CallDataHex = options.CallDataHex,
            TxGasLimit = options.GasLimit
        };
        await vm.RunBytecodeCommand.ExecuteAsync(null);

        var path = Path.Combine(Path.GetTempPath(), $"schlieren-audit-{Guid.NewGuid():N}.md");
        try
        {
            await vm.GenerateAuditReportAsync(path);
            var report = await File.ReadAllTextAsync(path);

            Assert.Contains(
                $"**Total Gas Used**       : `{canonical!.Result.GasUsed:N0}`",
                report,
                StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
