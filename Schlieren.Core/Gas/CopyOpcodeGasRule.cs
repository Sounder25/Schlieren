using Schlieren.Core.Execution;
using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>Base, copy-word, and memory expansion formula for copy opcodes.</summary>
public sealed class CopyOpcodeGasRule : IGasRule<MemoryGasContext>
{
    private static readonly MemoryExpansionGasRule ExpansionRule = new();
    private static readonly IReadOnlyDictionary<GasRuleId, CopyOpcodeGasRule> Rules =
        new Dictionary<GasRuleId, CopyOpcodeGasRule>
        {
            [new("OP.CALLDATACOPY")] = new("OP.CALLDATACOPY", Fork.Frontier),
            [new("OP.CODECOPY")] = new("OP.CODECOPY", Fork.Frontier),
            [new("OP.RETURNDATACOPY")] = new("OP.RETURNDATACOPY", Fork.Byzantium)
        };

    private CopyOpcodeGasRule(string id, Fork activation)
    {
        Metadata = new GasRuleMetadata(
            new GasRuleId(id),
            "Copy opcode",
            activation,
            "Ethereum Yellow Paper copy opcode groups",
            "ExecutionOpcodes.cs");
    }

    public GasRuleMetadata Metadata { get; }

    public static CopyOpcodeGasRule For(GasRuleId id)
    {
        if (Rules.TryGetValue(id, out var rule))
            return rule;
        throw new GasScheduleException($"Copy opcode gas rule '{id}' is not registered.");
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
                $"Copy memory expansion gas exceeds the host gas counter: {ex.Message}");
        }

        var words = checked((ulong)GasMath.WordCount(context.Length));
        var copyCharge = GasMath.MultiplyChecked(words, 3);
        var total = GasMath.AddChecked(
            GasMath.AddChecked(3, copyCharge),
            expansion.ChargedGas);

        return GasCalculation.Create(
            Metadata,
            fork,
            total,
            0,
            GasDisposition.Charge,
            new[]
            {
                new GasComponent("base", "Copy opcode base", GasComponentKind.Charge, 3),
                new GasComponent("copy-words", "Three gas per copied word", GasComponentKind.Charge, copyCharge),
                new GasComponent("memory-expansion", "Shared memory expansion", GasComponentKind.Charge, expansion.ChargedGas)
            },
            expansion.Decisions);
    }
}