using System.Numerics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math.EC.Custom.Sec;
using Scrutor.Core.Forks;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.Execution;

/// <summary>
/// Ethereum precompiled contracts.
/// Precompile availability is determined by <see cref="IForkRules.PrecompileCount"/>.
/// </summary>
public static class Precompiles
{
    // ── EIP-7951: P256Verify address is 0x0100 (two-byte, not single-byte) ──
    private static readonly byte[] P256VerifyAddressBytes =
        new byte[] { 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,0 };

    public static bool IsP256Verify(Address address)
        => address.Bytes.AsSpan().SequenceEqual(P256VerifyAddressBytes);

    /// <summary>Returns true when <paramref name="address"/> is an active precompile for the given fork rules.</summary>
    public static bool IsPrecompile(Address address, IForkRules rules)
    {
        // EIP-7951: P256Verify at 0x0100 (Osaka+)
        if (rules.HasEip7951P256Verify && IsP256Verify(address)) return true;

        var bytes = address.Bytes;
        for (int i = 0; i < 19; i++)
            if (bytes[i] != 0) return false;
        var id = bytes[19];
        return id >= 1 && id <= rules.PrecompileCount;
    }

    /// <summary>Returns true when <paramref name="address"/> id is in [1, precompileCount].</summary>
    public static bool IsPrecompile(Address address, int precompileCount)
    {
        var bytes = address.Bytes;
        for (int i = 0; i < 19; i++)
            if (bytes[i] != 0) return false;
        var id = bytes[19];
        return id >= 1 && id <= precompileCount;
    }

    /// <summary>Legacy overload — kept for call sites not yet migrated.</summary>
    public static bool IsPrecompile(Address address, bool kzgEnabled = true, bool blsEnabled = false)
    {
        var bytes = address.Bytes;
        for (int i = 0; i < 19; i++)
            if (bytes[i] != 0) return false;
        var id = bytes[19];
        if (id is >= 1 and <= 9) return true;
        if (id == 10) return kzgEnabled;
        if (id is >= 0x0b and <= 0x13) return blsEnabled;
        return false;
    }

    public static (byte[]? output, ulong gasUsed) Execute(Address address, byte[] input, ulong gasLimit, int precompileCount)
    {
        var id = address.Bytes[19];
        if (id < 1 || id > precompileCount) return (Array.Empty<byte>(), 0);
        bool kzgEnabled = precompileCount >= 10;
        bool blsEnabled = precompileCount >= 0x0b;
        bool eip2565 = precompileCount >= 9; // Berlin+ has 9 precompiles before Cancun adds 10
        return Execute(address, input, gasLimit, kzgEnabled, blsEnabled, eip2565);
    }

    /// <summary>Execute with full IForkRules — preferred overload for fork-aware gas.</summary>
    public static (byte[]? output, ulong gasUsed) Execute(Address address, byte[] input, ulong gasLimit, IForkRules rules)
    {
        // EIP-7951: P256Verify at 0x0100 (Osaka+) — check before single-byte dispatch
        if (rules.HasEip7951P256Verify && IsP256Verify(address))
            return P256Verify(input, gasLimit);

        var id = address.Bytes[19];
        if (id < 1 || id > rules.PrecompileCount) return (Array.Empty<byte>(), 0);
        // BN254 precompiles have fork-dependent gas (EIP-1108 changed Byzantium→Istanbul)
        return id switch
        {
            5 => ModExp(input, gasLimit, rules.HasEip2565ModExpPricing, rules.HasEip7883ModExpIncrease),
            6 => BnAdd(input, gasLimit, rules.BnAddGas),
            7 => BnMul(input, gasLimit, rules.BnMulGas),
            8 => BnPairing(input, gasLimit, rules.BnPairingBaseGas, rules.BnPairingPerPointGas),
            _ => Execute(address, input, gasLimit,
                         kzgEnabled: rules.HasEip4844BlobTx,
                         blsEnabled: rules.HasEip7702SetCode,
                         eip2565: rules.HasEip2565ModExpPricing)
        };
    }

    public static ExecutionResult ExecuteAsResult(Address address, byte[] input, ulong gasLimit, int precompileCount)
    {
        var (output, gasUsed) = Execute(address, input, gasLimit, precompileCount);
        if (output == null) return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        return ExecutionResult.Success(gasUsed, output);
    }

