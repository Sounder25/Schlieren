using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Schlieren.Tests.Campaigns.PathologicalExecution;

/// <summary>
/// Converts a PathologicalCase into a CampaignExecutionRequest.
///
/// Design principle: produce the simplest possible bytecode that exercises
/// exactly the case under test.  All boundary operands are pushed onto the
/// EVM stack from Solidity-style PUSH instructions — never embedded in
/// calldata — so the interpreter must handle them before any memory access.
///
/// Every case provides ≥10_000_000 gas so OOG is always the engine's call,
/// not a test-scaffolding artefact.
/// </summary>
public static class PathologicalMaterializer
{
    // Fixed deterministic addresses
    private const string AddrSender      = "0x00000000000000000000000000000000000000a1";
    private const string AddrTarget      = "0x00000000000000000000000000000000000000a2";
    private const string AddrChild       = "0x00000000000000000000000000000000000000b1";
    private const string AddrGrandchild  = "0x00000000000000000000000000000000000000b2";
    private const string AddrEmpty       = "0x00000000000000000000000000000000000000c1";
    // Standard very-large balance so value-transfer tests don't trivially fail on balance
    private const string LargeBalance    = "0xDE0B6B3A76400000000"; // 1000 ETH-ish
    private const ulong  GasLimit        = 10_000_000;

    public static CampaignExecutionRequest Materialize(PathologicalCase c)
    {
        var code     = BuildCode(c);
        var prestate = BuildPrestate(c, code);

        return new CampaignExecutionRequest
        {
            Fork     = c.Fork,
            Caller   = AddrSender,
            Target   = AddrTarget,
            Calldata = BuildCalldata(c),
            Value    = 0,
            GasLimit = GasLimit,
            Prestate = prestate,
        };
    }

    // ── Code builder ─────────────────────────────────────────────────────────

    private static string BuildCode(PathologicalCase c)
    {
        var ops = new List<string>();

        switch (c.Family)
        {
            case PathFamily.BigIntegerNarrowing:
                // Some BigIntegerNarrowing cases exercise copy opcodes (CALLDATACOPY,
                // CODECOPY, RETURNDATACOPY) with huge offsets — route those to the
                // copy builder; all others go to the memory boundary builder.
                if (c.CopySource.HasValue)
                    BuildCopyCode(c, ops);
                else
                    BuildMemoryBoundaryCode(c, ops);
                break;

            case PathFamily.MemoryBoundary:
                BuildMemoryBoundaryCode(c, ops);
                break;

            case PathFamily.CopyReturndata:
                BuildCopyCode(c, ops);
                break;

            case PathFamily.PrecompilePathological:
                BuildPrecompileCode(c, ops);
                break;

            case PathFamily.ExceptionalHalt:
                BuildExceptionalHaltCode(c, ops);
                break;

            case PathFamily.CreateLifecycle:
                BuildCreateCode(c, ops);
                break;

            case PathFamily.StackDepth:
                BuildStackDepthCode(c, ops);
                break;

            case PathFamily.ArithmeticBoundary:
                BuildArithCode(c, ops);
                break;

            default:
                ops.Add("00"); // STOP — safe fallback
                break;
        }

        return "0x" + string.Join("", ops);
    }

    // ── Memory / BigInteger narrowing ─────────────────────────────────────────

    private static void BuildMemoryBoundaryCode(PathologicalCase c, List<string> ops)
    {
        var offset = ResolveBoundaryBigInt(c.Boundary ?? BoundaryValue.Zero, c.Param1);

        switch (c.MemoryKind)
        {
            case MemoryVariant.KeccakHugeRange:
                // KECCAK256(huge_offset, 0)  — expansion should OOG
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, offset);
                ops.Add("20"); // KECCAK256
                ops.Add("50"); // POP
                ops.Add("00"); // STOP
                return;

            case MemoryVariant.LogHugeRange:
                // LOG0(huge_offset, 0)
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, offset);
                ops.Add("a0"); // LOG0
                ops.Add("00");
                return;

            case MemoryVariant.ReturnHugeOffset:
                // RETURN(huge_offset, 0)
                PushBI(ops, BigInteger.Zero); // size
                PushBI(ops, offset);          // offset
                ops.Add("f3"); // RETURN
                return;

