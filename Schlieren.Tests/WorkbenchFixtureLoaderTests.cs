using Schlieren.UI.Services;
using Schlieren.UI.ViewModels;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchFixtureLoaderTests
{
    [Fact]
    public void Parse_OfficialStateTest_ReadsPreTxAndPost()
    {
        var path = Path.Combine(
            @"C:\projects\Schlieren\state_tests\osaka\eip7825_transaction_gas_limit_cap",
            "test_transaction_gas_limit_cap.json");
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        Assert.True(WorkbenchFixtureLoader.LooksLikeStateTest(json));
        var parsed = WorkbenchFixtureLoader.Parse(json, "Osaka");
        Assert.True(parsed.Ok, parsed.Error);
        var fx = parsed.Fixture!;
        Assert.False(string.IsNullOrWhiteSpace(fx.SenderHex));
        Assert.NotEmpty(fx.PreAccounts);
        Assert.True(fx.GasLimit > 0);
        Assert.False(string.IsNullOrWhiteSpace(fx.Fork));
    }

    [Fact]
    public void Vm_ImportFixture_FillsTxAndPrestate()
    {
        var path = Path.Combine(
            @"C:\projects\Schlieren\state_tests\osaka\eip7825_transaction_gas_limit_cap",
            "test_transaction_gas_limit_cap.json");
        if (!File.Exists(path))
            return;

        using var vm = new WorkbenchViewModel();
        var msg = vm.ImportContractSource(File.ReadAllText(path), "cap.json");
        Assert.Contains("fixture", msg, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(vm.TxFrom);
        Assert.NotEmpty(vm.PrestateAccountRows);
    }

    [Fact]
    public void Quantity_ParsesHexAndDecimal()
    {
        Assert.True(WorkbenchQuantity.TryBigInteger("0x0a", out var hex));
        Assert.Equal(10, hex);
        Assert.True(WorkbenchQuantity.TryBigInteger("21", out var dec));
        Assert.Equal(21, dec);
    }

    [Fact]
    public void Parse_FixtureWithSecretKeyNoSender_FallsBackToPreAccount_NotRawKey()
    {
        // Standard EELS state-test shape: transaction carries "secretKey" (a 32-byte
        // private key) but no "sender" field. SenderHex must resolve to the funded
        // pre-state account's address, never to the raw private key.
        const string senderAddress = "0xae32ae2ec09b1a1521de4dbc0a846fcd40d9e1fd";
        const string secretKey = "0xffc684abd3b0e49d1021eee51257767465d91292f29c15df1e25e063daed8b5a";
        var json = $$"""
        {
          "tests/fake::test_no_sender_field[fork_Berlin-state_test]": {
            "env": {
              "currentCoinbase": "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba",
              "currentGasLimit": "0x07270e00",
              "currentNumber": "0x01",
              "currentTimestamp": "0x03e8",
              "currentDifficulty": "0x020000"
            },
            "pre": {
              "{{senderAddress}}": { "nonce": "0x00", "balance": "0x3635c9adc5dea00000", "code": "0x", "storage": {} }
            },
            "transaction": {
              "nonce": "0x00",
              "gasPrice": "0x0a",
              "gasLimit": ["0x0186a0"],
              "to": null,
              "value": ["0x00"],
              "data": ["0x"],
              "secretKey": "{{secretKey}}"
            },
            "post": {
              "Berlin": [
                {
                  "hash": "0x0000000000000000000000000000000000000000000000000000000000000",
                  "logs": "0x1dcc4de8dec75d7aab85b567b6ccd41ad312451b948a7413f0a142fd40d49347",
                  "indexes": { "data": 0, "gas": 0, "value": 0 },
                  "state": {}
                }
              ]
            }
          }
        }
        """;

        var parsed = WorkbenchFixtureLoader.Parse(json, "Berlin");
        Assert.True(parsed.Ok, parsed.Error);
        var fx = parsed.Fixture!;

        Assert.Equal(senderAddress, fx.SenderHex, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(secretKey, fx.SenderHex, StringComparer.OrdinalIgnoreCase);
        // A 32-byte private key must never end up where a 20-byte address is expected.
        Assert.True(fx.SenderHex.Length <= 42, $"SenderHex looks like a raw key, not an address: {fx.SenderHex}");
    }

    [Fact]
    public void Parse_MalformedPreAccountBalance_SurfacesError_NotSilentlyZero()
    {
        // Regression test: a present-but-unparseable pre-state account balance used to
        // silently become 0 instead of surfacing a load error — showing the user a
        // materially wrong pre-state with no indication the fixture data itself was bad.
        const string senderAddress = "0xae32ae2ec09b1a1521de4dbc0a846fcd40d9e1fd";
        var json = $$"""
        {
          "tests/fake::test_malformed_balance[fork_Berlin-state_test]": {
            "env": {
              "currentCoinbase": "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba",
              "currentGasLimit": "0x07270e00",
              "currentNumber": "0x01",
              "currentTimestamp": "0x03e8",
              "currentDifficulty": "0x020000"
            },
            "pre": {
              "{{senderAddress}}": { "nonce": "0x00", "balance": "not-a-number", "code": "0x", "storage": {} }
            },
            "transaction": {
              "nonce": "0x00",
              "gasPrice": "0x0a",
              "gasLimit": ["0x0186a0"],
              "to": null,
              "value": ["0x00"],
              "data": ["0x"],
              "sender": "{{senderAddress}}"
            },
            "post": { "Berlin": [ { "hash": "0x0", "logs": "0x0", "indexes": { "data": 0, "gas": 0, "value": 0 }, "state": {} } ] }
          }
        }
        """;

        var parsed = WorkbenchFixtureLoader.Parse(json, "Berlin");
        Assert.False(parsed.Ok);
        Assert.Contains("balance", parsed.Error, StringComparison.OrdinalIgnoreCase);
    }
}
