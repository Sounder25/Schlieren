using System.Reflection;
using Schlieren.Core.Execution;

namespace Schlieren.Tests.Execution;

public sealed class CanonicalExecutionArchitectureTests
{
    [Theory]
    [InlineData("ApplyTransactionWithGasTreeAsync")]
    [InlineData("ApplyTransactionWithFrameAsync")]
    public void StateTransition_HasNoDiagnosticEvaluator(string methodName)
    {
        Assert.Null(typeof(StateTransition).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Theory]
    [InlineData("Schlieren.Core.Execution.GasTreeFromTrace")]
    [InlineData("Schlieren.Core.Execution.GasTreeBuilder")]
    [InlineData("Schlieren.Core.Execution.GasFrameNode")]
    public void CoreAssembly_HasNoLegacyDiagnosticGasType(string typeName)
    {
        Assert.Null(typeof(StateTransition).Assembly.GetType(typeName));
    }

    [Fact]
    public void ExecutionContext_HasNoLegacyGasFrameSideChannel()
    {
        Assert.Null(typeof(Schlieren.Core.Execution.ExecutionContext).GetProperty(
            "GasFrame",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Fact]
    public void StateTransition_DoesNotThreadLegacyParentGasFrame()
    {
        var parameter = typeof(StateTransition)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(method => method.GetParameters())
            .FirstOrDefault(candidate => candidate.Name == "parentGasFrame");

        Assert.Null(parameter);
    }
}
