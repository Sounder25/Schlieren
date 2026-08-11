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
        if (rule.Metadata.ActivationFork > _fork)
        {
            throw new GasScheduleException(
                $"Gas rule '{rule.Metadata.RuleId}' activates at {rule.Metadata.ActivationFork} " +
                $"and cannot be registered for earlier fork {_fork}.");
        }

        _rules[rule.Metadata.RuleId] = rule;
        return this;
    }

    public ForkGasScheduleBuilder Remove(GasRuleId id)
    {
        _rules.Remove(id);
        return this;
    }

    public ForkGasSchedule Build() => Build(GasCoverageManifest.Empty);

    public ForkGasSchedule Build(GasCoverageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var missing = manifest.RequiredRuleIds
            .Where(id => !_rules.ContainsKey(id))
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new GasScheduleException(
                $"Fork {_fork} is missing required gas rules: " +
                string.Join(", ", missing.Select(id => id.Value)));
        }

        return new ForkGasSchedule(_fork, _rules, manifest);
    }
}
