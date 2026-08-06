using Scrutor.Core.State;

namespace Scrutor.Core.Execution;

/// <summary>
/// Computes the intrinsic (base) gas cost for a transaction per the Yellow Paper §6.2
/// and EIP-2930 (access list surcharge). This cost must be deducted from the gas limit
/// before entering EVM execution; the remainder is the available execution gas.
/// </summary>
public static class IntrinsicGas
{
    // Base costs per Yellow Paper Table 1 / Berlin hard-fork
    private const ulong TxBase = 21_000;
    private const ulong TxCreate = 32_000;    // additional cost for contract creation
    private const ulong ZeroByte = 4;
    private const ulong NonZeroByte = 16;

    // EIP-2930 access list surcharges (Berlin+)
    private const ulong AccessListAddressCost = 2_400;
    private const ulong AccessListStorageKeyCost = 1_900;

    // EIP-7623 (Prague): token-based calldata floor
    // Zero byte  = 1 token, Non-zero byte = 4 tokens
    // Floor = TX_BASE + tokens × TOTAL_COST_FLOOR_PER_TOKEN
    private const ulong TokensPerZeroByte    = 1;
    private const ulong TokensPerNonZeroByte = 4;
    private const ulong FloorCostPerToken    = 10;    // TOTAL_COST_FLOOR_PER_TOKEN

    /// <summary>
    /// EIP-7623: number of calldata tokens for the transaction.
    /// tokens = sum(1 per zero byte, 4 per non-zero byte).
    /// </summary>
    public static ulong ComputeTokens(Transaction tx)
    {
        ulong tokens = 0;
        foreach (var b in tx.Data)
            tokens += b == 0 ? TokensPerZeroByte : TokensPerNonZeroByte;
        return tokens;
    }

    /// <summary>
    /// EIP-7623: minimum gas that must be consumed by this transaction (floor).
    /// floor = TX_BASE + tokens × 10
    /// If actual gasUsed after execution is less than this, the floor is charged instead.
    /// </summary>
    public static ulong ComputeFloor(Transaction tx)
    {
        return TxBase + ComputeTokens(tx) * FloorCostPerToken;
    }

    /// <summary>
    /// Returns the intrinsic gas for <paramref name="tx"/>.
    /// Returns false (via out param) when the gas limit is already below intrinsic cost.
    /// </summary>
    /// <param name="tx">The transaction to evaluate.</param>
    /// <param name="intrinsic">The computed intrinsic gas value.</param>
    /// <returns>True if gas limit covers intrinsic cost, false otherwise.</returns>
    public static bool TryCompute(Transaction tx, out ulong intrinsic)
    {
        intrinsic = Compute(tx);
        return tx.GasLimit >= intrinsic;
    }

    // EIP-7702 (Prague): per-authorization base cost in type-4 transactions
    private const ulong PerAuthorizationCost = 25_000;

    /// <summary>
    /// Returns the intrinsic gas for <paramref name="tx"/> (may exceed GasLimit — caller must check).</summary>
    public static ulong Compute(Transaction tx)
    {
        checked
        {
            ulong gas = TxBase;

            // Contract creation surcharge
            if (tx.To == null)
            {
                gas += TxCreate;
                // EIP-3860: 2 gas per 32-byte word of initcode
                gas += 2UL * ((ulong)(tx.Data.Length + 31) / 32);
            }

            // Calldata cost: 4 per zero byte, 16 per non-zero byte
            foreach (var b in tx.Data)
                gas += b == 0 ? ZeroByte : NonZeroByte;

            // EIP-2930 access list: 2400 per address, 1900 per storage key
            foreach (var entry in tx.AccessList)
            {
                gas += AccessListAddressCost;
                gas += AccessListStorageKeyCost * (ulong)entry.StorageKeys.Count;
            }

            // EIP-7702 (Prague): 25,000 gas per authorization in type-4 transactions
            if (tx.TxType == 4)
            {
                gas += PerAuthorizationCost * (ulong)tx.AuthorizationList.Count;
            }

            return gas;
        }
    }
}
