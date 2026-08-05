using System;
using System.IO;
using System.Threading.Tasks;
using Scrutor.UI.Services;
using Scrutor.UI.ViewModels;
using Xunit;

namespace Scrutor.Tests;

public sealed class AuditReportExporterTests
{
    [Fact(DisplayName = "AuditReportExporter generates valid Markdown report file with findings")]
    public async Task GenerateReportAsync_EmitsValidMarkdown()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"scrutor_audit_{Guid.NewGuid():N}.md");
        try
        {
            var findings = new[]
            {
                new SecurityFindingViewModel
                {
                    SeverityEmoji = "🔴",
                    Description = "REENTRANCY: Critical - Depth Delta 1",
                    Details = "Target: Vault.sol | Re-entered at step 23",
                    FileName = "Vault.sol",
                    LineNumber = 23,
                    StepIndex = 23
                }
            };

            var instructions = new[]
            {
                new InstructionViewModel("0000", "PUSH1", 3, "ROOT"),
                new InstructionViewModel("0002", "SLOAD", 2100, "ROOT")
            };

            var reportText = await AuditReportExporter.GenerateReportAsync(
                activeFileTitle: "Vault.sol",
                selectedFork: "Cancun",
                blockGasLimit: 30000000,
                baseFeeGwei: 1,
                totalSteps: 34,
                totalGasUsed: 42100,
                findings: findings,
                instructions: instructions,
                savePath: tempFile);

            Assert.True(File.Exists(tempFile));
            Assert.Contains("# SCRUTOR — Smart Contract Security & Gas Audit Report", reportText);
            Assert.Contains("Cancun", reportText);
            Assert.Contains("REENTRANCY", reportText);
            Assert.Contains("SLOAD", reportText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