    public static ExecutionResult ExecuteAsResult(Address address, byte[] input, ulong gasLimit, IForkRules rules)
    {
        var (output, gasUsed) = Execute(address, input, gasLimit, rules);
        if (output == null) return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        return ExecutionResult.Success(gasUsed, output);
    }

    public static (byte[]? output, ulong gasUsed) Execute(Address address, byte[] input, ulong gasLimit, bool kzgEnabled = true, bool blsEnabled = false, bool eip2565 = true)
    {
        var id = address.Bytes[19];
        return id switch
        {
            1 => EcRecover(input, gasLimit),
            2 => Sha256Precompile(input, gasLimit),
            3 => Ripemd160Precompile(input, gasLimit),
            4 => Identity(input, gasLimit),
            5 => ModExp(input, gasLimit, eip2565),
            6 => BnAdd(input, gasLimit),
            7 => BnMul(input, gasLimit),
            8 => BnPairing(input, gasLimit),
            9 => Blake2F(input, gasLimit),
            10 => kzgEnabled ? KzgPointEvaluation(input, gasLimit) : (Array.Empty<byte>(), 0),
            >= 0x0b and <= 0x11 => blsEnabled ? Bls12381Precompiles.Execute(id, input, gasLimit) : (Array.Empty<byte>(), 0),
            _ => (Array.Empty<byte>(), 0)
        };
    }

    /// <summary>
    /// Convenience wrapper that returns an <see cref="ExecutionResult"/> so callers
    /// in CALL/STATICCALL opcodes don't need to adapt the tuple themselves.
    /// </summary>
    public static ExecutionResult ExecuteAsResult(Address address, byte[] input, ulong gasLimit, bool kzgEnabled = true, bool blsEnabled = false)
    {
        var (output, gasUsed) = Execute(address, input, gasLimit, kzgEnabled, blsEnabled);
        if (output == null)
            return ExecutionResult.Failure(EvmError.OutOfGas, gasLimit);
        return ExecutionResult.Success(gasUsed, output);
    }

    // ── 0x01: ecRecover ──────────────────────────────────────────────────────
    private static (byte[]? output, ulong gasUsed) EcRecover(byte[] input, ulong gasLimit)
    {
        const ulong gas = 3000;
        if (gasLimit < gas) return (null, gasLimit);

        var padded = new byte[128];
        var copyLen = Math.Min(128, input.Length);
        if (copyLen > 0) Array.Copy(input, 0, padded, 0, copyLen);

        var hash = padded[..32];
        var vBig = new BigInteger(padded[32..64], isUnsigned: true, isBigEndian: true);
        var r = padded[64..96];
        var s = padded[96..128];

        // v must be exactly 27 or 28 — anything else (including values that don't fit int32) is invalid
        if (vBig != 27 && vBig != 28) return (Array.Empty<byte>(), gas);
        int v = (int)vBig;

        try
        {
            var recovered = CryptoUtils.RecoverAddressForPrecompile(hash, v, r, s);
            if (recovered is null) return (Array.Empty<byte>(), gas);
            var output = new byte[32];
            Array.Copy(recovered.Value.Bytes, 0, output, 12, 20);
            return (output, gas);
        }
        catch
        {
            return (Array.Empty<byte>(), gas);
        }
    }

    // ── 0x02: SHA-256 ────────────────────────────────────────────────────────
    private static (byte[]? output, ulong gasUsed) Sha256Precompile(byte[] input, ulong gasLimit)
    {
        ulong gas = 60 + 12 * (ulong)((input.Length + 31) / 32);
        if (gasLimit < gas) return (null, gasLimit);
        return (SHA256.HashData(input), gas);
    }

    // ── 0x03: RIPEMD-160 ─────────────────────────────────────────────────────
    private static (byte[]? output, ulong gasUsed) Ripemd160Precompile(byte[] input, ulong gasLimit)
    {
        ulong gas = 600 + 120 * (ulong)((input.Length + 31) / 32);
        if (gasLimit < gas) return (null, gasLimit);

        var ripemd = new RipeMD160Digest();
        ripemd.BlockUpdate(input, 0, input.Length);
        var hash = new byte[20];
        ripemd.DoFinal(hash, 0);

        var output = new byte[32];
        Array.Copy(hash, 0, output, 12, 20);
        return (output, gas);
    }

