namespace Schlieren.Tests.Inspect;

/// <summary>
/// Shared Frontier CREATE fee-pair used by Core assembler tests and RPC debug_inspect tests.
/// Residual 32000 gas = TX.CREATE_SURCHARGE.
/// </summary>
public static class InspectGoldenCase
{
    public const string SenderHex = "0xf6c3a9edc1afa0ad5b720e4d42e1437c43d3b3ff";
    public const string CoinbaseHex = "0x2adc25665018aa1fe0e6bc666dac8fc2697ff9ba";
    public const string Fork = "Frontier";
    public const string GasPriceHex = "0xa";
    public const string GasHex = "0x186a0";
    public const string InitcodeHex = "0x6000";
    public const string SenderExpected = "0xf4240";
    public const string SenderActual = "0xa6040";
    public const string CoinbaseExpected = "0x0";
    public const string CoinbaseActual = "0x4e200";

    public static string SenderMismatch =>
        $"balance mismatch for {SenderHex}: expected={SenderExpected}, actual={SenderActual}";

    public static string CoinbaseMismatch =>
        $"balance mismatch for {CoinbaseHex}: expected={CoinbaseExpected}, actual={CoinbaseActual}";

    public static string[] Mismatches => [SenderMismatch, CoinbaseMismatch];

    public static string DebugInspectJsonRpc(int id = 1) =>
        $$"""
        {"jsonrpc":"2.0","id":{{id}},"method":"debug_inspect","params":[{
          "from":"{{SenderHex}}",
          "to":null,
          "data":"{{InitcodeHex}}",
          "gas":"{{GasHex}}",
          "value":"0x0",
          "gasPrice":"{{GasPriceHex}}",
          "fork":"{{Fork}}",
          "coinbase":"{{CoinbaseHex}}",
          "mismatches":[
            "{{SenderMismatch}}",
            "{{CoinbaseMismatch}}"
          ]
        }]}
        """;
}
