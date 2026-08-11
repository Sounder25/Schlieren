namespace Scrutor.Core.Gas;

/// <summary>Stable identifier for one executable gas rule.</summary>
public readonly record struct GasRuleId
{
    public GasRuleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Gas rule ID cannot be blank.", nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
