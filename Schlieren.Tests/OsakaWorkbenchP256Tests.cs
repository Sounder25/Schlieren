using Schlieren.UI.Services;
using Xunit;

namespace Schlieren.Tests;

public sealed class OsakaWorkbenchP256Tests
{
    [Fact(DisplayName = "Osaka workbench run of EIP-7951 wrapper verifies official P-256 vector")]
    public async Task OsakaFork_P256Wrapper_ReturnsOne()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var code = await File.ReadAllTextAsync(Path.Combine(root, "muscle", "osaka-eip7951-p256verify-wrapper.hex"));
        var data = await File.ReadAllTextAsync(Path.Combine(root, "muscle", "osaka-p256-valid.calldata.hex"));

        var prague = await BytecodeExecutionService.RunAsync(code, new BytecodeRunOptions
        {
            ForkLabel = "Prague",
            CallDataHex = data,
            GasLimit = 10_000_000
        });
        Assert.NotNull(prague);
        // Prague: 0x0100 is not P256VERIFY — empty-account CALL via StateTransition.
        // Must not crash (InternalError). Returndata size is not 32.
        Assert.DoesNotContain(
            prague!.Result.TraceSteps.Last().Storage.Values,
            v => v.Contains("20", StringComparison.Ordinal));

        var osaka = await BytecodeExecutionService.RunAsync(code, new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            CallDataHex = data,
            GasLimit = 10_000_000
        });
        Assert.NotNull(osaka);
        Assert.True(osaka!.Result.IsSuccess);
        // Wrapper does not RETURN; it SSTOREs: slot0=call success, slot1=returndatasize.
        var last = osaka.Result.TraceSteps[^1];
        Assert.Contains(last.Storage, kv =>
            kv.Key.EndsWith("0", StringComparison.Ordinal) && kv.Value.Contains("1"));
        Assert.Contains(last.Storage, kv =>
            kv.Value.Contains("20") || kv.Value.Contains("32"));
    }
}
