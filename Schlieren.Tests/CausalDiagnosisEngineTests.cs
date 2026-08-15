using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;
using Xunit;

namespace Schlieren.Tests;

public sealed class CausalDiagnosisEngineTests
{
    [Fact]
    public void CreateInitcodeWord_Frontier_IsProvenAndFingerprinted()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var created = Address.FromHex("0x00000000000000000000000000000000000000aa");
        var init = new byte[24 * 32];
        var ev = Evidence(
            sender,
            fork: "Frontier",
            create: true,
            init,
            gasPrice: 1,
            senderExpected: 1_000_000,
            senderActual: 1_000_000 - 48, // overcharged 48 gas
            mismatches:
            [
                $"balance mismatch for {sender}: expected=0xf4240, actual=0xf4210",
                $"missing account in actual state: {created}",
                "receipt.status mismatch: expected=True, actual=False"
            ]);

        var report = CausalDiagnosisEngine.Analyze(ev);
        Assert.Equal("CREATE.INITCODE_WORD", report.Root.RuleId);
        Assert.Equal(DiagnosisGrade.Proven, report.Root.Grade);
        Assert.Equal(ExecutionPhase.GasCharge, report.FirstPhase);
        Assert.Contains("Frontier", report.Fingerprint, StringComparison.Ordinal);
        Assert.Contains("CREATE.INITCODE_WORD", report.Fingerprint, StringComparison.Ordinal);
        Assert.Contains("EIP-3860", report.Root.Why, StringComparison.Ordinal);
        Assert.Contains("DIRECT EFFECT", report.Root.Consequences, StringComparison.Ordinal);
        Assert.DoesNotContain("account cleanup", report.Root.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FourCreateFixtures_ShareOneFingerprint()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 4; i++)
        {
            var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
            var created = Address.FromHex($"0x00000000000000000000000000000000000000a{i}");
            var ev = Evidence(sender, "Frontier", true, new byte[24 * 32], 1,
                1_000_000, 1_000_000 - 48,
                [
                    $"balance mismatch for {sender}: expected=0xf4240, actual=0xf4210",
                    $"missing account in actual state: {created}"
                ]);
            keys.Add(CausalDiagnosisEngine.Analyze(ev).Fingerprint);
        }
        Assert.Single(keys);
    }

    [Fact]
    public void P256Constant_IsInactiveOnFrontier()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var ev = Evidence(sender, "Frontier", false, Array.Empty<byte>(), 1,
            1_000_000, 1_000_000 + 6900,
            [$"balance mismatch for {sender}: expected=0xf4240, actual=0xf5d34"]);
        var report = CausalDiagnosisEngine.Analyze(ev);
        Assert.DoesNotContain(report.Ranked, d => d.RuleId == "PRECOMPILE.P256VERIFY" && d.Score >= 40);
    }

    [Fact]
    public void FrontierFeePair_32000_IsProvenCreateSurcharge()
    {
        var sender = Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
        var coin = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
        var tx = new Transaction
        {
            From = sender,
            To = null,
            GasPrice = 10,
            GasLimit = 100_000,
            Data = new byte[32]
        };
        var ev = FailureEvidenceFactory.From(
            "front-create", "Frontier",
            @"C:\fixtures\frontier\stCreateTest\test.json",
            tx, sender, coin,
            [
                $"balance mismatch for {sender}: expected=0xf4240, actual=0xa6040",
                $"balance mismatch for {coin}: expected=0x0, actual=0x4e200"
            ],
            53_000, 0, true);

        var report = CausalDiagnosisEngine.Analyze(ev);
        Assert.Equal("TX.CREATE_SURCHARGE", report.Root.RuleId);
        Assert.Equal(DiagnosisGrade.Proven, report.Root.Grade);
        Assert.Equal(ExecutionPhase.IntrinsicGas, report.FirstPhase);
        Assert.Contains("INTRINSIC", report.Fingerprint, StringComparison.Ordinal);
        Assert.Contains("32000", report.Root.Proof, StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup", report.Root.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FourFrontierSurchargeCases_ShareFingerprint()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var sender = Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
        var coin = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
        for (var i = 0; i < 4; i++)
        {
            var tx = new Transaction { From = sender, To = null, GasPrice = 10, GasLimit = 100_000, Data = new byte[32 + i] };
            var ev = FailureEvidenceFactory.From(
                "c" + i, "Frontier", @"C:\fixtures\frontier\stCreateTest\a.json",
                tx, sender, coin,
                [
                    $"balance mismatch for {sender}: expected=0x4c4b40, actual=0x476ae0",
                    $"balance mismatch for {coin}: expected=0x0, actual=0x4e200"
                ],
                50_000, 0, true);
            keys.Add(CausalDiagnosisEngine.Analyze(ev).Fingerprint);
        }
        Assert.Single(keys);
    }

    [Fact]
    public void ShanghaiCreate_DoesNotClaimPreactivation()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var ev = Evidence(sender, "Shanghai", true, new byte[24 * 32], 1,
            1_000_000, 1_000_000 - 48,
            [
                $"balance mismatch for {sender}: expected=0xf4240, actual=0xf4210",
                "missing account in actual state: 0x00000000000000000000000000000000000000aa"
            ]);
        var report = CausalDiagnosisEngine.Analyze(ev);
        Assert.NotEqual("CREATE.INITCODE_WORD", report.Root.RuleId);
    }

    private static FailureEvidence Evidence(
        Address sender, string fork, bool create, byte[] data, int gasPrice,
        long senderExpected, long senderActual, string[] mismatches)
    {
        var tx = new Transaction
        {
            From = sender,
            To = create ? null : Address.FromHex("0x0000000000000000000000000000000000000001"),
            GasPrice = gasPrice,
            GasLimit = 100_000,
            Data = data
        };
        return FailureEvidenceFactory.From(
            "case", fork, $@"C:\fixtures\{fork.ToLowerInvariant()}\eip3860\test.json",
            tx, sender, default, mismatches, 50_000, 0, false,
            lastOpcode: create ? "CREATE" : "STOP");
    }
}
