using Scrutor.Core.Forks;

namespace Scrutor.Core.Gas;

/// <summary>Builds a resolved schedule from an empty fork or a parent overlay.</summary>
public sealed class ForkGasScheduleBuilder
{
    private readonly Fork _fork;
    private readonly Dictionary<GasRuleId, IGasRule> _rules;

    private ForkGasScheduleBuilder(Fork fork, Dictionary<GasRuleId, IGasRule> rules)
    {
        _fork = fork;
        _rules = rules;
    }

    public static ForkGasScheduleBuilder Empty(Fork fork) =>
        new(fork, new Dictionary<GasRuleId, IGasRule>());

    public static ForkGasScheduleBuilder From(ForkGasSchedule parent, Fork fork)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (fork <= parent.Fork)
        {
            throw new GasScheduleException(
                $"Child fork {fork} must be later than parent fork {parent.Fork}.");
        }

        return new ForkGasScheduleBuilder(fork, parent.CopyRules());
    }

    public ForkGasScheduleBuilder Set(IGasRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules[rule.Metadata.RuleId] = rule;
        return this;
    }

    public ForkGasSchedule Build() => new(_fork, _rules);
}
