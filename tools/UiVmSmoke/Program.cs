using System;
using System.Diagnostics;
using Scrutor.UI.ViewModels;
class T {
  static void Main() {
    Console.WriteLine("start " + DateTime.Now);
    var sw = Stopwatch.StartNew();
    try {
      var vm = new WorkbenchViewModel();
      Console.WriteLine("VM created in " + sw.ElapsedMilliseconds + "ms steps=" + vm.TotalSteps + " files=" + vm.ProjectFiles.Count);
    } catch (Exception ex) {
      Console.WriteLine("FAIL: " + ex);
    }
  }
}
