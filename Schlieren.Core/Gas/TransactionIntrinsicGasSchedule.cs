using Schlieren.Core.Forks;

namespace Schlieren.Core.Gas;

/// <summary>Executable, per-rule breakdown of transaction intrinsic gas.</summary>
public static class TransactionIntrinsicGasSchedule
{
    public static IReadOnlyList<GasCalculation> Calculate(
        TransactionGasContext context,
        IForkRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var definitions = new[]
        {
            Rule("TX.BASE", Fork.Frontier, 21_000, "Transaction base cost"),
            Rule("TX.CREATE_SURCHARGE", Fork.Homestead,
                context.IsContractCreation ? 32_000UL : 0UL,
                "Contract-creation transaction surcharge"),
            Rule("TX.CALLDATA_ZERO", Fork.Frontier,
                GasMath.MultiplyChecked(context.CalldataZeroBytes, rules.CalldataZeroByteCost),
                "Zero calldata bytes"),
            Rule("TX.CALLDATA_NONZERO", Fork.Frontier,
                GasMath.MultiplyChecked(context.CalldataNonZeroBytes, rules.CalldataNonZeroByteCost),
                "Non-zero calldata bytes"),
            Rule("TX.ACCESS_LIST_ADDRESS", Fork.Berlin,
                GasMath.MultiplyChecked(context.AccessListAddresses, 2_400),
                "Access-list addresses"),
            Rule("TX.ACCESS_LIST_STORAGE_KEY", Fork.Berlin,
                GasMath.MultiplyChecked(context.AccessListStorageKeys, 1_900),
                "Access-list storage keys"),
            Rule("TX.INITCODE_WORD", Fork.Shanghai,
                context.IsContractCreation
                    ? checked((ulong)GasMath.WordCount(context.CalldataZeroBytes + context.CalldataNonZeroBytes) * 2)
                    : 0,
                "Contract initcode words"),
            Rule("TX.AUTHORIZATION_COST", Fork.Prague,
                context.TransactionType == 4
                    ? GasMath.MultiplyChecked(context.AuthorizationCount, 25_000)
                    : 0,
                "EIP-7702 authorizations")
        };

        return definitions
            .Where(rule => rule.Metadata.ActivationFork <= rules.Fork)
            .Select(rule => rule.Calculate(context, rules.Fork))
            .ToArray();
    }

    private static TransactionChargeRule Rule(
        string id,
        Fork activation,
        ulong charge,
        string label) => new(id, activation, charge, label);

    private sealed class TransactionChargeRule : IGasRule<TransactionGasContext>
    {
        private readonly ulong _charge;
        private readonly string _label;

        public TransactionChargeRule(string id, Fork activation, ulong charge, string label)
        {
            _charge = charge;
            _label = label;
            Metadata = new GasRuleMetadata(
                new GasRuleId(id),
                "Transaction",
                activation,
                "Ethereum execution-specs transaction intrinsic gas",
                "IntrinsicGas.Compute");
        }

        public GasRuleMetadata Metadata { get; }

        public GasCalculation Calculate(TransactionGasContext context, Fork fork) =>
            GasCalculation.Create(
                Metadata,
                fork,
                _charge,
                0,
                GasDisposition.Charge,
                new[]
                {
                    new GasComponent("charge", _label, GasComponentKind.Charge, _charge)
                },
                Array.Empty<GasDecision>());
    }
}
