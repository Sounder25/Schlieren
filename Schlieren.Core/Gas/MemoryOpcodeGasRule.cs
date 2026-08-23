using Schlieren.Core.Execution;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>Base plus shared expansion formula for MLOAD/MSTORE/MSTORE8.</summary>
public sealed class MemoryOpcodeGasRule : IGasRule<MemoryGasContext>
{
    private static readonly MemoryExpansionGasRule ExpansionRule = new();
    private static readonly IReadOnlyDictionary<GasRuleId, MemoryOpcodeGasRule> Rules =
        new Dictionary<GasRuleId, MemoryOpcodeGasRule>
        {
            [new("OP.MLOAD")] = new("OP.MLOAD"),
            [new("OP.MSTORE")] = new("OP.MSTORE"),
            [new("OP.MSTORE8")] = new("OP.MSTORE8")
        };

    private MemoryOpcodeGasRule(string id)
    {
        Metadata = new GasRuleMetadata(
            new GasRuleId(id),
            "Memory opcode",
            Fork.Frontier,
            "Ethereum Yellow Paper memory opcode groups",
            "MemoryOpcodes.cs");
    }

    public GasRuleMetadata Metadata { get; }

    public static MemoryOpcodeGasRule For(GasRuleId id)
    {
        if (Rules.TryGetValue(id, out var rule))
            return rule;

        throw new GasScheduleException($"Memory opcode gas rule '{id}' is not registered.");
    }

    public GasCalculation Calculate(MemoryGasContext context, Fork fork)
    {
        GasCalculation expansion;
        try
        {
            expansion = ExpansionRule.Calculate(context, fork);
        }
        catch (OverflowException ex)
        {
            throw new EvmOutOfGasException(
                $"Memory expansion gas exceeds the host gas counter: {ex.Message}");
        }

        var total = GasMath.AddChecked(3, expansion.ChargedGas);
        return GasCalculation.Create(
            Metadata,
            fork,
            total,
            0,
            GasDisposition.Charge,
            new[]
            {
                new GasComponent("base", "Memory opcode base", GasComponentKind.Charge, 3),
                new GasComponent("memory-expansion", "Shared memory expansion", GasComponentKind.Charge, expansion.ChargedGas)
            },
            expansion.Decisions);
    }
}