using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Schlieren.UI.ViewModels;

namespace Schlieren.UI.Services;

/// <summary>
/// Generates professional Markdown security and gas audit reports from active workspace state.
/// </summary>
public static class AuditReportExporter
{
    public static async Task<string> GenerateReportAsync(
        string activeFileTitle,
        string selectedFork,
        ulong blockGasLimit,
        ulong baseFeeGwei,
        int totalSteps,
        ulong totalGasUsed,
        IEnumerable<SecurityFindingViewModel> findings,
        IEnumerable<InstructionViewModel> instructions,
        string savePath)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# SCHLIEREN — Smart Contract Security & Gas Audit Report");
        sb.AppendLine();
        sb.AppendLine("*.NET 8 Ethereum Execution & Verification Engine*");
        sb.AppendLine();
        sb.AppendLine("Precise · Verifiable · Traceable · Conformant");
        sb.AppendLine();
        sb.AppendLine($"- **Target File / Context** : `{activeFileTitle}`");
        sb.AppendLine($"- **EVM Hard Fork**        : `{selectedFork}`");
        sb.AppendLine($"- **Block Gas Limit**      : `{blockGasLimit:N0}`");
        sb.AppendLine($"- **Base Fee**             : `{baseFeeGwei} Gwei`");
        sb.AppendLine($"- **Total Execution Steps**: `{totalSteps}`");
        sb.AppendLine($"- **Total Gas Used**       : `{totalGasUsed:N0}`");
        sb.AppendLine($"- **Report Generated**     : `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`");
        sb.AppendLine();

        // Security Findings
        var findingList = findings.ToList();
        sb.AppendLine("## Security Vulnerabilities & Findings");
        sb.AppendLine();
        if (findingList.Count == 0)
        {
            sb.AppendLine("✅ **No critical security vulnerabilities or proxy storage collisions detected.**");
        }
        else
        {
            sb.AppendLine("| Severity | Location | Description | Details |");
            sb.AppendLine("| :------- | :------- | :---------- | :------ |");
            foreach (var f in findingList)
            {
                sb.AppendLine($"| {f.SeverityEmoji} | `{f.LocationText}` | {f.Description} | {f.Details} |");
            }
        }
        sb.AppendLine();

        // Top Gas-Consuming Steps
        var instrList = instructions.ToList();
        var topGasSteps = instrList
            .OrderByDescending(i => i.GasCost)
            .Take(10)
            .ToList();

        sb.AppendLine("## Top Gas-Consuming Execution Opcodes");
        sb.AppendLine();
        if (topGasSteps.Count == 0)
        {
            sb.AppendLine("(no opcode trace available)");
        }
        else
        {
            sb.AppendLine("| PC | Opcode | Gas Cost | Frame Type |");
            sb.AppendLine("| :--- | :--- | -------: | :--- |");
            foreach (var step in topGasSteps)
            {
                sb.AppendLine($"| `0x{step.PC}` | `{step.Opcode}` | `{step.GasCost:N0}` | `{step.CallType}` |");
            }
        }
        sb.AppendLine();

        // Recommendation Summary
        sb.AppendLine("## Auditor Recommendations");
        sb.AppendLine();
        if (findingList.Any(f => f.Description.Contains("REENTRANCY", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("- 🔴 **Reentrancy Mitigation**: Apply the Checks-Effects-Interactions (CEI) pattern or use OpenZeppelin `ReentrancyGuard` (`nonReentrant` modifier) before making external state calls.");
        }
        if (findingList.Any(f => f.Description.Contains("STORAGE COLLISION", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("- ⚠️ **Storage Layout Safety**: Ensure ERC-1967 storage slots or explicit random slot offsets (`keccak256(...) - 1`) are used for proxy implementation variables.");
        }
        sb.AppendLine("- ⚡ **Gas Optimization**: Check COLD storage access opcodes (`SLOAD`/`SSTORE`) and consider pre-warming targets via EIP-2930 access lists.");
        sb.AppendLine();

        var reportContent = sb.ToString();
        await File.WriteAllTextAsync(savePath, reportContent, Encoding.UTF8);
        return reportContent;
    }
}
