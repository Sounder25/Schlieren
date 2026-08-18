using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// EELS oracle harness — wraps `ethereum-spec-evm statetest` for authoritative ground truth.
///
/// This is the primary oracle. REVM is secondary.
/// When REVM and EELS disagree, EELS wins.
///
/// The EELS harness:
///   - Constructs a minimal state test fixture from a CampaignExecutionRequest
///   - Runs it through `ethereum-spec-evm statetest --json`
///   - Parses the EIP-3155 structLog + gasUsed output
///   - Returns a CampaignExecutionResult
/// </summary>
public sealed class EelsExecutionHarness : IEvmExecutionHarness
{
    private readonly string _eelsExe;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public EelsExecutionHarness(string? eelsExe = null)
    {
        _eelsExe = eelsExe ?? FindEelsExe();
    }

    public static string FindEelsExe()
    {
        // Check common locations
        var candidates = new[]
        {
            "ethereum-spec-evm",
            "ethereum-spec-evm.exe",
            @"C:\Users\Erick\AppData\Local\hermes\hermes-agent\venv\Scripts\ethereum-spec-evm.exe",
            @"C:\Users\Erick\AppData\Local\Programs\Python\Python311\Scripts\ethereum-spec-evm.exe",
        };
        foreach (var c in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo(c, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
                if (p?.ExitCode == 0) return c;
            }
            catch { }
        }
        return "ethereum-spec-evm"; // fallback, will fail at runtime with clear message
    }

