using System.Globalization;
using System.Numerics;
using Schlieren.Core.Execution.Causal;

namespace Schlieren.Core.Execution.Inspect;

public static class InspectMapper
{
    public static int ParseGasDec(string? hexOrDec)
    {
        if (string.IsNullOrWhiteSpace(hexOrDec)) return 0;
        var s = hexOrDec.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return hex;
        return int.TryParse(hexOrDec.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)
            ? dec
            : 0;
    }

    public static string ToHex(ulong value) => "0x" + value.ToString("x", CultureInfo.InvariantCulture);

    public static string ToHex(long value)
        => value < 0 ? "0x0" : ToHex((ulong)value);

    public static string ToHex(BigInteger value)
        => value < 0 ? "0x0" : "0x" + value.ToString("x", CultureInfo.InvariantCulture);

    public static string ToHex(byte[]? data)
    {
        if (data is null || data.Length == 0) return "0x";
        return "0x" + Convert.ToHexString(data).ToLowerInvariant();
    }

    public static InspectStructLog FromStep(ExecutionTraceStep s) => new()
    {
        Pc = s.Pc,
        Op = s.Op ?? "",
        Gas = s.Gas ?? "0x0",
        GasCost = s.GasCost ?? "0x0",
        GasCostDec = ParseGasDec(s.GasCost),
        Depth = s.Depth < 1 ? 1 : s.Depth,
        Stack = s.Stack ?? new List<string>(),
        Memory = s.Memory ?? new List<string>(),
        Storage = s.Storage ?? new Dictionary<string, string>(),
        Contract = s.ContractAddress,
        Caller = s.CallerAddress,
        CallType = s.CallType?.ToString(),
        Output = s.OutputData is { Length: > 0 } ? ToHex(s.OutputData) : null
    };

    public static InspectGasNode FromTree(GasTreeNode n) => new()
    {
        Label = n.Label ?? "",
        Gas = n.Gas,
        TotalGas = n.TotalGas,
        Children = n.Children.Select(FromTree).ToList()
    };

    public static InspectDiagnosisHit FromHit(ScoredDiagnosis d) => new()
    {
        RuleId = d.RuleId,
        Title = d.Title,
        Grade = d.Grade switch
        {
            DiagnosisGrade.Proven => "PROVEN",
            DiagnosisGrade.Strong => "STRONG",
            _ => "POSSIBLE"
        },
        Score = d.Score,
        Phase = d.Phase.ToLabel(),
        Why = d.Why,
        Proof = d.Proof,
        Consequences = d.Consequences,
        LikelyFix = d.LikelyFix,
        CodeBoundary = d.CodeBoundary,
        ProtocolRule = d.ProtocolRule,
        GasDelta = d.GasDelta
    };

    public static InspectDiagnosis FromReport(CausalDiagnosisEngine.Report r) => new()
    {
        Fingerprint = r.Fingerprint,
        FirstPhase = r.FirstPhase.ToLabel(),
        Root = FromHit(r.Root),
        Candidates = r.Ranked.Skip(1).Select(FromHit).ToList()
    };
}
