namespace Schlieren.Core.Execution.Inspect;

public static class InspectMismatchFormat
{
    public static string Balance(string addressHex, string expectedHex, string actualHex)
        => $"balance mismatch for {addressHex}: expected={NormalizeHex(expectedHex)}, actual={NormalizeHex(actualHex)}";

    public static string Nonce(string addressHex, ulong expected, ulong actual)
        => $"nonce mismatch for {addressHex}: expected={expected}, actual={actual}";

    private static string NormalizeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "0x0";
        var s = hex.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s : "0x" + s;
    }
}
