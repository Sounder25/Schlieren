using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Schlieren.Core.Execution;
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

            // INVARIANT #1: Audit gas must equal trace gas
            var auditGas = ComputeAuditGas(trace, testCase.Calldata);
            if (auditGas != run.Result.GasUsed)
            {
                result.Status = RegressionStatus.GasAccountingBug;
                result.Message = $"Gas double-count detected: trace={run.Result.GasUsed}, audit={auditGas}, delta={auditGas - run.Result.GasUsed}";
                result.ExpectedGas = run.Result.GasUsed;
                result.ActualGas = auditGas;
                await SaveFailureTraceAsync(testCase, run, result);
                return result;
            }

            // INVARIANT #2: No nested gas double-count
            var nestedDoubleCounting = DetectNestedGasDoubleCount(trace);
            if (nestedDoubleCounting.IsDetected)
            {
                result.Status = RegressionStatus.NestedGasDoubleCounting;
                result.Message = nestedDoubleCounting.Description;
                await SaveFailureTraceAsync(testCase, run, result);
                return result;
            }

            // INVARIANT #3: DELEGATECALL must not trigger reentrancy
            var delegatecallReentrancy = DetectDelegatecallReentrancyFalsePositive(trace);
            if (delegatecallReentrancy.HasFalsePositive)
            {
                result.Status = RegressionStatus.ReentrancyFalsePositive;
                result.Message = $"DELEGATECALL falsely classified as reentrancy at step {delegatecallReentrancy.StepIndex}";
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
                var reentrancy = ReentrancyDetector.Analyze(trace);
                if (reentrancy.Count != testCase.ExpectedReentrancyCount.Value)
                {
                    result.Status = RegressionStatus.ReentrancyMismatch;
                    result.Message = $"Expected {testCase.ExpectedReentrancyCount.Value} reentrancy findings, got {reentrancy.Count}";
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

    /// <summary>
    /// Compute audit gas the same way WorkbenchViewModel does (depth-1 only + intrinsic).
    /// </summary>
    private static ulong ComputeAuditGas(IReadOnlyList<ExecutionTraceStep> trace, string calldataHex)
    {
        // Calldata intrinsic
        ulong calldataGas = 0UL;
        if (BytecodeExecutionService.TryParseHexBytes(calldataHex, out var calldata) && calldata.Length > 0)
        {
            var nonzeroBytes = calldata.Count(b => b != 0);
            var zeroBytes = calldata.Length - nonzeroBytes;
            calldataGas = (ulong)(nonzeroBytes * 16 + zeroBytes * 4);
        }

        // Sum depth-1 gas only
        var depth1Gas = (ulong)trace.Where(s => s.Depth == 1)
                                    .Sum(s => (long)ParseGasCost(s.GasCost));

        return depth1Gas + 21_000UL + calldataGas;
    }

    private static ulong ParseGasCost(string gasCostHex)
    {
        if (string.IsNullOrWhiteSpace(gasCostHex)) return 0UL;
        var clean = gasCostHex.StartsWith("0x") ? gasCostHex.Substring(2) : gasCostHex;
        return string.IsNullOrEmpty(clean) ? 0UL : Convert.ToUInt64(clean, 16);
    }

    /// <summary>
    /// Detect if nested frame gas is being double-counted.
    /// </summary>
    private static (bool IsDetected, string Description) DetectNestedGasDoubleCount(IReadOnlyList<ExecutionTraceStep> trace)
    {
        // If no nested execution, can't double-count
        var maxDepth = trace.Max(s => s.Depth);
        if (maxDepth <= 1)
            return (false, "");

        // Check if any parent CALL includes child gas in its gasCost
        for (int i = 0; i < trace.Count; i++)
        {
            var step = trace[i];
            if (step.Op is "CALL" or "DELEGATECALL" or "STATICCALL" or "CALLCODE")
            {
                // Find child execution range
                var childStart = i + 1;
                var childEnd = childStart;
                while (childEnd < trace.Count && trace[childEnd].Depth > step.Depth)
                    childEnd++;

                if (childEnd > childStart)
                {
                    // Child execution exists
                    var childGas = (ulong)trace.Skip(childStart).Take(childEnd - childStart)
                                               .Sum(s => (long)ParseGasCost(s.GasCost));
                    var parentGas = ParseGasCost(step.GasCost);

                    // If parent gasCost includes child gas, that's expected (trace behavior)
                    // The bug would be if we then ALSO sum the child opcodes separately in audit
                    // This check is heuristic: if parent gas is suspiciously large, flag it
                    if (parentGas > 10000 && childGas > 100 && parentGas > childGas)
                    {
                        return (true, $"Parent {step.Op} at step {i} reports {parentGas} gas, includes {childGas} child gas");
                    }
                }
            }
        }

        return (false, "");
    }

    /// <summary>
    /// Detect if DELEGATECALL frames are being falsely classified as reentrancy.
    /// </summary>
    private static (bool HasFalsePositive, int StepIndex) DetectDelegatecallReentrancyFalsePositive(IReadOnlyList<ExecutionTraceStep> trace)
    {
        var reentrancy = ReentrancyDetector.Analyze(trace);
        
        foreach (var finding in reentrancy)
        {
            // Check if the reentry step is actually a DELEGATECALL child frame
            if (finding.ReentryStep < trace.Count)
            {
                var step = trace[finding.ReentryStep];
                if (step.CallType == CallType.DelegateCall)
                {
                    return (true, finding.ReentryStep);
                }
            }
        }

        return (false, -1);
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
