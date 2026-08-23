using System.Collections.ObjectModel;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>Canonical calculations for opcodes whose price has no operands.</summary>
public static class FixedOpcodeGasSchedule
{
    private static readonly IReadOnlyDictionary<GasRuleId, ulong> Costs =
        new ReadOnlyDictionary<GasRuleId, ulong>(new Dictionary<GasRuleId, ulong>
        {
            [new("OP.ADD")] = 3,
            [new("OP.MUL")] = 5,
            [new("OP.SUB")] = 3,
            [new("OP.DIV")] = 5,
            [new("OP.SDIV")] = 5,
            [new("OP.MOD")] = 5,
            [new("OP.SMOD")] = 5,
            [new("OP.ADDMOD")] = 8,
            [new("OP.MULMOD")] = 8,
            [new("OP.SIGNEXTEND")] = 5
        });

    public static GasCalculation Calculate(GasRuleId id, Fork fork)
    {
        if (!Costs.TryGetValue(id, out var cost))
            throw new GasScheduleException($"Fixed opcode gas rule '{id}' is not registered for fork {fork}.");

        var metadata = new GasRuleMetadata(
            id,
            "Opcode",
            Fork.Frontier,
            "Ethereum Yellow Paper, Appendix G opcode groups",
            "EVM opcode execution");

        return GasCalculation.Create(
            metadata,
            fork,
            cost,
            0,
            GasDisposition.Charge,
            new[]
            {
                new GasComponent("fixed-opcode-cost", id.Value, GasComponentKind.Charge, cost)
            },
            Array.Empty<GasDecision>());
    }

    public static ulong Charge(string opcodeName, Fork fork) =>
        Calculate(new GasRuleId($"OP.{opcodeName}"), fork).ChargedGas;
}