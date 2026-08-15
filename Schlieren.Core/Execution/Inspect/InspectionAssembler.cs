using Schlieren.Core.Execution.Causal;

namespace Schlieren.Core.Execution.Inspect;

/// <summary>
/// The only Core entry RPC may call. Builds inspect JSON from one canonical run.
/// </summary>
public static class InspectionAssembler
{
    public static InspectResult FromCanonical(InspectRequest request, ExecutionResult result)
    {
        var fork = request.Block.Rules.Fork.ToString();
        var last = result.TraceSteps is { Count: > 0 } ? result.TraceSteps[^1] : null;

        var ev = FailureEvidenceFactory.From(
            caseId: "inspect",
            forkName: fork,
            fixturePath: "",
            tx: request.Tx,
            sender: request.Tx.From,
            coinbase: request.Block.Coinbase,
            mismatches: request.Mismatches,
            gasUsed: result.GasUsed,
            refundCounter: result.GasRefundCounter,
            executionSucceeded: result.IsSuccess,
            error: result.Error,
            lastOpcode: last?.Op,
            lastPc: last?.Pc ?? 0,
            expectException: request.ExpectException,
            expectedReceiptSuccess: request.ExpectedReceiptSuccess);

        var report = CausalDiagnosisEngine.Analyze(ev);
        var diagnosis = InspectMapper.FromReport(report);
        if (request.Mismatches.Count == 0 &&
            diagnosis.Root is { Grade: "PROVEN" } proven)
        {
            diagnosis = new InspectDiagnosis
            {
                Fingerprint = diagnosis.Fingerprint,
                FirstPhase = diagnosis.FirstPhase,
                Root = new InspectDiagnosisHit
                {
                    RuleId = proven.RuleId,
                    Title = proven.Title,
                    Grade = "STRONG",
                    Score = proven.Score,
                    Phase = proven.Phase,
                    Why = proven.Why,
                    Proof = proven.Proof,
                    Consequences = proven.Consequences,
                    LikelyFix = proven.LikelyFix,
                    CodeBoundary = proven.CodeBoundary,
                    ProtocolRule = proven.ProtocolRule,
                    GasDelta = proven.GasDelta
                },
                Candidates = diagnosis.Candidates
            };
        }

        var logs = new List<InspectStructLog>(result.TraceSteps?.Count ?? 0);
        if (result.TraceSteps is { Count: > 0 })
        {
            foreach (var step in result.TraceSteps)
            {
                var dto = InspectMapper.FromStep(step);
                if (request.DisableStack || request.DisableMemory || request.DisableStorage)
                {
                    dto = new InspectStructLog
                    {
                        Pc = dto.Pc,
                        Op = dto.Op,
                        Gas = dto.Gas,
                        GasCost = dto.GasCost,
                        GasCostDec = dto.GasCostDec,
                        Depth = dto.Depth,
                        Stack = request.DisableStack ? new List<string>() : dto.Stack,
                        Memory = request.DisableMemory ? new List<string>() : dto.Memory,
                        Storage = request.DisableStorage ? new Dictionary<string, string>() : dto.Storage,
                        Contract = dto.Contract,
                        Caller = dto.Caller,
                        CallType = dto.CallType,
                        Output = dto.Output
                    };
                }
                logs.Add(dto);
            }
        }

        var tree = GasTreeFromTrace.FromCanonical(request.Tx, request.Block.Rules, result);

        return new InspectResult
        {
            Ok = true,
            Fork = fork,
            Execution = new InspectExecution
            {
                Success = result.IsSuccess,
                Error = result.Error.ToString(),
                GasUsed = InspectMapper.ToHex(result.GasUsed),
                GasLimit = InspectMapper.ToHex(request.Tx.GasLimit),
                RefundCounter = InspectMapper.ToHex(result.GasRefundCounter),
                ReturnValue = InspectMapper.ToHex(result.ReturnData)
            },
            Trace = new InspectTrace { StructLogs = logs },
            GasTree = InspectMapper.FromTree(tree),
            Diagnosis = diagnosis
        };
    }
}
