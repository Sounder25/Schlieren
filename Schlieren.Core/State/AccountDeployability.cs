using Schlieren.Core.Primitives;

namespace Schlieren.Core.State;

/// <summary>
/// EELS <c>account_deployable()</c> / EIP-7610:
/// an address may receive new code only when nonce is 0, code is empty,
/// and storage is empty. Non-empty storage alone is a collision even if
/// the account looks "empty" for EIP-161 purposes.
/// </summary>
public static class AccountDeployability
{
    public static async ValueTask<bool> IsDeployableAsync(
        IGlobalState state,
        Address address,
        CancellationToken ct = default)
    {
        var nonce = await state.GetNonceAsync(address, ct);
        if (nonce != 0)
            return false;

        var code = await state.GetCodeAsync(address, ct);
        if (code.Length != 0)
            return false;

        var storage = await state.GetStoragePresenceAsync(address, ct);
        // Empty or Unknown-without-writes: only NonEmpty blocks deployment.
        return storage != StoragePresence.NonEmpty;
    }
}
