using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Verifies that EELS (ethereum-spec-evm) and Schlieren agree on
/// the Berlin SSTORE clear case that exposed REVM's refund bug.
///
/// S3A-3037: Berlin, CALL, SStore, XToZero (pre=0xAA, write=0x00)
///   Schlieren = 14314  ✓
///   REVM      = 23828  ✗ (does not apply REFUND_STORAGE_CLEAR under Berlin)
///   EELS      = 14314  ✓
/// </summary>
public sealed class EelsOracleVerification
{
    private readonly ITestOutputHelper _out;
    public EelsOracleVerification(ITestOutputHelper output) => _out = output;

    private static Core.Execution.EvmMachine BuildMachine() =>
        new(typeof(Core.Execution.IOpcode).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        typeof(Core.Execution.IOpcode).IsAssignableFrom(t))
            .Select(t => (Core.Execution.IOpcode)System.Activator.CreateInstance(t)!)
            .ToList());

    [Fact]
    public async Task Berlin_XToZero_Schlieren_Matches_EELS()
    {
        if (!EelsExecutionHarness.IsAvailable())
        {
            _out.WriteLine("SKIP: ethereum-spec-evm not available");
            return;
        }

        var machine  = BuildMachine();
        var pipeline = new Core.Execution.StateTransition(machine);
        var schlieren = new SchlierenExecutionHarness(pipeline);
        var eels      = new EelsExecutionHarness();

        var request = new CampaignExecutionRequest
        {
            Fork     = "Berlin",
            Caller   = DeterministicAddresses.Caller,
            Target   = DeterministicAddresses.Parent,
            Calldata = "0x",
            Value    = 0,
            GasLimit = 10_000_000,
            Prestate = new[]
            {
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Parent,
                    Code    =
                        "0x6000600060006000600073" +
                        "00000000000000000000000000000000000000bb" +
                        "5af15000",
                    Balance = "0xDE0B6B3A7640000", Nonce = 0,
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Child,
                    Code    = "0x600060005500",
                    Balance = "0xDE0B6B3A7640000", Nonce = 0,
                    Storage = new Dictionary<string, string> { ["0x0"] = "0xAA" },
                },
                new CampaignAccount
                {
                    Address = DeterministicAddresses.Caller,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0,
                },
            }
        };

        var sResult = await schlieren.ExecuteAsync(request);
        var eResult = await eels.ExecuteAsync(request);

        _out.WriteLine($"Schlieren gasUsed = {sResult.GasUsed}");
        _out.WriteLine($"EELS     gasUsed = {eResult.GasUsed}");
        _out.WriteLine($"Delta            = {(long)sResult.GasUsed - (long)eResult.GasUsed:+#;-#;0}");

        Assert.Equal(eResult.GasUsed, sResult.GasUsed);
    }
}
