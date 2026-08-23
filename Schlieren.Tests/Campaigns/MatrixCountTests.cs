using Xunit;
using Xunit.Abstractions;

namespace Schlieren.Tests.Campaigns;

public class MatrixCountTests
{
    private readonly ITestOutputHelper _output;
    
    public MatrixCountTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void Matrix_GeneratesExpectedCaseCount()
    {
        var cases = CallSemanticsMatrixGenerator.GenerateMatrix();
        
        _output.WriteLine($"Total cases generated: {cases.Count}");
        
        // Group by category
        var baseline = cases.Count(c => c.Depth == 2 && 
                                        !c.PrecompileTarget.HasValue && 
                                        c.Value == CallSemanticsMatrixGenerator.ValueTransfer.Zero && 
                                        c.Behavior != CallSemanticsMatrixGenerator.ChildBehavior.MultipleWrites &&
                                        !c.GasLimit.HasValue);
        var valueTransfer = cases.Count(c => c.Value != CallSemanticsMatrixGenerator.ValueTransfer.Zero);
        var precompiles = cases.Count(c => c.PrecompileTarget.HasValue);
        var depths = cases.Count(c => c.Depth > 2);
        var multiWrites = cases.Count(c => c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.MultipleWrites);
        var gasBoundary = cases.Count(c => c.GasLimit.HasValue);
        var exotic = cases.Count(c => c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.SelfDestruct || 
                                      c.Behavior == CallSemanticsMatrixGenerator.ChildBehavior.Create);

        _output.WriteLine($"\nBreakdown:");
        _output.WriteLine($"  Baseline (depth 2, zero value): {baseline}");
        _output.WriteLine($"  Value transfer: {valueTransfer}");
        _output.WriteLine($"  Precompiles: {precompiles}");
        _output.WriteLine($"  Depth > 2: {depths}");
        _output.WriteLine($"  Multiple writes: {multiWrites}");
        _output.WriteLine($"  Gas boundary: {gasBoundary}");
        _output.WriteLine($"  Exotic (SELFDESTRUCT/CREATE): {exotic}");
        
        // Deduplication by canonical fingerprint consolidates semantically identical cases.
        // 137 unique cases after dedup (previously ~200 before dedup was added).
        Assert.InRange(cases.Count, 120, 160);
    }
}