            case MemoryVariant.RevertHugeOffset:
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, offset);
                ops.Add("fd"); // REVERT
                return;

            case MemoryVariant.CallArgsHugeOffset:
                // CALL(gas, addr, 0, huge_argsOffset, 0, 0, 0)
                PushBI(ops, BigInteger.Zero);          // retLen
                PushBI(ops, BigInteger.Zero);          // retOff
                PushBI(ops, BigInteger.Zero);          // argsLen
                PushBI(ops, offset);                   // argsOff (huge)
                PushBI(ops, BigInteger.Zero);          // value
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a"); // GAS
                ops.Add("f1"); // CALL
                ops.Add("50");
                ops.Add("00");
                return;

            case MemoryVariant.CallRetHugeOffset:
                // CALL with huge retOffset
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, offset);                   // retOff (huge)
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a");
                ops.Add("f1");
                ops.Add("50");
                ops.Add("00");
                return;

            default:
                // Default: MLOAD or MSTORE at boundary offset
                if (c.Opcode == PathOpcode.Mstore)
                {
                    PushBI(ops, BigInteger.Zero); // value
                    PushBI(ops, offset);          // offset
                    ops.Add("52"); // MSTORE
                }
                else
                {
                    PushBI(ops, offset);
                    ops.Add("51"); // MLOAD
                    ops.Add("50");
                }
                ops.Add("00");
                return;
        }
    }

    // ── Copy / returndata ─────────────────────────────────────────────────────

    private static void BuildCopyCode(PathologicalCase c, List<string> ops)
    {
        if (c.CopySource == CopySource.Returndata)
        {
            // First: CALL a child that returns N bytes (param1 = return size, default 32)
            var retBytes = c.Param1 ?? 32;
            var childRet = retBytes > 0 ? BuildFixedReturnCode(retBytes) : "0x00";

            // CALL child with enough gas
            PushBI(ops, BigInteger.Zero);                  // retLen
            PushBI(ops, BigInteger.Zero);                  // retOff
            PushBI(ops, BigInteger.Zero);                  // argsLen
            PushBI(ops, BigInteger.Zero);                  // argsOff
            PushBI(ops, BigInteger.Zero);                  // value
            ops.Add("73"); ops.AddRange(Addr20(AddrChild));
            ops.Add("5a");
            ops.Add("f1"); // CALL
            ops.Add("50"); // POP success flag

            // Now RETURNDATACOPY(destOff, srcOff, size)
            var (srcOff, size) = ResolveCopyOffsetSize(c, retBytes);
            PushBI(ops, size);                              // size
            PushBI(ops, srcOff);                           // srcOff into returndata buffer
            PushBI(ops, BigInteger.Zero);                  // destOff in memory
            ops.Add("3e"); // RETURNDATACOPY
            ops.Add("00");
            return;
        }

        // CALLDATACOPY / CODECOPY / EXTCODECOPY
        var (dstOff, sOff, sz) = ResolveCopyTriple(c);

        switch (c.CopySource)
        {
            case CopySource.Calldata:
                PushBI(ops, sz);
                PushBI(ops, sOff);
                PushBI(ops, dstOff);
                ops.Add("37"); // CALLDATACOPY
                break;

            case CopySource.Code:
                PushBI(ops, sz);
                PushBI(ops, sOff);
                PushBI(ops, dstOff);
                ops.Add("39"); // CODECOPY
                break;

            case CopySource.ExtcodeOf:
                PushBI(ops, sz);
                PushBI(ops, sOff);
                PushBI(ops, dstOff);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("3c"); // EXTCODECOPY
                break;
        }
        ops.Add("00");
    }

    private static (BigInteger srcOff, BigInteger size) ResolveCopyOffsetSize(PathologicalCase c, ulong retBytes)
    {
        var retBuf = new BigInteger(retBytes);
        return c.CopyKind switch
        {
            CopyVariant.OffsetZero          => (BigInteger.Zero, retBuf),
            CopyVariant.OffsetExactEnd      => (retBuf, BigInteger.Zero),
            CopyVariant.OffsetOnePastEnd    => (retBuf + 1, BigInteger.One),
            CopyVariant.SizeZero            => (BigInteger.Zero, BigInteger.Zero),
            CopyVariant.SizeOneByte         => (BigInteger.Zero, BigInteger.One),
            CopyVariant.Size31              => (BigInteger.Zero, new BigInteger(31)),
            CopyVariant.Size32              => (BigInteger.Zero, new BigInteger(32)),
            CopyVariant.Size33              => (BigInteger.Zero, new BigInteger(33)),
            CopyVariant.Size255             => (BigInteger.Zero, new BigInteger(255)),
            CopyVariant.Size256             => (BigInteger.Zero, new BigInteger(256)),
            CopyVariant.Size257             => (BigInteger.Zero, new BigInteger(257)),
            CopyVariant.Size10k             => (BigInteger.Zero, new BigInteger(10_240)),
            CopyVariant.HugeOffset          => (U256Max, BigInteger.Zero),
            CopyVariant.HugeSize            => (BigInteger.Zero, U256Max),
            CopyVariant.OverflowOffsetPlusSize => (U256Max - 31, new BigInteger(64)),
            _                               => (BigInteger.Zero, retBuf),
        };
    }

    private static (BigInteger dst, BigInteger src, BigInteger size) ResolveCopyTriple(PathologicalCase c) =>
        c.CopyKind switch
        {
            CopyVariant.OffsetZero          => (BigInteger.Zero, BigInteger.Zero, new BigInteger(32)),
            CopyVariant.OffsetExactEnd      => (BigInteger.Zero, new BigInteger(32), new BigInteger(32)),
            CopyVariant.OffsetOnePastEnd    => (BigInteger.Zero, new BigInteger(33), new BigInteger(32)),
            CopyVariant.SizeZero            => (BigInteger.Zero, BigInteger.Zero, BigInteger.Zero),
            CopyVariant.SizeOneByte         => (BigInteger.Zero, BigInteger.Zero, BigInteger.One),
            CopyVariant.Size31              => (BigInteger.Zero, BigInteger.Zero, new BigInteger(31)),
            CopyVariant.Size32              => (BigInteger.Zero, BigInteger.Zero, new BigInteger(32)),
            CopyVariant.Size33              => (BigInteger.Zero, BigInteger.Zero, new BigInteger(33)),
            CopyVariant.Size255             => (BigInteger.Zero, BigInteger.Zero, new BigInteger(255)),
            CopyVariant.Size256             => (BigInteger.Zero, BigInteger.Zero, new BigInteger(256)),
            CopyVariant.Size257             => (BigInteger.Zero, BigInteger.Zero, new BigInteger(257)),
            CopyVariant.Size10k             => (BigInteger.Zero, BigInteger.Zero, new BigInteger(10_240)),
            CopyVariant.HugeOffset          => (BigInteger.Zero, U256Max, new BigInteger(32)),
            CopyVariant.HugeSize            => (BigInteger.Zero, BigInteger.Zero, U256Max),
            CopyVariant.OverflowOffsetPlusSize => (BigInteger.Zero, U256Max - 31, new BigInteger(64)),
            CopyVariant.HugeDestOffset      => (U256Max, BigInteger.Zero, new BigInteger(32)),
            _                               => (BigInteger.Zero, BigInteger.Zero, new BigInteger(32)),
        };

    // ── Precompile ────────────────────────────────────────────────────────────

    private static void BuildPrecompileCode(PathologicalCase c, List<string> ops)
    {
        // Build the input buffer and place it at memory[0..inputLen]
        var input     = BuildPrecompileInput(c);
        var inputLen  = (ulong)input.Length;
        var precompileAddr = ResolvePrecompileAddress(c.Opcode);

        // Write input bytes to memory (MSTORE for 32-byte chunks, or MSTORE8 per byte for small)
        StoreToMemory(ops, input);

        // 0 gas override for "call with 0 gas" cases
        var gas = c.Param1 == 0 ? BigInteger.Zero : new BigInteger(GasLimit / 2);

        // CALL(gas, precompile_addr, 0, 0, inputLen, 0, 64)
        PushBI(ops, new BigInteger(64));          // retLen
        PushBI(ops, BigInteger.Zero);             // retOff
        PushBI(ops, new BigInteger(inputLen));    // argsLen
        PushBI(ops, BigInteger.Zero);             // argsOff
        PushBI(ops, BigInteger.Zero);             // value
        Push20Addr(ops, precompileAddr);
        PushBI(ops, gas);
        ops.Add("f1"); // CALL
        ops.Add("50"); // POP
        ops.Add("00"); // STOP
    }

    private static byte[] BuildPrecompileInput(PathologicalCase c)
    {
        if (c.ModexpKind.HasValue) return BuildModexpInput(c.ModexpKind.Value);
        if (c.Bn254Kind.HasValue)  return BuildBn254Input(c.Bn254Kind.Value, c.Opcode);
        if (c.Blake2fKind.HasValue) return BuildBlake2fInput(c.Blake2fKind.Value);
        if (c.Opcode == PathOpcode.PrecompileEcpairing && c.Param1.HasValue)
            return new byte[c.Param1.Value > 1000 ? 384 : (int)c.Param1.Value]; // length-parametric
        if (c.PrecompileInput.HasValue) return BuildGenericInput(c.PrecompileInput.Value, c.Opcode);
        return new byte[0];
    }

    private static byte[] BuildModexpInput(ModexpVariant mv)
    {
        // ModExp input: 3×32-byte header (Blen, Elen, Mlen) + B + E + M
        return mv switch
        {
            ModexpVariant.ZeroLengths => new byte[96], // bLen=eLen=mLen=0

            ModexpVariant.HugeDeclaredBase =>
                // bLen=2^64-1, eLen=1, mLen=1 — input truncated to just the 3-word header + 2 bytes
                Concat(
                    U256Word(ulong.MaxValue),   // bLen
                    U256Word(1),                // eLen
                    U256Word(1),                // mLen
                    new byte[] { 0x02 },        // E=2
                    new byte[] { 0x03 }),       // M=3

            ModexpVariant.HugeDeclaredExp =>
                Concat(U256Word(1), U256Word(ulong.MaxValue), U256Word(1),
                       new byte[] { 0x02 }, new byte[] { 0x03 }),

            ModexpVariant.HugeDeclaredMod =>
                Concat(U256Word(1), U256Word(1), U256Word(ulong.MaxValue),
                       new byte[] { 0x02 }, new byte[] { 0x03 }),

            ModexpVariant.AllHuge =>
                Concat(U256Word(ulong.MaxValue), U256Word(ulong.MaxValue), U256Word(ulong.MaxValue)),
            // truncated: no actual B/E/M bytes

            ModexpVariant.TruncatedInput =>
                // declare bLen=32 but only provide 16 bytes of B
                Concat(U256Word(32), U256Word(1), U256Word(1), new byte[16]),

            ModexpVariant.ZeroModulus =>
                Concat(U256Word(1), U256Word(1), U256Word(1),
                       new byte[] { 0x02 }, new byte[] { 0x01 }, new byte[] { 0x00 }),
            // base=2, exp=1, mod=0

            ModexpVariant.Normal =>
                Concat(U256Word(1), U256Word(1), U256Word(1),
                       new byte[] { 0x02 }, new byte[] { 0x01 }, new byte[] { 0x03 }),
            // 2^1 mod 3 = 2

            _ => new byte[0],
        };
    }

    private static byte[] BuildBn254Input(Bn254Variant bv, PathOpcode op)
    {
        // BN254 G1 generator (on curve, valid)
        var Gx = new byte[32];
        Gx[31] = 1;
        var Gy = new byte[32];
        Gy[31] = 2;

        return bv switch
        {
            Bn254Variant.ValidPoint      => Concat(Gx, Gy, Gx, Gy),
            Bn254Variant.ZeroPoint       => new byte[128],
            Bn254Variant.PointNotOnCurve => Concat(Gx, Gx, Gx, Gx), // both Y = X → not on curve
            Bn254Variant.PointInfinityFirst => Concat(new byte[64], Gx, Gy),
            Bn254Variant.WrongInputLength => new byte[127],
            Bn254Variant.AllZeroInput    => new byte[128],
            _ => new byte[128],
        };
    }

    private static byte[] BuildBlake2fInput(Blake2fVariant bv)
    {
        // 213 = 4 (rounds) + 64 (h) + 128 (m) + 16 (t) + 1 (f)
        switch (bv)
        {
            case Blake2fVariant.ValidInput:
            {
                var buf = new byte[213];
                buf[3]   = 1;  // rounds = 1 (big-endian uint32)
                buf[212] = 1;  // f = 1 (final flag)
                return buf;
            }
            case Blake2fVariant.InvalidFinalFlag:
            {
                var buf = new byte[213];
                buf[3]   = 1;  // rounds = 1
                buf[212] = 2;  // f = 2 → invalid (must be 0 or 1)
                return buf;
            }
            case Blake2fVariant.WrongInputLength:
                return new byte[100]; // ≠ 213
            case Blake2fVariant.AllZeroInput:
            default:
                return new byte[213];
        }
    }

    private static byte[] BuildGenericInput(PrecompileInputVariant piv, PathOpcode op)
    {
        var baseSize = op switch
        {
            PathOpcode.PrecompileEcrecover => 128,
            PathOpcode.PrecompileSha256    => 64,
            PathOpcode.PrecompileRipemd160 => 64,
            PathOpcode.PrecompileIdentity  => 64,
            _                              => 64,
        };

        return piv switch
        {
            PrecompileInputVariant.Empty    => new byte[0],
            PrecompileInputVariant.OneByteShort => new byte[baseSize - 1],
            PrecompileInputVariant.Exact    => new byte[baseSize],
            PrecompileInputVariant.Oversized => new byte[baseSize * 4],
            PrecompileInputVariant.AllZero  => new byte[baseSize],
            PrecompileInputVariant.HighBitSet => Enumerable.Repeat((byte)0xFF, baseSize).ToArray(),
            _ => new byte[baseSize],
        };
    }

    // ── Exceptional halt code ─────────────────────────────────────────────────

    private static void BuildExceptionalHaltCode(PathologicalCase c, List<string> ops)
    {
        switch (c.HaltKind)
        {
            case ExceptionalHaltKind.OutOfGas:
                // MSTORE at a huge offset burns gas via memory expansion
                BuildMemoryBoundaryCode(c, ops);
                return;

            case ExceptionalHaltKind.InvalidOpcode:
                ops.Add("fe"); // INVALID
                return;

            case ExceptionalHaltKind.StackUnderflow:
                ops.Add("51"); // MLOAD — pop from empty stack
                ops.Add("00");
                return;

            case ExceptionalHaltKind.StackOverflow:
                // Push 1025 items: exactly 1025 PUSH1 0x00
                // 1024 is the limit, so the 1025th push overflows
                for (int i = 0; i <= 1024; i++) { ops.Add("60"); ops.Add("00"); }
                ops.Add("00");
                return;

            case ExceptionalHaltKind.BadJumpDestination:
                var target = c.Param1 ?? 1;
                PushU(ops, target);           // push jump destination
                ops.Add("56");               // JUMP
                ops.Add("5b");               // JUMPDEST (too late — jump was already taken to wrong place)
                ops.Add("00");
                return;

            case ExceptionalHaltKind.ReturndataCopyOob:
                // Call child that returns 0 bytes, then RETURNDATACOPY offset=1
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a"); ops.Add("f1"); ops.Add("50");
                // RETURNDATACOPY(0, 1, 1) — src offset 1 > returndata length 0
                PushBI(ops, BigInteger.One);  // size
                PushBI(ops, BigInteger.One);  // src offset (OOB)
                PushBI(ops, BigInteger.Zero); // dest
                ops.Add("3e"); // RETURNDATACOPY
                ops.Add("00");
                return;

            case ExceptionalHaltKind.StaticContextMutation:
                // STATICCALL to child that does SSTORE (or LOG if param1=0)
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a"); ops.Add("fa"); // STATICCALL
                ops.Add("50");
                ops.Add("00");
                return;

            case ExceptionalHaltKind.DepthLimitExceeded:
                // Use pre-built deep-call chain — the target code recurses to depth 1024
                // Child's code is built in BuildPrestate
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a"); ops.Add("f1"); // CALL
                ops.Add("50");
                ops.Add("00");
                return;

            default:
                ops.Add("00");
                return;
        }
    }

    // ── CREATE lifecycle ──────────────────────────────────────────────────────

    private static void BuildCreateCode(PathologicalCase c, List<string> ops)
    {
        switch (c.CreateKind)
        {
            case CreateVariant.NormalSmall:
                // CREATE(0, 0, 0) — empty initcode
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                ops.Add("f0");
                ops.Add("50");
                ops.Add("00");
                return;

            case CreateVariant.HugeInitcodeOffset:
                // CREATE(0, huge_offset, 0) — initcode at huge memory offset
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, U256Max - 32);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;

            case CreateVariant.HugeInitcodeSize:
            {
                // CREATE(0, 0, huge_size) — will OOG during memory expansion
                var sz = c.Param1.HasValue ? new BigInteger(c.Param1.Value) : U256Max;
                PushBI(ops, sz);
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;
            }

            case CreateVariant.OogDuringInitcode:
                // Deploy initcode that just burns gas: tight JUMP loop
                // Initcode embedded in the parent's code via CODECOPY then CREATE
                // Simple variant: CREATE passing an offset into our code that contains a loop
                // We'll store a tiny OOG-loop bytecode in memory and CREATE it
                StoreToMemory(ops, new byte[] { 0x5b, 0x60, 0x00, 0x56 }); // JUMPDEST PUSH1 0 JUMP
                PushBI(ops, new BigInteger(4)); PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;

            case CreateVariant.ReturnHugeRuntimeCode:
            {
                // Initcode that returns N bytes of runtime code; triggers EIP-170 or deposit-OOG
                var sz = c.Param1 ?? 24577;
                // Store sz-byte initcode: PUSH32 sz, PUSH1 0, RETURN
                // Actually just MSTORE a length and RETURN(0, sz)
                // Initcode: PUSH3 sz PUSH1 0 RETURN → 6 bytes
                byte[] initcode = BuildReturnSizeInitcode(sz);
                StoreToMemory(ops, initcode);
                PushBI(ops, new BigInteger(initcode.Length));
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;
            }

            case CreateVariant.RevertedCreate:
                // Initcode that REVERTs; caller should see 0 on stack
                StoreToMemory(ops, new byte[] { 0x60, 0x00, 0x60, 0x00, 0xfd }); // PUSH1 0 PUSH1 0 REVERT
                PushBI(ops, new BigInteger(5)); PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;

            case CreateVariant.CreateNested:
                // Initcode that itself does CREATE(0,0,0)
                StoreToMemory(ops, new byte[] { 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0xf0, 0x50, 0x00 });
                PushBI(ops, new BigInteger(9)); PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;

            case CreateVariant.Create2HugeSalt:
            {
                // CREATE2(0, 0, 0, salt) — salt is the boundary value
                var salt = ResolveBoundaryBigInt(c.Boundary ?? BoundaryValue.U256Max, null);
                PushBI(ops, salt);             // salt
                PushBI(ops, BigInteger.Zero);  // size
                PushBI(ops, BigInteger.Zero);  // offset
                PushBI(ops, BigInteger.Zero);  // value
                ops.Add("f5"); ops.Add("50"); ops.Add("00");
                return;
            }

            case CreateVariant.Create2HugeOffset:
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                PushBI(ops, U256Max);           // offset huge
                PushBI(ops, BigInteger.Zero);
                ops.Add("f5"); ops.Add("50"); ops.Add("00");
                return;

            case CreateVariant.NonceRollover:
                // Sender nonce = 2^64-1; CREATE should fail
                // (prestate sets the nonce; code just does CREATE)
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                ops.Add("f0"); ops.Add("50"); ops.Add("00");
                return;

            default:
                ops.Add("00");
                return;
        }
    }

    // ── Stack / depth pressure ────────────────────────────────────────────────

    private static void BuildStackDepthCode(PathologicalCase c, List<string> ops)
    {
        switch (c.StackKind)
        {
            case StackVariant.Push1022Items:
                for (int i = 0; i < 1022; i++) { ops.Add("60"); ops.Add("00"); }
                ops.Add("00");
                return;

            case StackVariant.Push1023Items:
                for (int i = 0; i < 1023; i++) { ops.Add("60"); ops.Add("00"); }
                ops.Add("00");
                return;

            case StackVariant.Push1024Items:
                // Push exactly 1024 → valid; one more would overflow
                for (int i = 0; i < 1024; i++) { ops.Add("60"); ops.Add("00"); }
                ops.Add("00");
                return;

            case StackVariant.Push1025Items:
                // Should hit StackOverflow on the 1025th push
                for (int i = 0; i <= 1024; i++) { ops.Add("60"); ops.Add("00"); }
                ops.Add("00");
                return;

            case StackVariant.NestedCallDepth1023:
            case StackVariant.NestedCallDepth1024:
            case StackVariant.NestedCallDepth1025:
            {
                // The target code recurses via CALL; depth controlled by prestate chain
                // Just issue a CALL to the recursive contract
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a"); ops.Add("f1");
                ops.Add("50"); ops.Add("00");
                return;
            }

            case StackVariant.DeepCreateChain:
            case StackVariant.DeepRevertUnwind:
                // Implemented via nested CALL chain in prestate
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero); PushBI(ops, BigInteger.Zero);
                PushBI(ops, BigInteger.Zero);
                ops.Add("73"); ops.AddRange(Addr20(AddrChild));
                ops.Add("5a"); ops.Add("f1");
                ops.Add("50"); ops.Add("00");
                return;

            default:
            {
                // Dup/Swap at a specific depth
                var depth = (int)(c.Param1 ?? 1022);
                depth = Math.Min(depth, 1023); // cap to avoid hanging in test build
                for (int i = 0; i < depth; i++) { ops.Add("60"); ops.Add("00"); }
                ops.Add(c.Opcode == PathOpcode.Dup ? "80" : "90"); // DUP1 / SWAP1
                ops.Add("00");
                return;
            }
        }
    }

    // ── Arithmetic boundaries ─────────────────────────────────────────────────

    private static void BuildArithCode(PathologicalCase c, List<string> ops)
    {
        var bv = c.Boundary ?? BoundaryValue.U256Max;
        var a  = ResolveBoundaryBigInt(bv, c.Param1);
        var b  = a;

        switch (c.ArithKind)
        {
            case ArithVariant.DivByZero:
            case ArithVariant.ModByZero:
            case ArithVariant.SdivByZero:
                b = BigInteger.Zero;
                break;
            case ArithVariant.SdivNegativeOverflow:
                // (2^255) / (-1)
                a = BigInteger.Pow(2, 255);
                b = U256Max; // -1 in 2s complement
                break;
            case ArithVariant.SarOnMaxSigned:
                a = new BigInteger(255);  // shift
                b = BigInteger.Pow(2, 255) - 1; // max positive signed
                break;
            case ArithVariant.SarOnMinSigned:
                a = BigInteger.One;
                b = BigInteger.Pow(2, 255); // min negative
                break;
            case ArithVariant.ShlByMax:
            case ArithVariant.ShrByMax:
                a = new BigInteger(256); // shift ≥ 256 → 0
                b = U256Max;
                break;
            case ArithVariant.ExpByZero:
                a = U256Max; b = BigInteger.Zero;
                break;
            case ArithVariant.ExpZeroBase:
                a = BigInteger.Zero; b = U256Max;
                break;
            case ArithVariant.ExpLargeBase:
                a = U256Max; b = U256Max;
                break;
            default:
                a = b = ResolveBoundaryBigInt(bv, c.Param1);
                break;
        }

        // For binary opcodes: push b then a (stack: a on top)
        PushBI(ops, b); PushBI(ops, a);

        ops.Add(c.ArithKind switch
        {
            ArithVariant.AddWrap or ArithVariant.AddOverflow256 => "01",   // ADD
            ArithVariant.SubWrap                                => "03",   // SUB
            ArithVariant.MulWrap                                => "02",   // MUL
            ArithVariant.DivByZero                              => "04",   // DIV
            ArithVariant.ModByZero                              => "06",   // MOD
            ArithVariant.SdivByZero or ArithVariant.SdivNegativeOverflow => "05", // SDIV
            ArithVariant.ModNeg                                 => "07",   // SMOD
            ArithVariant.SarOnMaxSigned or ArithVariant.SarOnMinSigned => "1d",   // SAR
            ArithVariant.ShlByMax                               => "1b",   // SHL
            ArithVariant.ShrByMax                               => "1c",   // SHR
            ArithVariant.ExpByZero or ArithVariant.ExpZeroBase
                or ArithVariant.ExpLargeBase                    => "0a",   // EXP
            ArithVariant.SignedCmpMaxMin                        => "13",   // SGT
            _                                                   => "01",
        });
        ops.Add("50"); // POP
        ops.Add("00"); // STOP
    }

    // ── Prestate ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<CampaignAccount> BuildPrestate(PathologicalCase c, string code)
    {
        var accounts = new List<CampaignAccount>
        {
            new() { Address = AddrSender, Balance = LargeBalance, Nonce = 0 },
            new() { Address = AddrTarget, Code = code, Balance = LargeBalance, Nonce = 0 },
        };

        // NonceRollover: sender's code-deploying nonce maxed
        if (c.CreateKind == CreateVariant.NonceRollover)
        {
            accounts[1] = accounts[1] with { Nonce = ulong.MaxValue };
        }

        // Child accounts for cases that need them
        if (NeedsChildAccount(c))
        {
            accounts.Add(BuildChildAccount(c));
        }

        return accounts;
    }

    private static bool NeedsChildAccount(PathologicalCase c) =>
        c.CopySource == CopySource.Returndata ||
        c.HaltKind is ExceptionalHaltKind.ReturndataCopyOob
                   or ExceptionalHaltKind.StaticContextMutation
                   or ExceptionalHaltKind.DepthLimitExceeded ||
        c.StackKind is StackVariant.NestedCallDepth1023
                    or StackVariant.NestedCallDepth1024
                    or StackVariant.NestedCallDepth1025
                    or StackVariant.DeepCreateChain
                    or StackVariant.DeepRevertUnwind ||
        c.MemoryKind is MemoryVariant.CallArgsHugeOffset or MemoryVariant.CallRetHugeOffset ||
        c.Family == PathFamily.BigIntegerNarrowing && c.CopySource != null ||
        (c.Opcode == PathOpcode.Calldatacopy && c.CopySource == CopySource.ExtcodeOf);

    private static CampaignAccount BuildChildAccount(PathologicalCase c)
    {
        // STATICCALL → SSTORE case: child tries SSTORE
        if (c.HaltKind == ExceptionalHaltKind.StaticContextMutation)
        {
            var childCode = c.Param1 == 0
                ? "0x600060006000600060006000a0" // LOG0 (also forbidden in static)
                : "0x6000600055";               // PUSH1 0 PUSH1 0 SSTORE
            return new() { Address = AddrChild, Code = childCode, Balance = LargeBalance };
        }

        // Depth-limit: child has self-recursive CALL code
        if (c.HaltKind == ExceptionalHaltKind.DepthLimitExceeded ||
            c.StackKind is StackVariant.NestedCallDepth1023
                        or StackVariant.NestedCallDepth1024
                        or StackVariant.NestedCallDepth1025)
        {
            // Self-referencing: CALL self with GAS/64; eventually depth limit triggers,
            // CALL returns 0, STOP
            var self = AddrChild.TrimStart('0', 'x');
            return new() { Address = AddrChild, Code = BuildSelfRecurseCode(AddrChild), Balance = LargeBalance };
        }

        // Default: STOP child (just returns 0 bytes)
        return new() { Address = AddrChild, Code = "0x00", Balance = LargeBalance };
    }

    // ── Code helpers ──────────────────────────────────────────────────────────

    /// <summary>Self-recursive call code: CALL self until depth limit.</summary>
    private static string BuildSelfRecurseCode(string selfAddr)
    {
        var ops = new List<string>();
        PushBI(ops, BigInteger.Zero); // retLen
        PushBI(ops, BigInteger.Zero); // retOff
        PushBI(ops, BigInteger.Zero); // argsLen
        PushBI(ops, BigInteger.Zero); // argsOff
        PushBI(ops, BigInteger.Zero); // value
        ops.Add("73"); ops.AddRange(Addr20(selfAddr));
        ops.Add("5a"); // GAS (subject to 63/64 rule)
        ops.Add("f1"); // CALL
        ops.Add("50"); // POP
        ops.Add("00"); // STOP
        return "0x" + string.Join("", ops);
    }

    /// <summary>Initcode that RETURNs exactly N bytes of runtime code (all zeros).</summary>
    private static byte[] BuildReturnSizeInitcode(ulong size)
    {
        // PUSH_N size PUSH1 0 RETURN
        var sizeBytes = BigIntToBeBytes(new BigInteger(size));
        var bytecode  = new List<byte>();
        bytecode.Add((byte)(0x5f + sizeBytes.Length)); // PUSHn
        bytecode.AddRange(sizeBytes);
        bytecode.Add(0x60); bytecode.Add(0x00);        // PUSH1 0 (offset)
        bytecode.Add(0xf3);                             // RETURN
        return bytecode.ToArray();
    }

    /// <summary>Fixed-size RETURN: child RETURN N zero bytes.</summary>
    private static string BuildFixedReturnCode(ulong size)
    {
        var ops = new List<string>();
        PushU(ops, size);             // size
        PushBI(ops, BigInteger.Zero); // offset
        ops.Add("f3"); // RETURN
        return "0x" + string.Join("", ops);
    }

    // ── Memory helpers ────────────────────────────────────────────────────────

    /// <summary>Store byte array into memory starting at offset 0, using MSTORE8 per byte.</summary>
    private static void StoreToMemory(List<string> ops, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            ops.Add("60"); ops.Add($"{data[i]:x2}"); // PUSH1 byte
            PushU(ops, (ulong)i);                     // offset
            ops.Add("53"); // MSTORE8
        }
    }

    /// <summary>Store precompile address for CALL — handles both short and 20-byte addresses.</summary>
    private static string ResolvePrecompileAddress(PathOpcode op) => op switch
    {
        PathOpcode.PrecompileEcrecover => "0x0000000000000000000000000000000000000001",
        PathOpcode.PrecompileSha256    => "0x0000000000000000000000000000000000000002",
        PathOpcode.PrecompileRipemd160 => "0x0000000000000000000000000000000000000003",
        PathOpcode.PrecompileIdentity  => "0x0000000000000000000000000000000000000004",
        PathOpcode.PrecompileModexp    => "0x0000000000000000000000000000000000000005",
        PathOpcode.PrecompileEcadd     => "0x0000000000000000000000000000000000000006",
        PathOpcode.PrecompileEcmul     => "0x0000000000000000000000000000000000000007",
        PathOpcode.PrecompileEcpairing => "0x0000000000000000000000000000000000000008",
        PathOpcode.PrecompileBlake2f   => "0x0000000000000000000000000000000000000009",
        _                              => "0x0000000000000000000000000000000000000001",
    };

    // ── Calldata builder ──────────────────────────────────────────────────────

    private static string BuildCalldata(PathologicalCase c)
    {
        // CALLDATACOPY cases benefit from non-empty calldata
        if (c.CopySource == CopySource.Calldata)
            return "0x" + string.Concat(Enumerable.Repeat("aa", 64)); // 64 bytes
        return "0x";
    }

    // ── Emit helpers ─────────────────────────────────────────────────────────

    /// <summary>Push a BigInteger as a minimal PUSH_N instruction.</summary>
    private static void PushBI(List<string> ops, BigInteger v)
    {
        if (v == BigInteger.Zero) { ops.Add("60"); ops.Add("00"); return; }
        // Mask to 256 bits
        var masked = ((v % (BigInteger.Pow(2, 256)) + BigInteger.Pow(2, 256)) % BigInteger.Pow(2, 256));
        var bytes  = BigIntToBeBytes(masked);
        ops.Add($"{0x5f + bytes.Length:x2}");
        foreach (var b in bytes) ops.Add($"{b:x2}");
    }

    private static void PushU(List<string> ops, ulong v)
    {
        if (v == 0) { ops.Add("60"); ops.Add("00"); return; }
        var bytes = new List<byte>();
        while (v > 0) { bytes.Insert(0, (byte)(v & 0xff)); v >>= 8; }
        ops.Add($"{0x5f + bytes.Count:x2}");
        foreach (var b in bytes) ops.Add($"{b:x2}");
    }

    private static void Push20Addr(List<string> ops, string addr)
    {
        ops.Add("73"); ops.AddRange(Addr20(addr));
    }

    private static IEnumerable<string> Addr20(string addr)
    {
        var hex = addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;
        hex = hex.PadLeft(40, '0')[^40..];
        for (int i = 0; i < 40; i += 2) yield return hex.Substring(i, 2);
    }

    // ── BigInteger resolution helpers ─────────────────────────────────────────

    private static readonly BigInteger U256Max =
        BigInteger.Pow(2, 256) - 1;

    private static BigInteger ResolveBoundaryBigInt(BoundaryValue bv, ulong? param1)
    {
        // Sentinel values
        if (bv == BoundaryValue.U255) return BigInteger.Pow(2, 255) - 1;
        if (bv == BoundaryValue.U256Max) return BigInteger.Pow(2, 256) - 1;
        if (bv == BoundaryValue.OffsetPlusLengthOverflow) return BigInteger.Pow(2, 256) - 32;

        if (param1.HasValue && (ulong)bv >= (ulong)BoundaryValue.U32Max)
            return new BigInteger(param1.Value);

        return new BigInteger((ulong)bv);
    }

    private static byte[] BigIntToBeBytes(BigInteger v)
    {
        if (v == BigInteger.Zero) return new byte[] { 0 };
        // ToByteArray returns little-endian, possibly with sign byte
        var le  = v.ToByteArray(isUnsigned: true, isBigEndian: false);
        var be  = le.Reverse().SkipWhile(b => b == 0).ToArray();
        return be.Length == 0 ? new byte[] { 0 } : be;
    }

    // ── Binary helpers ────────────────────────────────────────────────────────

    private static byte[] U256Word(ulong v)
    {
        var w = new byte[32];
        for (int i = 7; i >= 0; i--) { w[24 + i] = (byte)(v & 0xff); v >>= 8; }
        return w;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        int pos = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, result, pos, p.Length); pos += p.Length; }
        return result;
    }
}
