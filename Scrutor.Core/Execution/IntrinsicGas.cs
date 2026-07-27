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

    /// <summary>
    /// Returns the intrinsic gas for <paramref name="tx"/> (may exceed GasLimit — caller must check).
    /// </summary>
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

            return gas;
        }
    }
}
