using Schlieren.Core.Forks;
using Schlieren.Core.State;

namespace Schlieren.Core.Execution;

/// <summary>
/// Computes the intrinsic (base) gas cost for a transaction, fully fork-aware.
/// Calldata costs, access-list costs, initcode word costs, and per-authorization
/// costs all vary by fork — pass the active <see cref="IForkRules"/> for the block.
/// </summary>
public static class IntrinsicGas
{
    private const ulong TxBase   = 21_000;
    private const ulong TxCreate = 32_000;

    // EIP-2930 access list surcharges (Berlin+)
    private const ulong AccessListAddressCost    = 2_400;
    private const ulong AccessListStorageKeyCost = 1_900;

    // EIP-7702 (Prague+): per-authorization cost in type-4 txs
    private const ulong PerAuthorizationCost = 25_000;

    // EIP-7623 (Prague+): calldata token floor
    private const ulong TokensPerZeroByte    = 1;
    private const ulong TokensPerNonZeroByte = 4;
    private const ulong FloorCostPerToken    = 10;

    /// <summary>EIP-7623: compute total calldata tokens.</summary>
    public static ulong ComputeTokens(Transaction tx)
    {
        ulong tokens = 0;
        foreach (var b in tx.Data)
            tokens += b == 0 ? TokensPerZeroByte : TokensPerNonZeroByte;
        return tokens;
    }

    /// <summary>EIP-7623: floor = TX_BASE + tokens × 10.</summary>
    public static ulong ComputeFloor(Transaction tx)
        => TxBase + ComputeTokens(tx) * FloorCostPerToken;

    /// <summary>Returns true when GasLimit ≥ intrinsic cost.</summary>
    public static bool TryCompute(Transaction tx, IForkRules rules, out ulong intrinsic)
    {
        intrinsic = Compute(tx, rules);
        return tx.GasLimit >= intrinsic;
    }

    /// <summary>Legacy overload — uses Prague rules (backward compat).</summary>
    public static bool TryCompute(Transaction tx, out ulong intrinsic)
        => TryCompute(tx, ForkRulesFactory.Latest, out intrinsic);

    /// <summary>
    /// Returns the intrinsic gas for <paramref name="tx"/> under the given fork rules.
    /// </summary>
    public static ulong Compute(Transaction tx, IForkRules rules)
    {
        checked
        {
            ulong gas = TxBase;

            // Contract creation surcharge
            if (tx.To == null)
            {
                gas += TxCreate;
                // EIP-3860 (Shanghai+): 2 gas per 32-byte word of initcode
                if (rules.HasEip3860InitcodeLimit)
                    gas += 2UL * ((ulong)(tx.Data.Length + 31) / 32);
            }

            // Calldata cost — fork-dependent per-byte rates
            foreach (var b in tx.Data)
                gas += b == 0 ? rules.CalldataZeroByteCost : rules.CalldataNonZeroByteCost;

            // EIP-2930 (Berlin+): access list surcharges
            if (rules.HasEip2930AccessLists)
            {
                foreach (var entry in tx.AccessList)
                {
                    gas += AccessListAddressCost;
                    gas += AccessListStorageKeyCost * (ulong)entry.StorageKeys.Count;
                }
            }

            // EIP-7702 (Prague+): 25,000 gas per authorization in type-4 txs
            if (rules.HasEip7702SetCode && tx.TxType == 4)
                gas += PerAuthorizationCost * (ulong)tx.AuthorizationList.Count;

            return gas;
        }
    }

    /// <summary>Legacy overload — uses Prague rules (backward compat).</summary>
    public static ulong Compute(Transaction tx)
        => Compute(tx, ForkRulesFactory.Latest);
}
