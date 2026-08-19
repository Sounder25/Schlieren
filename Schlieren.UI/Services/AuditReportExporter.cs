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
        IEnumerable<DiagnosticFinding> diagnostics,
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
                sb.AppendLine($"| {f.SeverityEmoji} | `{EscapeMarkdownCell(f.LocationText)}` | {EscapeMarkdownCell(f.Description)} | {EscapeMarkdownCell(f.Details)} |");
            }
        }
        sb.AppendLine();

        // Execution Diagnostics
        var diagnosticList = diagnostics.ToList();
        if (diagnosticList.Count > 0)
        {
            sb.AppendLine("## Execution Diagnostics");
            sb.AppendLine();
            sb.AppendLine("*Execution context explanations — not security vulnerabilities*");
            sb.AppendLine();

            foreach (var d in diagnosticList)
            {
                var severityLabel = d.Severity switch
                {
                    DiagnosticSeverity.Info => "ℹ️ Info",
                    DiagnosticSeverity.Warning => "⚠️ Warning",
                    DiagnosticSeverity.Error => "❌ Error",
                    _ => "Info"
                };

                var confidenceLabel = d.Confidence switch
                {
                    DiagnosticConfidence.High => "High",
                    DiagnosticConfidence.Medium => "Medium",
                    DiagnosticConfidence.Low => "Low",
                    _ => "Medium"
                };

                sb.AppendLine($"### {d.Category} — {d.Title}");
                sb.AppendLine();
                sb.AppendLine($"**Severity:** {severityLabel} | **Confidence:** {confidenceLabel} | **Expected Behavior:** {(d.IsExpectedBehavior ? "Yes" : "No")}");
                sb.AppendLine();
                sb.AppendLine(d.Summary);
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(d.Detail))
                {
                    sb.AppendLine("**Details:**");
                    sb.AppendLine();
                    sb.AppendLine(d.Detail);
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(d.LikelyCause))
                {
                    sb.AppendLine($"**Likely Cause:** {d.LikelyCause}");
                    sb.AppendLine();
                }

                if (d.IsExpectedBehavior)
                {
                    sb.AppendLine("**Classification:** Expected execution-context behavior, not an implementation failure.");
                    sb.AppendLine();
                }
            }
        }

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
        
        bool hasRecommendations = false;
        
        if (findingList.Any(f => f.Description.Contains("REENTRANCY", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("- 🔴 **Reentrancy Mitigation**: Apply the Checks-Effects-Interactions (CEI) pattern or use OpenZeppelin `ReentrancyGuard` (`nonReentrant` modifier) before making external state calls.");
            hasRecommendations = true;
        }
        if (findingList.Any(f => f.Description.Contains("STORAGE COLLISION", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("- ⚠️ **Storage Layout Safety**: Ensure ERC-1967 storage slots or explicit random slot offsets (`keccak256(...) - 1`) are used for proxy implementation variables.");
            hasRecommendations = true;
        }
        
        // Only recommend storage optimization if SLOAD/SSTORE actually occurred
        var hasStorageOps = instrList.Any(i => 
            i.Opcode.Equals("SLOAD", StringComparison.OrdinalIgnoreCase) || 
            i.Opcode.Equals("SSTORE", StringComparison.OrdinalIgnoreCase));
        
        if (hasStorageOps)
        {
            sb.AppendLine("- ⚡ **Gas Optimization**: Storage operations detected. Check COLD storage access opcodes (`SLOAD`/`SSTORE`) and consider pre-warming targets via EIP-2930 access lists.");
            hasRecommendations = true;
        }
        
        if (!hasRecommendations)
        {
            sb.AppendLine("- ✅ **No specific recommendations**. Execution completed without detectable anti-patterns.");
        }
        
        sb.AppendLine();

        var reportContent = sb.ToString();
        await File.WriteAllTextAsync(savePath, reportContent, Encoding.UTF8);
        return reportContent;
    }

    /// <summary>
    /// Escapes a value for safe placement inside a Markdown table cell. A raw '|' is parsed
    /// as a column boundary and a raw newline breaks the row entirely — both occur in normal
    /// finding text (e.g. "Target: X | re-entry step Y"), not just adversarial input.
    /// </summary>
    private static string EscapeMarkdownCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("|", "\\|")
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>")
            .Replace("\r", "<br>");
    }
}
