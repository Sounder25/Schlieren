using System.Numerics;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Guard;

public sealed record GuardAccountSnapshot(
    Address Address,
    ulong Nonce,
    BigInteger Balance,
    byte[] Code,
    IReadOnlyDictionary<string, string> Storage);

public static class GuardPreState
{
    public static IReadOnlyList<GuardAccountSnapshot> Capture(IGlobalState state)
    {
        return state.Snapshot()
            .OrderBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new GuardAccountSnapshot(
                kv.Key,
                kv.Value.Nonce,
                kv.Value.Balance,
                kv.Value.Code,
                kv.Value.Storage.ToDictionary(
                    slot => Abi.Qty(slot.Key),
                    slot => Abi.Qty(slot.Value),
                    StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public static object ToJson(IEnumerable<GuardAccountSnapshot> accounts) =>
        accounts.Select(a => new
        {
            address = a.Address.ToString(),
            nonce = Abi.Qty(a.Nonce),
            balance = Abi.Qty(a.Balance),
            code = a.Code.Length == 0 ? "0x" : Abi.ToHex(a.Code),
            storage = a.Storage
        }).ToArray();
}
