using System.Numerics;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.State;

/// <summary>
/// Produces a structured diff between two global state snapshots.
/// Uses <see cref="IGlobalState.Snapshot"/> to compare Account dictionaries
/// without calling async APIs.
/// 
/// Usable for ad-hoc inspection (RPC eth_call, CLI execution) without
/// requiring a conformance fixture.
/// </summary>
public static class StateDiffBuilder
{
    /// <summary>
    /// A single account's changes between pre-state and post-state.
    /// </summary>
    public sealed record AccountDelta
    {
        public required Address Address { get; init; }
        public required AccountDeltaKind Kind { get; init; }
        public BigInteger? BalanceBefore { get; init; }
        public BigInteger? BalanceAfter { get; init; }
        public BigInteger BalanceDelta => (BalanceAfter ?? 0) - (BalanceBefore ?? 0);
        public ulong? NonceBefore { get; init; }
        public ulong? NonceAfter { get; init; }
        public bool CodeChanged { get; init; }
        public IReadOnlyList<StorageSlotDelta> StorageChanges { get; init; } = Array.Empty<StorageSlotDelta>();
        public bool SelfDestructed { get; init; }
    }

    public enum AccountDeltaKind
    {
        Modified,
        Created,
        Deleted,
        Unchanged
    }

    /// <summary>
    /// A single storage slot change.
    /// </summary>
    public sealed record StorageSlotDelta(
        string Slot,
        BigInteger Before,
        BigInteger After)
    {
        public BigInteger Delta => After - Before;
    }

    /// <summary>
    /// Complete state diff between two snapshots.
    /// </summary>
    public sealed record StateDiff
    {
        public required IReadOnlyList<AccountDelta> Accounts { get; init; }
        public int CreatedCount => Accounts.Count(a => a.Kind == AccountDeltaKind.Created);
        public int ModifiedCount => Accounts.Count(a => a.Kind == AccountDeltaKind.Modified);
        public int DeletedCount => Accounts.Count(a => a.Kind == AccountDeltaKind.Deleted);
        public bool HasChanges => Accounts.Any(a => a.Kind != AccountDeltaKind.Unchanged);
    }

    /// <summary>
    /// Compare two Account-dictionary snapshots produced by <see cref="IGlobalState.Snapshot"/>.
    /// This is the canonical comparison path — synchronous and deterministic.
    /// </summary>
    public static StateDiff Compare(
        IDictionary<Address, Account> preSnapshot,
        IDictionary<Address, Account> postSnapshot)
    {
        var allAddresses = new HashSet<Address>(preSnapshot.Keys);
        foreach (var addr in postSnapshot.Keys)
            allAddresses.Add(addr);

        var deltas = new List<AccountDelta>();

        foreach (var addr in allAddresses)
        {
            var preExists = preSnapshot.TryGetValue(addr, out var preAcct);
            var postExists = postSnapshot.TryGetValue(addr, out var postAcct);

            if (!preExists && !postExists) continue;

            var kind = (!preExists && postExists) ? AccountDeltaKind.Created
                     : (preExists && !postExists) ? AccountDeltaKind.Deleted
                     : AccountDeltaKind.Modified;

            BigInteger? balBefore = preExists ? preAcct!.Balance : null;
            BigInteger? balAfter = postExists ? postAcct!.Balance : null;
            ulong? nonceBefore = preExists ? preAcct!.Nonce : null;
            ulong? nonceAfter = postExists ? postAcct!.Nonce : null;

            var preCode = preExists ? preAcct!.Code ?? Array.Empty<byte>() : Array.Empty<byte>();
            var postCode = postExists ? postAcct!.Code ?? Array.Empty<byte>() : Array.Empty<byte>();
            // Created/deleted empty-code EOAs are not a code change.
            bool codeChanged = !preCode.AsSpan().SequenceEqual(postCode);

            // Compare storage
            var storageChanges = new List<StorageSlotDelta>();
            var allSlots = new HashSet<BigInteger>();
            if (preExists && preAcct!.Storage != null)
                foreach (var k in preAcct.Storage.Keys) allSlots.Add(k);
            if (postExists && postAcct!.Storage != null)
                foreach (var k in postAcct.Storage.Keys) allSlots.Add(k);

            foreach (var slot in allSlots)
            {
                var beforeVal = BigInteger.Zero;
                var afterVal = BigInteger.Zero;

                if (preExists && preAcct!.Storage != null)
                    preAcct.Storage.TryGetValue(slot, out beforeVal);
                if (postExists && postAcct!.Storage != null)
                    postAcct.Storage.TryGetValue(slot, out afterVal);

                if (beforeVal != afterVal)
                {
                    storageChanges.Add(new StorageSlotDelta(
                        Slot: "0x" + slot.ToString("x64"),
                        Before: beforeVal,
                        After: afterVal));
                }
            }

            // Check if anything actually changed
            bool anyChange = balBefore != balAfter
                          || nonceBefore != nonceAfter
                          || codeChanged
                          || storageChanges.Count > 0
                          || kind != AccountDeltaKind.Modified;

            if (!anyChange && kind == AccountDeltaKind.Modified)
                kind = AccountDeltaKind.Unchanged;

            if (kind == AccountDeltaKind.Unchanged) continue;

            deltas.Add(new AccountDelta
            {
                Address = addr,
                Kind = kind,
                BalanceBefore = balBefore,
                BalanceAfter = balAfter,
                NonceBefore = nonceBefore,
                NonceAfter = nonceAfter,
                CodeChanged = codeChanged,
                StorageChanges = storageChanges
            });
        }

        return new StateDiff { Accounts = deltas };
    }

    /// <summary>
    /// Render a state diff as human-readable text.
    /// </summary>
    public static string RenderText(StateDiff diff)
    {
        if (!diff.HasChanges)
            return "No state changes.";

        var lines = new List<string>
        {
            $"State diff: {diff.CreatedCount} created, {diff.ModifiedCount} modified, {diff.DeletedCount} deleted",
            ""
        };

        foreach (var acct in diff.Accounts)
        {
            lines.Add($"  {acct.Kind.ToString().ToUpper()}: {acct.Address}");

            if (acct.BalanceBefore.HasValue || acct.BalanceAfter.HasValue)
            {
                lines.Add($"    Balance: {acct.BalanceBefore ?? 0} → {acct.BalanceAfter ?? 0} (delta: {acct.BalanceDelta:+#;-#;0})");
            }

            if (acct.NonceBefore.HasValue || acct.NonceAfter.HasValue)
            {
                lines.Add($"    Nonce: {acct.NonceBefore ?? 0} → {acct.NonceAfter ?? 0}");
            }

            if (acct.CodeChanged)
                lines.Add("    Code: CHANGED");

            foreach (var slot in acct.StorageChanges)
            {
                lines.Add($"    Storage[{slot.Slot}]: {slot.Before} → {slot.After}");
            }

            if (acct.SelfDestructed)
                lines.Add("    SELF-DESTRUCTED");

            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
