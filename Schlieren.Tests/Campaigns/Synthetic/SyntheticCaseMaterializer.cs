using System;
using System.Collections.Generic;
using System.Numerics;

namespace Schlieren.Tests.Campaigns.Synthetic;

/// <summary>
/// Turns a SyntheticCase into a runnable CampaignExecutionRequest.
/// Deterministic: same case → same bytes, always. No randomness here.
/// </summary>
public static class SyntheticCaseMaterializer
{
    // Fixed addresses — must match DeterministicAddresses constants
    private const string AddrCaller     = "0x0000000000000000000000000000000000000001";
    private const string AddrParent     = "0x00000000000000000000000000000000000000aa";
    private const string AddrChild      = "0x00000000000000000000000000000000000000bb";
    private const string AddrGrandchild = "0x00000000000000000000000000000000000000cc";
    private const string AddrPrecompile = "0x0000000000000000000000000000000000000001"; // ecrecover
    private const string AddrEmpty      = "0x00000000000000000000000000000000000000dd";
    private const string AddrNonexist   = "0x00000000000000000000000000000000000000ee";

    public static CampaignExecutionRequest Materialize(SyntheticCase c)
    {
        var childCode  = BuildChildCode(c);
        var parentCode = BuildParentCode(c);
        var prestate   = BuildPrestate(c, parentCode, childCode);

        return new CampaignExecutionRequest
        {
            Fork     = c.Fork,
            Caller   = AddrCaller,
            Target   = AddrParent,
            Calldata = "0x",
            Value    = 0,
            GasLimit = 10_000_000,
            Prestate = prestate,
        };
    }

    // ── Child bytecode ────────────────────────────────────────────────────────
    // ChildBehavior = the action body. RevertMode = the terminal opcode.
    // These are orthogonal: SStore+ExplicitRevert means SSTORE ... REVERT in bytecode.

    private static string BuildChildCode(SyntheticCase c)
    {
        var body = BuildChildBody(c);
        var term = BuildTerminator(c);
        return "0x" + string.Join("", body.Concat(term));
    }

    /// <summary>Action body — everything before the terminal opcode.</summary>
    private static List<string> BuildChildBody(SyntheticCase c)
    {
        var ops = new List<string>();
        switch (c.ChildBehavior)
        {
            case ChildBehavior.Stop:
            case ChildBehavior.Revert:
            case ChildBehavior.OutOfGas:
            case ChildBehavior.InvalidOpcode:
                // No body — terminal opcode is the whole thing
                break;

            case ChildBehavior.Return:
                var retLen = ReturnSizeToBytes(c.ReturnSize);
                if (retLen > 0)
                {
                    Push(ops, 0xAA); Push(ops, 0); ops.Add("52");           // MSTORE
                    PushUlong(ops, (ulong)retLen); Push(ops, 0); ops.Add("f3"); // RETURN(0, len)
                    return ops; // RETURN is its own terminal — RevertMode ignored
                }
                // retLen==0: fall through to terminator (RETURN(0,0) or REVERT(0,0))
                Push(ops, 0); Push(ops, 0);
                ops.Add(c.RevertMode == RevertMode.None ? "f3" : "fd");
                return ops;

            case ChildBehavior.SStore:
                var (writeVal, writeSlot) = StorageWrite(c.StoragePattern);
                PushUlong(ops, writeVal); PushUlong(ops, writeSlot); ops.Add("55"); // SSTORE
                break;

            case ChildBehavior.SStoreRevert:
                // Legacy combined behavior — always SSTORE then REVERT
                Push(ops, 0xAA); Push(ops, 0); ops.Add("55");
                Push(ops, 0); Push(ops, 0); ops.Add("fd");
                return ops;

            case ChildBehavior.Log:
                Push(ops, 0); Push(ops, 0); ops.Add("a0"); // LOG0(0, 0)
                break;

            case ChildBehavior.Log1:
                Push(ops, 0x1111); Push(ops, 0); Push(ops, 0); ops.Add("a1"); // LOG1
                break;

            case ChildBehavior.Log2:
                Push(ops, 0x2222); Push(ops, 0x1111); Push(ops, 0); Push(ops, 0); ops.Add("a2");
                break;

            case ChildBehavior.Log3:
                Push(ops, 0x3333); Push(ops, 0x2222); Push(ops, 0x1111); Push(ops, 0); Push(ops, 0); ops.Add("a3");
                break;

            case ChildBehavior.Log4:
                Push(ops, 0x4444); Push(ops, 0x3333); Push(ops, 0x2222); Push(ops, 0x1111); Push(ops, 0); Push(ops, 0); ops.Add("a4");
                break;

            case ChildBehavior.LogRevert:
                Push(ops, 0); Push(ops, 0); ops.Add("a0");
                Push(ops, 0); Push(ops, 0); ops.Add("fd");
                return ops;

            case ChildBehavior.SelfDestruct:
                ops.Add("73"); ops.AddRange(Addr20(AddrCaller));
                ops.Add("ff"); // SELFDESTRUCT
                return ops;   // no terminator after SELFDESTRUCT

            case ChildBehavior.NestedCall:
                Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0);
                ops.Add("73"); ops.AddRange(Addr20(AddrGrandchild));
                ops.Add("5a"); ops.Add("f1"); ops.Add("50"); // GAS CALL POP
                break;

            case ChildBehavior.Create:
                // CREATE empty contract: PUSH1 0 (size) PUSH1 0 (offset) PUSH1 0 (value) CREATE POP
                Push(ops, 0); Push(ops, 0); Push(ops, 0); ops.Add("f0"); ops.Add("50");
                break;

            case ChildBehavior.Create2:
                // CREATE2: PUSH1 0 (salt) PUSH1 0 (size) PUSH1 0 (offset) PUSH1 0 (value) CREATE2 POP
                Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0); ops.Add("f5"); ops.Add("50");
                break;

