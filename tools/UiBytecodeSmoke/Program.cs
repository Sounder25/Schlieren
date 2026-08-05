using System;
using System.Threading.Tasks;
using Scrutor.UI.Services;
class S {
  static async Task<int> Main() {
    var r = await BytecodeExecutionService.RunAsync("600560030160005260206000f3", new BytecodeRunOptions { GasLimit = 100000, ChainId = 1, BaseFeeGwei = 2 });
    if (r is null) { Console.WriteLine("NULL"); return 1; }
    Console.WriteLine($"ok success={r.Value.IsSuccess} steps={r.Value.TraceSteps.Count} gas={r.Value.GasUsed}");
    return r.Value.IsSuccess && r.Value.TraceSteps.Count > 0 ? 0 : 2;
  }
}
