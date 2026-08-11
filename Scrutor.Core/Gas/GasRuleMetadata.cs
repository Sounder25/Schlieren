using Scrutor.Core.Forks;

namespace Scrutor.Core.Gas;

/// <summary>Protocol and source metadata carried by every gas calculation.</summary>
public sealed record GasRuleMetadata(
    GasRuleId RuleId,
    string Category,
    Fork ActivationFork,
    string ProtocolReference,
    string ImplementationBoundary);
