using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>Non-generic rule contract used by immutable schedule registries.</summary>
public interface IGasRule
{
    GasRuleMetadata Metadata { get; }
    Type ContextType { get; }
    GasCalculation CalculateObject(object context, Fork fork);
}

/// <summary>Strongly typed executable gas formula.</summary>
public interface IGasRule<in TContext> : IGasRule
{
    GasCalculation Calculate(TContext context, Fork fork);

    Type IGasRule.ContextType => typeof(TContext);

    GasCalculation IGasRule.CalculateObject(object context, Fork fork)
    {
        if (context is not TContext typedContext)
        {
            throw new GasScheduleException(
                $"Gas rule '{Metadata.RuleId}' on {fork} requires context " +
                $"'{typeof(TContext).FullName}', but received '{context?.GetType().FullName ?? "null"}'.");
        }

        return Calculate(typedContext, fork);
    }
}