            case ChildBehavior.CreateRevert:
                // CREATE then REVERT (creation rolled back)
                Push(ops, 0); Push(ops, 0); Push(ops, 0); ops.Add("f0"); ops.Add("50");
                Push(ops, 0); Push(ops, 0); ops.Add("fd");
                return ops;

            case ChildBehavior.CallThenSStore:
                // CALL grandchild, then SSTORE slot 0
                Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0);
                ops.Add("73"); ops.AddRange(Addr20(AddrGrandchild));
                ops.Add("5a"); ops.Add("f1"); ops.Add("50");
                Push(ops, 0xAA); Push(ops, 0); ops.Add("55"); // SSTORE
                break;

            case ChildBehavior.SStoreThenCall:
                // SSTORE slot 0, then CALL grandchild
                Push(ops, 0xAA); Push(ops, 0); ops.Add("55");
                Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0); Push(ops, 0);
                ops.Add("73"); ops.AddRange(Addr20(AddrGrandchild));
                ops.Add("5a"); ops.Add("f1"); ops.Add("50");
                break;

            case ChildBehavior.MultiReturn:
                // Return 32 bytes
                Push(ops, 0xDEAD); Push(ops, 0); ops.Add("52");
                Push(ops, 32); Push(ops, 0); ops.Add("f3");
                return ops;

            default:
                break;
        }
        return ops;
    }

    /// <summary>Terminal opcode sequence — appended after the body.</summary>
    private static List<string> BuildTerminator(SyntheticCase c)
    {
        // Behaviors that self-terminate ignore RevertMode
        if (c.ChildBehavior is ChildBehavior.SStoreRevert or ChildBehavior.LogRevert
            or ChildBehavior.SelfDestruct or ChildBehavior.Return
            or ChildBehavior.CreateRevert or ChildBehavior.MultiReturn)
            return new List<string>();

        return c.RevertMode switch
        {
            RevertMode.None          => new List<string> { "00" },              // STOP
            RevertMode.ExplicitRevert=> new List<string> { "60","00","60","00","fd" }, // PUSH1 0 PUSH1 0 REVERT
            RevertMode.InvalidOpcode => new List<string> { "fe" },              // INVALID
            RevertMode.OutOfGas      => new List<string>                        // tight JUMP loop
                { "60","03","56","5b","60","03","56" }, // PUSH1 3 JUMP JUMPDEST PUSH1 3 JUMP
            _ => new List<string> { "00" }
        };
    }

    /// <summary>
    /// Returns (writeValue, slot) for the SSTORE based on StoragePattern.
    /// XToY writes 0xBB (different from pre=0xAA).
    /// XToZero writes 0x00.
    /// ZeroToX / None write 0xAA.
    /// Public so InvariantChecker can derive expected transitions.
    /// </summary>
    public static (ulong value, ulong slot) StorageWritePublic(StoragePattern p) => StorageWrite(p);

    /// <summary>Returns the pre-state value for the given slot, matching BuildPreStorage.</summary>
    public static ulong PreStorageValue(StoragePattern p, ulong slot)
    {
        if (slot != 0) return 0;
        return p switch
        {
            StoragePattern.XToY         => 0xAA,
            StoragePattern.XToZero      => 0xAA,
            StoragePattern.SameSlotTwice => 0xAA,
            StoragePattern.MultiSlot    => 0xAA, // slot 0
            _                           => 0x00, // ZeroToX, None
        };
    }

    private static (ulong value, ulong slot) StorageWrite(StoragePattern p) => p switch
    {
        StoragePattern.XToY        => (0xBB, 0),  // pre=0xAA  write=0xBB  → change
        StoragePattern.XToZero     => (0x00, 0),  // pre=0xAA  write=0x00  → delete
        StoragePattern.ZeroToX     => (0xAA, 0),  // pre=0x00  write=0xAA  → create
        StoragePattern.SameSlotTwice => (0xBB, 0),// same slot, final value 0xBB (second write wins)
        StoragePattern.MultiSlot   => (0xAA, 0),  // slot 0 = 0xAA (multi handled separately)
        _                          => (0xAA, 0),  // ZeroToX / None
    };

    // ── Parent bytecode ───────────────────────────────────────────────────────

    private static string BuildParentCode(SyntheticCase c)
    {
        var ops    = new List<string>();
        var target = ResolveTargetAddress(c);

        // Stack: retSize retOffset argsSize argsOffset value target gas <CALL>
        Push(ops, 0);  // retSize
        Push(ops, 0);  // retOffset
        Push(ops, 0);  // argsSize
        Push(ops, 0);  // argsOffset
        PushBigInt(ops, ResolveValue(c));  // value
        ops.Add("73"); ops.AddRange(Addr20(target));  // PUSH20 target

        // Gas forwarded to child
        if (c.GasClass == GasClass.High)
            ops.Add("5a");  // GAS — forward whatever remains (subject to 63/64)
        else
            PushUlong(ops, ResolveChildGas(c));

        ops.Add(c.CallKind switch
        {
            CallKind.Call         => "f1",
            CallKind.StaticCall   => "fa",
            CallKind.DelegateCall => "f4",
            CallKind.CallCode     => "f2",
            _ => throw new ArgumentOutOfRangeException(nameof(c.CallKind))
        });

        ops.Add("50");  // POP result
        ops.Add("00");  // STOP

        return "0x" + string.Join("", ops);
    }

    // ── Prestate ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<CampaignAccount> BuildPrestate(
        SyntheticCase c, string parentCode, string childCode)
    {
        var accounts = new List<CampaignAccount>
        {
            new() { Address = AddrParent, Code = parentCode,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0 },
            new() { Address = AddrCaller,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0 },
        };

        switch (c.TargetKind)
        {
            case TargetKind.ExistingCode:
                accounts.Add(new()
                {
                    Address = AddrChild, Code = childCode,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0,
                    Storage = BuildPreStorage(c),
                });
                break;

            case TargetKind.EmptyAccount:
                // Account exists with no code — value transfer target
                accounts.Add(new()
                {
                    Address = AddrChild,
                    Balance = "0xDE0B6B3A7640000", Nonce = 0,
                });
                break;

            case TargetKind.Nonexistent:
                // No account entry at AddrChild / AddrNonexist — left out of prestate
                break;

            case TargetKind.Precompile:
                // ecrecover (0x01) — no prestate entry needed
                break;

            case TargetKind.Self:
                // Parent calls itself — already in prestate as AddrParent
                break;
        }

        if (c.ChildBehavior is ChildBehavior.NestedCall
                             or ChildBehavior.CallThenSStore
                             or ChildBehavior.SStoreThenCall)
        {
            accounts.Add(new()
            {
                Address = AddrGrandchild,
                Code    = "0x00",  // leaf STOP
                Balance = "0xDE0B6B3A7640000", Nonce = 0,
            });
        }

        return accounts;
    }

    private static Dictionary<string, string> BuildPreStorage(SyntheticCase c)
    {
        var s = new Dictionary<string, string>();
        switch (c.StoragePattern)
        {
            case StoragePattern.XToY:
            case StoragePattern.XToZero:
            case StoragePattern.SameSlotTwice:
                s["0x0"] = "0xAA";  // pre=0xAA; write will be 0xBB/0x00/0xBB
                break;
            case StoragePattern.MultiSlot:
                s["0x0"] = "0xAA";
                s["0x1"] = "0xBB";
                s["0x2"] = "0xCC";
                break;
            // ZeroToX, None: pre-storage empty (slot starts at 0)
        }
        return s;
    }

    // ── Resolution helpers ────────────────────────────────────────────────────

    private static string ResolveTargetAddress(SyntheticCase c) => c.TargetKind switch
    {
        TargetKind.ExistingCode => AddrChild,
        TargetKind.EmptyAccount => AddrChild,
        TargetKind.Nonexistent  => AddrNonexist,
        TargetKind.Precompile   => AddrPrecompile,
        TargetKind.Self         => AddrParent,
        _                       => AddrChild,
    };

    private static BigInteger ResolveValue(SyntheticCase c) => c.ValueClass switch
    {
        ValueClass.Zero                => BigInteger.Zero,
        ValueClass.One                 => BigInteger.One,
        ValueClass.Byte255             => new BigInteger(255),
        ValueClass.Byte256             => new BigInteger(256),
        ValueClass.OneEther            => BigInteger.Parse("1000000000000000000"),
        ValueClass.InsufficientBalance => BigInteger.Parse("99999999999999999999999"),
        _                              => BigInteger.Zero,
    };

    private static ulong ResolveChildGas(SyntheticCase c) => c.GasClass switch
    {
        GasClass.Minimal         => 100,
        GasClass.BelowStipend    => 2299,
        GasClass.Stipend         => 2300,
        GasClass.AboveStipend    => 2301,
        GasClass.ExactMinus1     => 20_999,
        GasClass.Exact           => 21_000,
        GasClass.ExactPlus1      => 21_001,
        GasClass.Boundary6364    => 9_979_000 - (9_979_000 / 64),
        GasClass.High            => 10_000_000,
        _                        => 10_000_000,
    };

    private static int ReturnSizeToBytes(ReturnSize r) => r switch
    {
        ReturnSize.Zero    => 0,   ReturnSize.One    => 1,
        ReturnSize.Byte31  => 31,  ReturnSize.Byte32 => 32,
        ReturnSize.Byte33  => 33,  ReturnSize.Byte255 => 255,
        ReturnSize.Byte256 => 256, ReturnSize.Byte257 => 257,
        _                  => 0,
    };

    // ── Bytecode emission helpers ─────────────────────────────────────────────

    private static void Push(List<string> ops, ulong v) => PushUlong(ops, v);

    private static void PushUlong(List<string> ops, ulong v)
    {
        if (v == 0) { ops.Add("60"); ops.Add("00"); return; }
        var bytes = new List<byte>();
        while (v > 0) { bytes.Insert(0, (byte)(v & 0xff)); v >>= 8; }
        ops.Add($"{0x5f + bytes.Count:x2}");
        foreach (var b in bytes) ops.Add($"{b:x2}");
    }

    private static void PushBigInt(List<string> ops, BigInteger v)
    {
        if (v == BigInteger.Zero) { ops.Add("60"); ops.Add("00"); return; }
        var bytes = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length > 32) bytes = bytes[^32..];
        ops.Add($"{0x5f + bytes.Length:x2}");
        foreach (var b in bytes) ops.Add($"{b:x2}");
    }

    /// <summary>Split a 0x-prefixed 40-char address into 20 two-char hex elements.</summary>
    private static IEnumerable<string> Addr20(string addr)
    {
        var hex = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;
        if (hex.Length != 40)
            throw new ArgumentException($"Address must be 40 hex chars, got {hex.Length}: '{addr}'");
        for (int i = 0; i < 40; i += 2)
            yield return hex.Substring(i, 2);
    }
}
