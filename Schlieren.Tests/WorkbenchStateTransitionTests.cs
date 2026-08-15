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
}
