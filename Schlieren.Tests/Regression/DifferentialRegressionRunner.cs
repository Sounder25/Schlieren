using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Journal;
using Schlieren.Core.Security;
using Schlieren.UI.Services;

namespace Schlieren.Tests.Regression;

/// <summary>
/// Automated regression harness that validates execution invariants and compares
/// SCHLIEREN output against expected results from a golden corpus.
/// 
/// Catches:
/// - Gas accounting bugs (trace vs audit mismatch)
/// - Frame attribution errors (nested gas double-count)
/// - Diagnostic false positives/negatives
/// - Security classifier errors
/// </summary>
public sealed class DifferentialRegressionRunner
{
    /// <summary>
    /// Run a single regression case and validate all invariants.
    /// </summary>
    public static async Task<RegressionResult> RunCaseAsync(RegressionCase testCase)
    {
        var result = new RegressionResult { CaseName = testCase.Name };
        
        try
        {
            // Execute with SCHLIEREN
            var run = await BytecodeExecutionService.RunAsync(
                testCase.ContractCode,
                new BytecodeRunOptions
                {
                    ForkLabel = testCase.Fork,
                    ContractHex = testCase.ContractAddress,
                    CallDataHex = testCase.Calldata,
                    ExtraAccounts = testCase.PreState
                });

            if (run == null)
            {
                result.Status = RegressionStatus.ExecutionFailed;
                result.Message = "BytecodeExecutionService returned null";
                return result;
            }

            var trace = run.Result.TraceSteps;
            var journal = run.Result.Journal
                ?? throw new InvalidOperationException("Canonical execution did not produce a journal.");
            var journalAnalysis = JournalAnalysis.Build(journal);
            var securityFindings = JournalSecurityAnalyzer.Analyze(journalAnalysis);
            
            // Validate execution outcome
            if (testCase.ExpectedSuccess.HasValue)
            {
                if (run.Result.IsSuccess != testCase.ExpectedSuccess.Value)
                {
                    result.Status = RegressionStatus.OutcomeMismatch;
                    result.Message = $"Expected success={testCase.ExpectedSuccess.Value}, got {run.Result.IsSuccess} ({run.Result.Error})";
                    return result;
                }
            }

            // Validate gas
            if (testCase.ExpectedGas.HasValue)
            {
                if (run.Result.GasUsed != testCase.ExpectedGas.Value)
                {
                    result.Status = RegressionStatus.GasMismatch;
                    result.Message = $"Expected {testCase.ExpectedGas.Value} gas, got {run.Result.GasUsed}";
                    result.ExpectedGas = testCase.ExpectedGas.Value;
                    result.ActualGas = run.Result.GasUsed;
                    await SaveFailureTraceAsync(testCase, run, result);
                    return result;
                }
            }

            // Validate max depth
            if (testCase.ExpectedMaxDepth.HasValue)
            {
                var actualMaxDepth = trace.Max(s => s.Depth);
                if (actualMaxDepth != testCase.ExpectedMaxDepth.Value)
                {
                    result.Status = RegressionStatus.DepthMismatch;
                    result.Message = $"Expected max depth {testCase.ExpectedMaxDepth.Value}, got {actualMaxDepth}";
                    await SaveFailureTraceAsync(testCase, run, result);
                    return result;
                }
            }

            // Validate returndata
            if (!string.IsNullOrEmpty(testCase.ExpectedReturnData))
            {
                var actualReturnData = BytecodeExecutionService.ToHex(run.Result.ReturnData);
                if (!string.Equals(actualReturnData, testCase.ExpectedReturnData, StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = RegressionStatus.ReturnDataMismatch;
                    result.Message = $"Expected returndata {testCase.ExpectedReturnData}, got {actualReturnData}";
                    await SaveFailureTraceAsync(testCase, run, result);
                    return result;
                }
            }

            // INVARIANT #1: Exclusive journal gas must conserve against settlement.
            var gasTree = JournalGasTree.Build(journal, run.Result);
            if (!gasTree.Conservation.IsConserved)
            {
                result.Status = RegressionStatus.GasAccountingBug;
                result.Message = $"Journal gas conservation failed: settled={gasTree.Conservation.SettledGas}, derived={gasTree.Conservation.DerivedGas}, delta={gasTree.Conservation.Delta}";
                result.ExpectedGas = gasTree.Conservation.SettledGas;
                result.ActualGas = gasTree.Conservation.DerivedGas;
                await SaveFailureTraceAsync(testCase, run, result);
                return result;
            }

            // INVARIANT #2: Frame ownership is explicit and internally consistent.
            if (journalAnalysis.Frames.Values.Any(frame =>
                    frame.ParentId.HasValue && !journalAnalysis.Frames.ContainsKey(frame.ParentId.Value)))
            {
                result.Status = RegressionStatus.NestedGasDoubleCounting;
                result.Message = "Journal contains a frame whose explicit parent is missing.";
                await SaveFailureTraceAsync(testCase, run, result);
                return result;
            }

            // INVARIANT #3: DELEGATECALL must not trigger reentrancy.
            var delegatecallReentrancy = securityFindings.FirstOrDefault(finding =>
                finding.Category == SecurityCategory.Reentrancy &&
                journalAnalysis.Frames[finding.PrimaryFrameId].CallType == CallType.DelegateCall);
            if (delegatecallReentrancy is not null)
            {
                result.Status = RegressionStatus.ReentrancyFalsePositive;
                result.Message = $"DELEGATECALL frame {delegatecallReentrancy.PrimaryFrameId} falsely classified as reentrancy.";
                await SaveFailureTraceAsync(testCase, run, result);
                return result;
            }

            // Validate diagnostics
            if (testCase.ExpectedDiagnosticCount.HasValue)
            {
                var diagnostics = ProxyImplementationUnresolvedDetector.Analyze(trace);
                var libraryGuard = LibraryGuardDetector.Analyze(trace);
                var totalDiagnostics = (diagnostics != null ? 1 : 0) + (libraryGuard != null ? 1 : 0);
                
                if (totalDiagnostics != testCase.ExpectedDiagnosticCount.Value)
                {
                    result.Status = RegressionStatus.DiagnosticMismatch;
                    result.Message = $"Expected {testCase.ExpectedDiagnosticCount.Value} diagnostics, got {totalDiagnostics}";
                    return result;
                }
            }

            // Validate reentrancy findings
            if (testCase.ExpectedReentrancyCount.HasValue)
            {
                var reentrancyCount = securityFindings.Count(finding => finding.Category == SecurityCategory.Reentrancy);
                if (reentrancyCount != testCase.ExpectedReentrancyCount.Value)
                {
                    result.Status = RegressionStatus.ReentrancyMismatch;
                    result.Message = $"Expected {testCase.ExpectedReentrancyCount.Value} reentrancy findings, got {reentrancyCount}";
                    return result;
                }
            }

            // All checks passed
            result.Status = RegressionStatus.Pass;
            result.Message = $"✓ {trace.Count} steps, {run.Result.GasUsed} gas";
            result.ActualGas = run.Result.GasUsed;
        }
        catch (Exception ex)
        {
            result.Status = RegressionStatus.Exception;
            result.Message = $"Exception: {ex.Message}";
        }

        return result;
    }

    private static ulong ParseGasCost(string gasCostHex)
    {
        if (string.IsNullOrWhiteSpace(gasCostHex)) return 0UL;
        var clean = gasCostHex.StartsWith("0x") ? gasCostHex.Substring(2) : gasCostHex;
        return string.IsNullOrEmpty(clean) ? 0UL : Convert.ToUInt64(clean, 16);
    }

    /// <summary>
    /// Save complete failure trace with diagnostic context for manual inspection.
    /// </summary>
    private static async Task SaveFailureTraceAsync(RegressionCase testCase, WorkbenchRunResult run, RegressionResult result)
    {
        try
        {
            var artifactsDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "failures");
            Directory.CreateDirectory(artifactsDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var safeName = string.Concat(testCase.Name.Where(c => char.IsLetterOrDigit(c) || c == '_'));
            var tracePath = Path.Combine(artifactsDir, $"{safeName}_{timestamp}.trace.json");
            var summaryPath = Path.Combine(artifactsDir, $"{safeName}_{timestamp}.summary.txt");

            // Save full trace JSON
            var traceJson = System.Text.Json.JsonSerializer.Serialize(run.Result.TraceSteps, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(tracePath, traceJson);

            // Save human-readable summary
            var summary = $@"REGRESSION FAILURE: {testCase.Name}
Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
Status: {result.Status}

{result.Message}

Test Case:
  Contract: {testCase.ContractAddress}
  Calldata: {testCase.Calldata}
  Fork: {testCase.Fork}

Execution:
  Success: {run.Result.IsSuccess}
  Error: {run.Result.Error}
  Gas Used: {run.Result.GasUsed:N0}
  Steps: {run.Result.TraceSteps.Count}
  Max Depth: {run.Result.TraceSteps.Max(s => s.Depth)}
  Return Data: {BytecodeExecutionService.ToHex(run.Result.ReturnData)}

Expected:
  Success: {testCase.ExpectedSuccess}
  Gas: {testCase.ExpectedGas?.ToString("N0") ?? "unspecified"}
  Max Depth: {testCase.ExpectedMaxDepth?.ToString() ?? "unspecified"}
  Return Data: {testCase.ExpectedReturnData ?? "unspecified"}
  Diagnostics: {testCase.ExpectedDiagnosticCount?.ToString() ?? "unspecified"}
  Reentrancy: {testCase.ExpectedReentrancyCount?.ToString() ?? "unspecified"}

Artifacts:
  Full trace: {tracePath}
  Summary: {summaryPath}
";

            await File.WriteAllTextAsync(summaryPath, summary);

            result.FailureTracePath = tracePath;
            result.FailureSummaryPath = summaryPath;
        }
        catch
        {
            // Don't fail the test if artifact saving fails
        }
    }
}

/// <summary>
/// A single regression test case.
/// </summary>
public sealed class RegressionCase
{
    public required string Name { get; init; }
    public required string ContractCode { get; init; }
    public required string ContractAddress { get; init; }
    public string Calldata { get; init; } = "";
    public string Fork { get; init; } = "Cancun";
    public IReadOnlyList<WorkbenchAccountSeed>? PreState { get; init; }
    
    // Expected outcomes
    public bool? ExpectedSuccess { get; init; }
    public ulong? ExpectedGas { get; init; }
    public int? ExpectedMaxDepth { get; init; }
    public string? ExpectedReturnData { get; init; }
    public int? ExpectedDiagnosticCount { get; init; }
    public int? ExpectedReentrancyCount { get; init; }
}

/// <summary>
/// Result of running a regression case.
/// </summary>
public sealed class RegressionResult
{
    public required string CaseName { get; init; }
    public RegressionStatus Status { get; set; }
    public string Message { get; set; } = "";
    public ulong? ExpectedGas { get; set; }
    public ulong? ActualGas { get; set; }
    public string? FailureTracePath { get; set; }
    public string? FailureSummaryPath { get; set; }
}

public enum RegressionStatus
{
    Pass,
    ExecutionFailed,
    OutcomeMismatch,
    GasMismatch,
    DepthMismatch,
    ReturnDataMismatch,
    GasAccountingBug,
    NestedGasDoubleCounting,
    ReentrancyFalsePositive,
    ReentrancyMismatch,
    DiagnosticMismatch,
    Exception
}