    // ── 0x04: Identity ───────────────────────────────────────────────────────
    private static (byte[]? output, ulong gasUsed) Identity(byte[] input, ulong gasLimit)
    {
        ulong gas = 15 + 3 * (ulong)((input.Length + 31) / 32);
        if (gasLimit < gas) return (null, gasLimit);
        return ((byte[])input.Clone(), gas);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x05: MODEXP  (EIP-198 / EIP-2565)
    // ══════════════════════════════════════════════════════════════════════════
    private static (byte[]? output, ulong gasUsed) ModExp(byte[] input, ulong gasLimit, bool eip2565 = true, bool eip7883 = false)
    {
        var bSizeBig = ReadUint256(input, 0);
        var eSizeBig = ReadUint256(input, 32);
        var mSizeBig = ReadUint256(input, 64);

        // EIP-7823 (Osaka+): hard cap 1024 on each length field.
        // Pre-Osaka EELS historically allowed larger declared lengths (up to 8192).
        BigInteger maxDeclared = eip7883 ? 1024 : 8192;
        if (bSizeBig > maxDeclared || eSizeBig > maxDeclared || mSizeBig > maxDeclared)
            return (null, gasLimit); // ExceptionalHalt: consume all remaining gas

        int bLen = (int)bSizeBig, eLen = (int)eSizeBig, mLen = (int)mSizeBig;

        int pos = 96;
        var bBytes = ReadPadded(input, ref pos, bLen);
        var eBytes = ReadPadded(input, ref pos, eLen);
        var mBytes = ReadPadded(input, ref pos, mLen);

        var gas = ModExpGas(bLen, eLen, mLen, eBytes, eip2565, eip7883);
        if (gas > gasLimit) return (null, gasLimit);

        // EELS Osaka: base_length == 0 and modulus_length == 0 → empty output (still charge gas).
        if (bLen == 0 && mLen == 0) return (Array.Empty<byte>(), gas);

        if (mLen == 0) return (Array.Empty<byte>(), gas);

        var B = bLen > 0 ? new BigInteger(bBytes, isUnsigned: true, isBigEndian: true) : BigInteger.Zero;
        var E = eLen > 0 ? new BigInteger(eBytes, isUnsigned: true, isBigEndian: true) : BigInteger.Zero;
        var M = mLen > 0 ? new BigInteger(mBytes, isUnsigned: true, isBigEndian: true) : BigInteger.Zero;

        byte[] result;
        if (M.IsZero)
        {
            result = new byte[mLen];
        }
        else
        {
            var R = BigInteger.ModPow(B, E, M);
            var rBytes = R.ToByteArray(isUnsigned: true, isBigEndian: true);
            result = new byte[mLen];
            int cl = Math.Min(rBytes.Length, mLen);
            Array.Copy(rBytes, 0, result, mLen - cl, cl);
        }

        return (result, gas);
    }

    /// <summary>
    /// MODEXP gas pricing:
    /// <list type="bullet">
    /// <item>EIP-198 (pre-Berlin): piecewise complexity, /20, no floor.</item>
    /// <item>EIP-2565 (Berlin–Prague): complexity = words², /3, floor 200.</item>
    /// <item>EIP-7883 (Osaka+): complexity = 16 if maxLen≤32 else 2·words²; no /3; floor 500;
    ///       exp&gt;32 uses 16×(expLen−32) (was 8×).</item>
    /// </list>
    /// Matches EELS <c>ethereum/forks/osaka/vm/precompiled_contracts/modexp.py</c>.
    /// </summary>
    internal static ulong ModExpGas(int baseLen, int expLen, int modLen, byte[] expBytes, bool eip2565 = true, bool eip7883 = false)
    {
        long maxLen = Math.Max(baseLen, modLen);

        // Multiplication complexity
        BigInteger multComp;
        if (eip7883)
        {
            // EIP-7883 §3: min complexity 16; when max_length > 32 use 2 * words²
            // (NOT words² alone — that was the undercharge on large base/modulus).
            if (maxLen <= 32)
            {
                multComp = 16;
            }
            else
            {
                var words = (maxLen + 7) / 8;
                multComp = (BigInteger)2 * words * words;
            }
        }
        else if (eip2565)
        {
            // EIP-2565 (Berlin+): complexity = ceil(maxLen / 8) ^ 2
            var words = (maxLen + 7) / 8;
            multComp = (BigInteger)words * words;
        }
        else
        {
            // EIP-198 (Byzantium–Istanbul): tiered piecewise complexity
            if (maxLen <= 64)
                multComp = (BigInteger)maxLen * maxLen;
            else if (maxLen <= 1024)
                multComp = (BigInteger)maxLen * maxLen / 4 + 96 * maxLen - 3072;
            else
                multComp = (BigInteger)maxLen * maxLen / 16 + 480 * maxLen - 199680;
        }

        // Iteration count (adjusted exponent length)
        // EELS: bit_length(head) - 1 when head != 0  == MsBitIndex for BigInteger > 0
        BigInteger iterCount;
        if (expLen <= 32)
        {
            var expVal = expBytes.Length > 0
                ? new BigInteger(expBytes, isUnsigned: true, isBigEndian: true)
                : BigInteger.Zero;
            if (expVal.IsZero)
                iterCount = BigInteger.Zero;
            else
                iterCount = MsBitIndex(expVal);
        }
        else
        {
            // First 32 bytes of the exponent (big-endian head)
            var highBytes = expBytes.Length >= 32
                ? expBytes.AsSpan(0, 32).ToArray()
                : PadLeft(expBytes, 32);
            var expHead = new BigInteger(highBytes, isUnsigned: true, isBigEndian: true);
            var bitLen = expHead.IsZero ? 0 : MsBitIndex(expHead);
            // EIP-7883: 16×(expLen−32); EIP-2565/198: 8×(expLen−32)
            int expMul = eip7883 ? 16 : 8;
            iterCount = (BigInteger)(expMul * (expLen - 32)) + bitLen;
        }

        if (iterCount < 1) iterCount = BigInteger.One;

        if (eip7883)
        {
            // floor 500, no /3
            var gasRaw = multComp * iterCount;
            if (gasRaw > 10_000_000_000UL) return 10_000_000_000UL;
            return Math.Max(500UL, (ulong)gasRaw);
        }

        if (eip2565)
        {
            // EIP-2565: divisor=3, floor=200
            var gasRaw = multComp * iterCount / 3;
            if (gasRaw > 10_000_000_000UL) return 10_000_000_000UL;
            return Math.Max(200UL, (ulong)gasRaw);
        }

        // EIP-198: divisor=20, no floor
        {
            var gasRaw = multComp * iterCount / 20;
            if (gasRaw > 10_000_000_000UL) return 10_000_000_000UL;
            return gasRaw.IsZero ? 0UL : (ulong)gasRaw;
        }
    }

    private static byte[] PadLeft(byte[] src, int targetLen)
    {
        if (src.Length >= targetLen) return src;
        var buf = new byte[targetLen];
        Array.Copy(src, 0, buf, targetLen - src.Length, src.Length);
        return buf;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x06: BN254 Add  (EIP-196, Istanbul gas = 150; Byzantium = 500)
    // ══════════════════════════════════════════════════════════════════════════
    private static (byte[]? output, ulong gasUsed) BnAdd(byte[] input, ulong gasLimit, ulong gas = 150)
    {
        if (gasLimit < gas) return (null, gasLimit);

        try
        {
            var padded = PadRight(input, 128);
            var p1 = DecodePoint(padded, 0);
            var p2 = DecodePoint(padded, 64);

            var sum = p1.Add(p2).Normalize();
            return (EncodePoint(sum), gas);
        }
        catch
        {
            // EIP-196: invalid input → error, consume all provided gas
            return (null, gasLimit);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x07: BN254 Scalar Mul  (EIP-196, Istanbul gas = 6000; Byzantium = 40000)
    // ══════════════════════════════════════════════════════════════════════════
    private static (byte[]? output, ulong gasUsed) BnMul(byte[] input, ulong gasLimit, ulong gas = 6000)
    {
        if (gasLimit < gas) return (null, gasLimit);

        try
        {
            var padded = PadRight(input, 96);
            var p = DecodePoint(padded, 0);
            var scalar = new Org.BouncyCastle.Math.BigInteger(1, padded[64..96]);
            var result = p.Multiply(scalar).Normalize();
            return (EncodePoint(result), gas);
        }
        catch
        {
            // EIP-196: invalid input → error, consume all provided gas
            return (null, gasLimit);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x08: BN254 Pairing  (EIP-197, Istanbul gas = 45000 + k*34000;
    //                         Byzantium gas = 100000 + k*80000)
    //
    //  Gas: baseGas + perPointGas per pair.
    //  Bad input length (not multiple of 192) → revert (null = OOG sentinel).
    //  Any G1/G2 decode failure → revert.
    //  k == 0 → success, output = 1  (empty product of pairings = 1 in GT).
    //  Otherwise: Ate pairing check; return 32-byte 1 if product = GT.1 else 0.
    // ══════════════════════════════════════════════════════════════════════════
    private static (byte[]? output, ulong gasUsed) BnPairing(byte[] input, ulong gasLimit, ulong baseGas = 45_000, ulong perPointGas = 34_000)
    {
        // Bad length → revert (null means "consume all gas / revert" in our convention)
        if (input.Length % 192 != 0) return (null, gasLimit);

        int k = input.Length / 192;
        ulong gas = baseGas + (ulong)k * perPointGas;
        if (gasLimit < gas) return (null, gasLimit);

        var output = Bn254Pairing.Run(input);
        if (output == null) return (null, gasLimit); // decode failure → revert

        return (output, gas);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x09: BLAKE2f compression  (EIP-152)
    // ══════════════════════════════════════════════════════════════════════════
    private static (byte[]? output, ulong gasUsed) Blake2F(byte[] input, ulong gasLimit)
    {
        if (input.Length != 213)
            return (null, gasLimit); // Validation failure must consume all provided gas

        byte finalByte = input[212];
        if (finalByte != 0 && finalByte != 1)
            return (null, gasLimit); // Validation failure must consume all provided gas

        uint rounds = (uint)((input[0] << 24) | (input[1] << 16) | (input[2] << 8) | input[3]);
        ulong gas = rounds;
        if (gasLimit < gas) return (null, gasLimit);

        // Parse h (8 x uint64 LE), m (16 x uint64 LE), t (2 x uint64 LE), f (bool)
        var h = new ulong[8];
        var m = new ulong[16];
        var t = new ulong[2];

        int off = 4;
        for (int i = 0; i < 8; i++) { h[i] = BitConverter.ToUInt64(input, off); off += 8; }
        for (int i = 0; i < 16; i++) { m[i] = BitConverter.ToUInt64(input, off); off += 8; }
        t[0] = BitConverter.ToUInt64(input, off); off += 8;
        t[1] = BitConverter.ToUInt64(input, off);
        bool final_ = finalByte == 1;

        Blake2bCompress(h, m, t, final_, rounds);

        var output = new byte[64];
        for (int i = 0; i < 8; i++)
            BitConverter.TryWriteBytes(output.AsSpan(i * 8), h[i]);
        return (output, gas);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x0A: KZG Point Evaluation (EIP-4844)
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly byte[] KzgConstantsOutput;
    private static IntPtr _kzgSetup = IntPtr.Zero;
    private static readonly object _kzgLock = new object();

    static Precompiles()
    {
        // 4096 (FIELD_ELEMENTS_PER_BLOB) as 32-byte BE
        var p1 = new byte[32];
        p1[30] = 0x10;
        p1[31] = 0x00;

        // 52435875175126190479447740508185965837690552500527637822603658699938581184513 (BLS_MODULUS) as 32-byte BE
        var p2 = new byte[32]
        {
            0x73, 0xed, 0xa7, 0x53, 0x29, 0x9d, 0x7d, 0x48,
            0x33, 0x39, 0xd8, 0x08, 0x09, 0xa1, 0xd8, 0x05,
            0x53, 0xbd, 0xa4, 0x02, 0xff, 0xfe, 0x5b, 0xfe,
            0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x01
        };

        KzgConstantsOutput = new byte[64];
        Array.Copy(p1, 0, KzgConstantsOutput, 0, 32);
        Array.Copy(p2, 0, KzgConstantsOutput, 32, 32);
    }

    private static (byte[]? output, ulong gasUsed) KzgPointEvaluation(byte[] input, ulong gasLimit)
    {
        const ulong gas = 50_000;
        if (gasLimit < gas) return (null, gasLimit);
        
        if (input.Length != 192) return (null, gasLimit);

        if (_kzgSetup == IntPtr.Zero)
        {
            lock (_kzgLock)
            {
                if (_kzgSetup == IntPtr.Zero)
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "kzg_trusted_setup.txt");
                    _kzgSetup = Ckzg.Ckzg.LoadTrustedSetup(path, 0);
                    if (_kzgSetup == IntPtr.Zero)
                        throw new InvalidOperationException("Failed to load KZG trusted setup.");
                }
            }
        }

        var versionedHash = input[0..32];
        var z = input[32..64];
        var y = input[64..96];
        var commitment = input[96..144];
        var proof = input[144..192];

        // 1. Validate versioned hash matches commitment
        var expectedVersionedHash = new byte[32];
        expectedVersionedHash[0] = 0x01;
        var sha = SHA256.HashData(commitment);
        Array.Copy(sha, 1, expectedVersionedHash, 1, 31);

        if (!versionedHash.SequenceEqual(expectedVersionedHash))
            return (null, gasLimit);

        // 2. Verify KZG proof
        try
        {
            bool valid = Ckzg.Ckzg.VerifyKzgProof(commitment, z, y, proof, _kzgSetup);
            if (!valid) return (null, gasLimit);
        }
        catch
        {
            return (null, gasLimit);
        }

        return (KzgConstantsOutput, gas);
    }

    // ── BLAKE2b F compression function ───────────────────────────────────────
    // Reference: RFC 7693 §3.2
    private static readonly ulong[] Blake2bIV =
    {
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b,
        0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f,
        0x1f83d9abfb41bd6b, 0x5be0cd19137e2179
    };

    private static readonly int[] Blake2bSigma =
    {
         0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15,
        14,10, 4, 8, 9,15,13, 6, 1,12, 0, 2,11, 7, 5, 3,
        11, 8,12, 0, 5, 2,15,13,10,14, 3, 6, 7, 1, 9, 4,
         7, 9, 3, 1,13,12,11,14, 2, 6, 5,10, 4, 0,15, 8,
         9, 0, 5, 7, 2, 4,10,15,14, 1,11,12, 6, 8, 3,13,
         2,12, 6,10, 0,11, 8, 3, 4,13, 7, 5,15,14, 1, 9,
        12, 5, 1,15,14,13, 4,10, 0, 7, 6, 3, 9, 2, 8,11,
        13,11, 7,14,12, 1, 3, 9, 5, 0,15, 4, 8, 6, 2,10,
         6,15,14, 9,11, 3, 0, 8,12, 2,13, 7, 1, 4,10, 5,
        10, 2, 8, 4, 7, 6, 1, 5,15,11, 9,14, 3,12,13, 0,
    };

    private static void Blake2bCompress(ulong[] h, ulong[] m, ulong[] t, bool f, uint rounds)
    {
        var v = new ulong[16];
        Array.Copy(h, 0, v, 0, 8);
        Array.Copy(Blake2bIV, 0, v, 8, 8);

        v[12] ^= t[0];
        v[13] ^= t[1];
        if (f) v[14] = ~v[14];

        for (uint r = 0; r < rounds; r++)
        {
            int s = (int)(r % 10) * 16;

            G(ref v[0], ref v[4], ref v[ 8], ref v[12], m[Blake2bSigma[s +  0]], m[Blake2bSigma[s +  1]]);
            G(ref v[1], ref v[5], ref v[ 9], ref v[13], m[Blake2bSigma[s +  2]], m[Blake2bSigma[s +  3]]);
            G(ref v[2], ref v[6], ref v[10], ref v[14], m[Blake2bSigma[s +  4]], m[Blake2bSigma[s +  5]]);
            G(ref v[3], ref v[7], ref v[11], ref v[15], m[Blake2bSigma[s +  6]], m[Blake2bSigma[s +  7]]);
            G(ref v[0], ref v[5], ref v[10], ref v[15], m[Blake2bSigma[s +  8]], m[Blake2bSigma[s +  9]]);
            G(ref v[1], ref v[6], ref v[11], ref v[12], m[Blake2bSigma[s + 10]], m[Blake2bSigma[s + 11]]);
            G(ref v[2], ref v[7], ref v[ 8], ref v[13], m[Blake2bSigma[s + 12]], m[Blake2bSigma[s + 13]]);
            G(ref v[3], ref v[4], ref v[ 9], ref v[14], m[Blake2bSigma[s + 14]], m[Blake2bSigma[s + 15]]);
        }

        for (int i = 0; i < 8; i++)
            h[i] ^= v[i] ^ v[i + 8];
    }

    private static void G(ref ulong a, ref ulong b, ref ulong c, ref ulong d, ulong x, ulong y)
    {
        a = a + b + x;
        d = RotateRight(d ^ a, 32);
        c = c + d;
        b = RotateRight(b ^ c, 24);
        a = a + b + y;
        d = RotateRight(d ^ a, 16);
        c = c + d;
        b = RotateRight(b ^ c, 63);
    }

    private static ulong RotateRight(ulong val, int bits) =>
        (val >> bits) | (val << (64 - bits));

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static BigInteger ReadUint256(byte[] src, int offset)
    {
        if (offset >= src.Length) return BigInteger.Zero;
        var slice = src[offset..Math.Min(offset + 32, src.Length)];
        if (slice.Length < 32) slice = PadRight(slice, 32);
        return new BigInteger(slice, isUnsigned: true, isBigEndian: true);
    }

    private static byte[] ReadPadded(byte[] src, ref int pos, int length)
    {
        if (length == 0) return Array.Empty<byte>();
        var buf = new byte[length];
        int avail = Math.Max(0, Math.Min(length, src.Length - pos));
        if (avail > 0) Array.Copy(src, pos, buf, 0, avail);
        pos += length;
        return buf;
    }

    private static byte[] PadRight(byte[] src, int targetLen)
    {
        if (src.Length >= targetLen) return src;
        var buf = new byte[targetLen];
        Array.Copy(src, 0, buf, 0, src.Length);
        return buf;
    }

    /// <summary>Index of the most-significant bit (0-based), i.e. floor(log2(n)) for n > 0.</summary>
    private static int MsBitIndex(BigInteger n)
    {
        if (n <= BigInteger.Zero) return 0;
        int bits = 0;
        var v = n;
        while (v > BigInteger.Zero) { bits++; v >>= 1; }
        return bits - 1;
    }

    // ── BN254 curve helpers (alt_bn128 / bn256) ──────────────────────────────

    private static Org.BouncyCastle.Math.EC.ECPoint DecodePoint(byte[] data, int offset)
    {
        var x = new Org.BouncyCastle.Math.BigInteger(1, data[offset..(offset + 32)]);
        var y = new Org.BouncyCastle.Math.BigInteger(1, data[(offset + 32)..(offset + 64)]);
        var curve = Bn254CurveParams.Curve;
        if (x.SignValue == 0 && y.SignValue == 0)
            return curve.Infinity;
        var point = curve.CreatePoint(x, y);
        // EIP-196: input point not on the curve → invalid input → throw so callers return empty
        if (!point.IsValid())
            throw new InvalidOperationException("BN254 point not on curve");
        return point;
    }

    private static byte[] EncodePoint(Org.BouncyCastle.Math.EC.ECPoint p)
    {
        if (p.IsInfinity)
            return new byte[64];
        var x = p.AffineXCoord.ToBigInteger().ToByteArrayUnsigned();
        var y = p.AffineYCoord.ToBigInteger().ToByteArrayUnsigned();
        var result = new byte[64];
        Array.Copy(x, 0, result, 32 - x.Length, x.Length);
        Array.Copy(y, 0, result, 64 - y.Length, y.Length);
        return result;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  0x0100: P256VERIFY  (EIP-7951, Osaka+)
    //  secp256r1 / P-256 ECDSA signature verification
    //  Input:  160 bytes — hash(32) | r(32) | s(32) | qx(32) | qy(32)
    //  Output: 32-byte 1 on success, empty bytes on any failure
    //  Gas:    6900 flat, consumed on success AND failure
    // ══════════════════════════════════════════════════════════════════════════
    private const ulong P256VerifyGas = 6_900;

    // secp256r1 (P-256) curve order n
    private static readonly BigInteger P256Order =
        BigInteger.Parse("0115792089210356248762697446949407573529996955224135760342422259061068512044369",
            System.Globalization.NumberStyles.Integer);

    // secp256r1 field prime p
    private static readonly BigInteger P256Prime =
        BigInteger.Parse("0115792089210356248762697446949407573530086143415290314195533631308867097853951",
            System.Globalization.NumberStyles.Integer);

    private static (byte[]? output, ulong gasUsed) P256Verify(byte[] input, ulong gasLimit)
    {
        // EIP-7951: gas is always consumed — return empty on ANY failure
        if (gasLimit < P256VerifyGas) return (null, gasLimit); // OOG

        // 1. Input must be exactly 160 bytes
        if (input.Length != 160)
            return (Array.Empty<byte>(), P256VerifyGas);

        var hash = input[0..32];
        var r    = new BigInteger(input[32..64],  isBigEndian: true, isUnsigned: true);
        var s    = new BigInteger(input[64..96],  isBigEndian: true, isUnsigned: true);
        var qx   = new BigInteger(input[96..128], isBigEndian: true, isUnsigned: true);
        var qy   = new BigInteger(input[128..160],isBigEndian: true, isUnsigned: true);

        // 2. r, s must be in (0, n)
        if (r <= BigInteger.Zero || r >= P256Order) return (Array.Empty<byte>(), P256VerifyGas);
        if (s <= BigInteger.Zero || s >= P256Order) return (Array.Empty<byte>(), P256VerifyGas);

        // 3. qx, qy must be in [0, p)
        if (qx < BigInteger.Zero || qx >= P256Prime) return (Array.Empty<byte>(), P256VerifyGas);
        if (qy < BigInteger.Zero || qy >= P256Prime) return (Array.Empty<byte>(), P256VerifyGas);

        // 4. Point at infinity check
        if (qx == BigInteger.Zero && qy == BigInteger.Zero) return (Array.Empty<byte>(), P256VerifyGas);

        // 5. Verify using .NET's ECDsa (uses OS crypto — fast, constant-time)
        try
        {
            // Build ECParameters for secp256r1
            var ecParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = PadTo32(qx),
                    Y = PadTo32(qy)
                }
            };

            using var ecdsa = ECDsa.Create(ecParams);

            // Convert r, s to IEEE P1363 format (r || s, each 32 bytes)
            var sig = new byte[64];
            var rBytes = PadTo32(r);
            var sBytes = PadTo32(s);
            rBytes.CopyTo(sig, 0);
            sBytes.CopyTo(sig, 32);

            bool valid = ecdsa.VerifyHash(hash, sig, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            if (valid)
            {
                var result = new byte[32];
                result[31] = 1;
                return (result, P256VerifyGas);
            }
            return (Array.Empty<byte>(), P256VerifyGas);
        }
        catch
        {
            // Invalid public key (not on curve, etc.) → return empty
            return (Array.Empty<byte>(), P256VerifyGas);
        }
    }

    private static byte[] PadTo32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 32) return bytes;
        var padded = new byte[32];
        Buffer.BlockCopy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }
}

/// <summary>
/// Cached BN254 (alt_bn128) curve parameters using BouncyCastle.
/// Field prime p = 21888242871839275222246405745257275088696311157297823662689037894645226208583
/// </summary>
internal static class Bn254CurveParams
{
    // BN254 field prime
    private static readonly Org.BouncyCastle.Math.BigInteger P =
        new("21888242871839275222246405745257275088696311157297823662689037894645226208583");

    // Curve order
    private static readonly Org.BouncyCastle.Math.BigInteger N =
        new("21888242871839275222246405745257275088548364400416034343698204186575808495617");

    // y^2 = x^3 + 3
    private static readonly Org.BouncyCastle.Math.BigInteger A = Org.BouncyCastle.Math.BigInteger.Zero;
    private static readonly Org.BouncyCastle.Math.BigInteger B = Org.BouncyCastle.Math.BigInteger.Three;

    // Generator (1, 2)
    private static readonly Org.BouncyCastle.Math.BigInteger Gx = Org.BouncyCastle.Math.BigInteger.One;
    private static readonly Org.BouncyCastle.Math.BigInteger Gy = Org.BouncyCastle.Math.BigInteger.Two;

    private static Org.BouncyCastle.Math.EC.FpCurve? _curve;

    public static Org.BouncyCastle.Math.EC.FpCurve Curve
    {
        get
        {
            if (_curve == null)
            {
                _curve = new Org.BouncyCastle.Math.EC.FpCurve(P, A, B, N, Org.BouncyCastle.Math.BigInteger.One);
            }
            return _curve;
        }
    }
}
