using System.Collections.ObjectModel;
using Scrutor.Core.Forks;

namespace Scrutor.Core.Gas;

/// <summary>A complete immutable rule registry resolved for one fork.</summary>
public sealed class ForkGasSchedule
{
    private readonly IReadOnlyDictionary<GasRuleId, IGasRule> _rules;

    internal ForkGasSchedule(Fork fork, IDictionary<GasRuleId, IGasRule> rules)
    {
        Fork = fork;
        var snapshot = new Dictionary<GasRuleId, IGasRule>(rules);
        _rules = new ReadOnlyDictionary<GasRuleId, IGasRule>(snapshot);
        RuleIds = Array.AsReadOnly(snapshot.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray());
    }

    public Fork Fork { get; }
    public IReadOnlyCollection<GasRuleId> RuleIds { get; }

    public GasCalculation Calculate<TContext>(GasRuleId id, TContext context)
    {
        var rule = GetRequired(id);
        return rule.CalculateObject(context!, Fork);
    }

    public IGasRule GetRequired(GasRuleId id)
    {
        if (_rules.TryGetValue(id, out var rule))
            return rule;

        throw new GasScheduleException(
            $"Gas rule '{id}' is not registered for fork {Fork}.");
    }

    internal Dictionary<GasRuleId, IGasRule> CopyRules() => new(_rules);
}
