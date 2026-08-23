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
    public void DiscrepancyCategory_DoesNotDependOnRenderedText()
    {
        var discrepancy = new StateDiscrepancy
        {
            Kind = DiscrepancyKind.Balance,
            Detail = "arbitrary wording"
        };

        Assert.Equal("balance", discrepancy.Category);
    }

    [Fact]
    public void MissingTypedEvidence_CannotCreateProof()
    {
        var sender = Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
        var coin = Address.FromHex("0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba");
        var tx = new Transaction
        {
            From = sender,
            To = sender,
            GasPrice = 10,
            GasLimit = 100_000,
            Data = []
        };
        var evidence = FailureEvidenceFactory.From(
            "untyped", "Frontier", "fixture.json", tx, sender, coin,
            21_000, 0, true);

        var report = CausalDiagnosisEngine.Analyze(evidence);

        Assert.Equal(DiagnosisGrade.Possible, report.Root.Grade);
        Assert.Null(evidence.FeePairGas);
        Assert.False(evidence.HasBalanceMismatch);
        Assert.All(report.Ranked, candidate =>
            Assert.Equal("No typed discrepancy evidence was supplied.", candidate.Proof));
    }

    [Fact]
    public void FrontierCreate_SenderResidualWithoutFeePair_IsStrongNotProven()
    {
        var sender = Address.FromHex("0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff");
        var tx = new Transaction { From = sender, To = null, GasPrice = 10, GasLimit = 100_000, Data = new byte[32] };
        var discrepancy = new StateDiscrepancy
        {
            Kind = DiscrepancyKind.Balance,
            Address = sender,
            ExpectedNumber = 1_000_000,
            ActualNumber = 680_000
        };
        var evidence = FailureEvidenceFactory.From(
            "typed", "Frontier", "fixture.json", tx, sender, default,
            53_000, 0, true,
            discrepancies: [discrepancy]);

        var report = CausalDiagnosisEngine.Analyze(evidence);

        Assert.Equal("TX.CREATE_SURCHARGE", report.Root.RuleId);
        Assert.Equal(DiagnosisGrade.Strong, report.Root.Grade);
    }

    [Fact]
    public void CreateInitcodeWord_Frontier_IsProvenAndFingerprinted()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var init = new byte[24 * 32];
        var ev = Evidence(
            sender,
            fork: "Frontier",
            create: true,
            init,
            gasPrice: 1,
            senderExpected: 1_000_000,
            senderActual: 1_000_000 - 48); // overcharged 48 gas

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
            var ev = Evidence(sender, "Frontier", true, new byte[24 * 32], 1,
                1_000_000, 1_000_000 - 48);
            keys.Add(CausalDiagnosisEngine.Analyze(ev).Fingerprint);
        }
        Assert.Single(keys);
    }

    [Fact]
    public void P256Constant_IsInactiveOnFrontier()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var ev = Evidence(sender, "Frontier", false, Array.Empty<byte>(), 1,
            1_000_000, 1_000_000 + 6900);
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
            53_000, 0, true,
            discrepancies:
            [
                Balance(sender, 1_000_000, 680_000),
                Balance(coin, 0, 320_000)
            ]);

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
                50_000, 0, true,
                discrepancies:
                [
                    Balance(sender, 5_000_000, 4_680_000),
                    Balance(coin, 0, 320_000)
                ]);
            keys.Add(CausalDiagnosisEngine.Analyze(ev).Fingerprint);
        }
        Assert.Single(keys);
    }

    [Fact]
    public void ShanghaiCreate_DoesNotClaimPreactivation()
    {
        var sender = Address.FromHex("0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b");
        var ev = Evidence(sender, "Shanghai", true, new byte[24 * 32], 1,
            1_000_000, 1_000_000 - 48);
        var report = CausalDiagnosisEngine.Analyze(ev);
        Assert.NotEqual("CREATE.INITCODE_WORD", report.Root.RuleId);
    }

    private static FailureEvidence Evidence(
        Address sender, string fork, bool create, byte[] data, int gasPrice,
        long senderExpected, long senderActual)
    {
        var tx = new Transaction
        {
            From = sender,
            To = create ? null : Address.FromHex("0x0000000000000000000000000000000000000001"),
            GasPrice = gasPrice,
            GasLimit = 100_000,
            Data = data
        };
        var discrepancies = new List<StateDiscrepancy>
        {
            Balance(sender, senderExpected, senderActual)
        };
        if (create)
        {
            discrepancies.Add(new StateDiscrepancy
            {
                Kind = DiscrepancyKind.MissingAccount,
                Address = Address.FromHex("0x00000000000000000000000000000000000000aa")
            });
        }
        return FailureEvidenceFactory.From(
            "case", fork, $@"C:\fixtures\{fork.ToLowerInvariant()}\eip3860\test.json",
            tx, sender, default, 50_000, 0, false,
            lastOpcode: create ? "CREATE" : "STOP",
            discrepancies: discrepancies);
    }

    private static StateDiscrepancy Balance(Address address, BigInteger expected, BigInteger actual) => new()
    {
        Kind = DiscrepancyKind.Balance,
        Address = address,
        ExpectedNumber = expected,
        ActualNumber = actual
    };
}
