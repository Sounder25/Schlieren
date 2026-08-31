using Schlieren.Core.Execution;

namespace Schlieren.Guard;

internal static class GuardMachine
{
    public static StateTransition CreatePipeline()
    {
        var opcodes = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!);
        return new StateTransition(new EvmMachine(opcodes));
    }
}
