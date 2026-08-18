using System;
using System.Threading.Tasks;
using Schlieren.UI.Services;
class S {
  static async Task<int> Main() {
    var r = await BytecodeExecutionService.RunAsync("600560030160005260206000f3", new BytecodeRunOptions {
      GasLimit = 100000, ChainId = 1, BaseFeeGwei = 2, ValueWei = "0",
      CallerHex = "0x0000000000000000000000000000000000000001",
      ContractHex = "0x00000000000000000000000000000000000000aa"
    });
    if (r is null) { Console.WriteLine("NULL"); return 1; }
    Console.WriteLine($"ok success={r.Result.IsSuccess} steps={r.Result.TraceSteps.Count} gas={r.Result.GasUsed} ret={BytecodeExecutionService.ToHex(r.Result.ReturnData)} callerBal={r.CallerBalanceWei}");
    return r.Result.IsSuccess && r.Result.TraceSteps.Count > 0 ? 0 : 2;
  }
}
