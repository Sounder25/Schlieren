using Schlieren.UI.Services;
using Xunit;

namespace Schlieren.Tests;

public sealed class WorkbenchStateTransitionTests
{
    [Fact(DisplayName = "Workbench contract-to-contract CALL uses StateTransition SubCall (no InternalError)")]
    public async Task ContractCall_DoesNotInternalError()
    {
        // Callee 0xbb: PUSH1 1 PUSH1 2 ADD PUSH1 0 MSTORE PUSH1 32 PUSH1 0 RETURN
        const string callee = "600160020160005260206000f3";
        // Caller: CALL 0xbb with gas 0xffff, then STOP
        const string caller =
            "600060006000600060007300000000000000000000000000000000000000bb61fffff100";

        var run = await BytecodeExecutionService.RunAsync(caller, new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            ContractHex = "0x00000000000000000000000000000000000000aa",
            ExtraAccounts =
            [
                new WorkbenchAccountSeed
                {
                    AddressHex = "0x00000000000000000000000000000000000000bb",
                    CodeHex = callee
                }
            ]
        });

        Assert.NotNull(run);
        Assert.NotEqual("InternalError", run!.Result.Error.ToString());
        Assert.True(run.Result.IsSuccess);
        Assert.Contains(run.Result.TraceSteps, s => s.Op == "CALL");
        Assert.True(run.Result.TraceSteps.Count > 8);
    }

    [Fact(DisplayName = "A call to the literal zero address is a message call, not CREATE")]
    public async Task LiteralZeroAddressTo_IsMessageCall_NotCreate()
    {
        // Regression test: ParseOptionalTo used to treat ANY all-zero hex as "no recipient"
        // (the CREATE signal), including the full 20-byte zero address — a legitimate
        // message-call target distinct from an absent 'to'.
        const string stop = "00";

        var run = await BytecodeExecutionService.RunAsync(stop, new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            ContractHex = "0x0000000000000000000000000000000000000000",
        });

        Assert.NotNull(run);
        Assert.False(run!.IsCreate);
        Assert.Equal("0x0000000000000000000000000000000000000000", run.ContractAddress, StringComparer.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "Short-form placeholders still mean CREATE (no regression)")]
    [InlineData("0x")]
    [InlineData("0x0")]
    [InlineData("0x00")]
    public async Task ShortFormPlaceholder_StillMeansCreate(string placeholder)
    {
        const string initcode = "600a600c600039600a6000f3600160020160005260206000f3"; // trivial init deploying a small runtime

        var run = await BytecodeExecutionService.RunAsync(initcode, new BytecodeRunOptions
        {
            ForkLabel = "Osaka",
            ContractHex = placeholder,
        });

        Assert.NotNull(run);
        Assert.True(run!.IsCreate);
    }
}
