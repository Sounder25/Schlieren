using Schlieren.Tests.Campaigns;

var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
Console.WriteLine($"Total cases generated: {cases.Count}");

// Group by category
var baseline = cases.Count(c => c.CaseId.Contains("D2") && !c.CaseId.Contains("PRECOMPILE") && c.Value == CallSemanticsMatrixGenerator.ValueTransfer.Zero && c.Behavior != CallSemanticsMatrixGenerator.ChildBehavior.MultipleWrites);
var valueTransfer = cases.Count(c => c.Value != CallSemanticsMatrixGenerator.ValueTransfer.Zero);
var precompiles = cases.Count(c => c.PrecompileTarget.HasValue);
var depths = cases.Count(c => c.Depth > 2);
var multiWrites = cases.Count(c => c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.MultipleWrites);
var gasBoundary = cases.Count(c => c.GasLimit.HasValue);
var exotic = cases.Count(c => c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.SelfDestruct || c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.Create);

Console.WriteLine($"\nBreakdown:");
Console.WriteLine($"  Baseline (depth 2, zero value): {baseline}");
Console.WriteLine($"  Value transfer: {valueTransfer}");
Console.WriteLine($"  Precompiles: {precompiles}");
Console.WriteLine($"  Depth > 2: {depths}");
Console.WriteLine($"  Multiple writes: {multiWrites}");
Console.WriteLine($"  Gas boundary: {gasBoundary}");
Console.WriteLine($"  Exotic (SELFDESTRUCT/CREATE): {exotic}");