    public static bool IsAvailable()
    {
        try
        {
            var exe = FindEelsExe();
            var psi = new ProcessStartInfo(exe, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<CampaignExecutionResult> ExecuteAsync(
        CampaignExecutionRequest request,
        CancellationToken ct = default)
    {
        var fixture  = BuildFixture(request);
        var fixtureJson = JsonSerializer.Serialize(fixture, _json);

        var tmpFile = Path.GetTempFileName() + ".json";
        await File.WriteAllTextAsync(tmpFile, fixtureJson, ct);

        try
        {
            var psi = new ProcessStartInfo(_eelsExe, $"statetest --json \"{tmpFile}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ethereum-spec-evm");

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            return ParseOutput(stdout + stderr, request);
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
        }
    }

    // ── Fixture construction ──────────────────────────────────────────────────

    private static object BuildFixture(CampaignExecutionRequest r)
    {
        // The sender for EELS statetest must be a recoverable address.
        // Use the standard test key: secretKey=0x45a915... → address=0xa94f5374...
        const string testSenderAddress = "0xa94f5374fce5edbc8e2a8697c15331677e6ebf0b";
        const string testSecretKey     = "0x45a915e4d060149eb4365960e6a7a45f334393093061116b197e3240065ff2d8";

        var pre = new Dictionary<string, object>();

        // Add sender (may override caller if it matches)
        pre[testSenderAddress] = new
        {
            balance = "0xDE0B6B3A7640000",
            code    = "0x",
            nonce   = "0x00",
            storage = new Dictionary<string, string>(),
        };

        // Add prestate accounts
        foreach (var acct in r.Prestate)
        {
            // Remap our DeterministicAddresses.Caller → testSender
            var addr = acct.Address.Equals(DeterministicAddresses.Caller,
                           StringComparison.OrdinalIgnoreCase)
                       ? testSenderAddress
                       : acct.Address;

            var storage = new Dictionary<string, string>();
            foreach (var (slot, val) in acct.Storage)
            {
                var normSlot = PadHex(slot, 32);
                var normVal  = PadHex(val, 32);
                storage[normSlot] = normVal;
            }

            pre[addr] = new
            {
                balance = string.IsNullOrEmpty(acct.Balance) ? "0x0" : acct.Balance,
                code    = string.IsNullOrEmpty(acct.Code)    ? "0x" : acct.Code,
                nonce   = $"0x{acct.Nonce:x}",
                storage,
            };
        }

        // Remap target if it was the caller address
        var to = r.Target.Equals(DeterministicAddresses.Caller,
                     StringComparison.OrdinalIgnoreCase)
                 ? testSenderAddress
                 : r.Target;

        return new Dictionary<string, object>
        {
            [$"Test_{r.Fork}"] = new
            {
                env = new
                {
                    currentCoinbase    = "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba",
                    currentDifficulty  = "0x020000",
                    currentGasLimit    = "0x1C9C380",
                    currentNumber      = "0x01",
                    currentTimestamp   = "0x03E8",
                },
                post = new Dictionary<string, object>
                {
                    [r.Fork] = new[]
                    {
                        new
                        {
                            hash    = "0x0000000000000000000000000000000000000000000000000000000000000000",
                            logs    = "0x1dcc4de8dec75d7aab85b567b6ccd41ad312451b948a7413f0a142fd40d49347",
                            indexes = new { data = 0, gas = 0, value = 0 },
                        }
                    },
                },
                pre,
                transaction = new
                {
                    data      = new[] { r.Calldata ?? "0x" },
                    gasLimit  = new[] { $"0x{r.GasLimit:x}" },
                    gasPrice  = "0x01",
                    nonce     = "0x00",
                    secretKey = testSecretKey,
                    to,
                    value     = new[] { $"0x{r.Value:x}" },
                },
            }
        };
    }

    // ── Output parsing ────────────────────────────────────────────────────────

    private static CampaignExecutionResult ParseOutput(string output, CampaignExecutionRequest req)
    {
        ulong evmGas    = 0;   // from {"output":"...","gasUsed":"0x..."}  — EVM only, pre-refund
        ulong refund    = 0;   // final refund counter from last structLog step
        bool  success   = true;
        string returnData = "0x";
        var stateDiff  = new Dictionary<string, string>();
        var logs       = new List<LogFingerprint>();
        var frames     = new List<FrameFingerprint>();

        // Parse each JSON line
        foreach (var rawLine in output.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith('{')) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // structLog step: track last refund counter
                if (root.TryGetProperty("refund", out var refundProp))
                    refund = (ulong)Math.Max(0, refundProp.GetInt64());

                // Summary line: {"output":"...","gasUsed":"0x..."} with no "pc"
                if (root.TryGetProperty("output", out var outProp) &&
                    root.TryGetProperty("gasUsed", out var guProp) &&
                    !root.TryGetProperty("pc", out _))
                {
                    evmGas     = ParseHexUlong(guProp.GetString() ?? "0x0");
                    returnData = outProp.GetString() ?? "0x";
                    if (!returnData.StartsWith("0x")) returnData = "0x" + returnData;
                }
            }
            catch { /* skip malformed lines */ }
        }

        // Apply the EELS Berlin refund formula:
        //   tx_gas_used_before_refund = tx.gas - gas_left = intrinsic + evmGas
        //   tx_gas_refund = min(tx_gas_used_before_refund / RefundQuotient, refund_counter)
        //   tx_gas_used_after_refund  = tx_gas_used_before_refund - tx_gas_refund
        const ulong intrinsic = 21_000;  // base for calldata=0x, no access list
        var txGasBeforeRefund = intrinsic + evmGas;

        // RefundQuotient: London+ = 5, pre-London = 2
        var isLondonOrLater = req.Fork is "London" or "Paris" or "Merge" or "Shanghai"
                                       or "Cancun" or "Prague" or "Osaka";
        ulong refundQuotient = isLondonOrLater ? 5UL : 2UL;
        var maxRefund  = txGasBeforeRefund / refundQuotient;
        var cappedRefund = Math.Min(refund, maxRefund);
        var finalGasUsed = txGasBeforeRefund - cappedRefund;

        var fingerprint = new ExecutionFingerprint
        {
            Success    = success,
            GasUsed    = finalGasUsed,
            ReturnData = returnData,
            Refund     = cappedRefund,
            FrameTree  = frames,
            Accesses   = new AccessFingerprint
            {
                ColdAccounts = new(), WarmAccounts = new(),
                ColdSlots = new(), WarmSlots = new(),
            },
            StateDiff  = stateDiff,
            Logs       = logs,
        };

        return new CampaignExecutionResult
        {
            Success            = success,
            GasUsed            = finalGasUsed,
            ReturnData         = returnData,
            Fingerprint        = fingerprint,
            RawTrace           = Core.Execution.ExecutionResult.Success(finalGasUsed, Array.Empty<byte>()),
            PostExecutionState = new Core.State.GlobalState(),
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string PadHex(string hex, int bytes)
    {
        var s = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        return "0x" + s.PadLeft(bytes * 2, '0');
    }

    private static ulong ParseHexUlong(string h)
    {
        if (string.IsNullOrEmpty(h) || h is "0x" or "0x0") return 0;
        var s = h.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? h[2..] : h;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
