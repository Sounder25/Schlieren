using System;
using System.Numerics;
using Nethermind.Crypto;

namespace Scrutor.Core.Execution;

/// <summary>
/// EIP-2537 BLS12-381 precompiles (Prague fork, addresses 0x0b–0x11).
/// Uses the blst library via Nethermind.Crypto.Bls.
///
/// Address map:
///   0x0b = G1ADD     0x0c = G1MSM     0x0d = G2ADD
///   0x0e = G2MSM     0x0f = Pairing
///   0x10 = MapFpToG1 0x11 = MapFp2ToG2
/// </summary>
internal static class Bls12381Precompiles
{
    // ── Gas constants (from EELS GasCosts) ────────────────────────────────
    private const ulong G1AddGas    = 375;
    private const ulong G1MulGas    = 12_000;
    private const ulong G1MapGas    = 5_500;
    private const ulong G2AddGas    = 600;
    private const ulong G2MulGas    = 22_500;
    private const ulong G2MapGas    = 23_800;

    // Pairing: 32600*k + 37700
    private const ulong PairingBase = 37_700;
    private const ulong PairingPerPair = 32_600;

    // MSM discount tables (index = k-1, value = discount/1000)
    private static readonly int[] G1Discount = {
        1000, 949, 848, 797, 764, 750, 738, 728, 719, 712, 705, 698, 692, 687, 682, 677,
        673, 669, 665, 661, 658, 654, 651, 648, 645, 642, 640, 637, 635, 632, 630, 627,
        625, 623, 621, 619, 617, 615, 613, 611, 609, 608, 606, 604, 603, 601, 599, 598,
        596, 595, 593, 592, 591, 589, 588, 586, 585, 584, 582, 581, 580, 579, 577, 576,
        575, 574, 573, 572, 570, 569, 568, 567, 566, 565, 564, 563, 562, 561, 560, 559,
        558, 557, 556, 555, 554, 553, 552, 551, 550, 549, 548, 547, 547, 546, 545, 544,
        543, 542, 541, 540, 540, 539, 538, 537, 536, 536, 535, 534, 533, 532, 532, 531,
        530, 529, 528, 528, 527, 526, 525, 525, 524, 523, 522, 522, 521, 520, 520, 519
    };
    private static readonly int[] G2Discount = {
        1000, 1000, 923, 884, 855, 832, 812, 796, 782, 770, 759, 749, 740, 732, 724, 717,
        711, 704, 699, 693, 688, 683, 679, 674, 670, 666, 663, 659, 655, 652, 649, 646,
        643, 640, 637, 634, 632, 629, 627, 624, 622, 620, 618, 615, 613, 611, 609, 607,
        606, 604, 602, 600, 598, 597, 595, 593, 592, 590, 589, 587, 586, 584, 583, 582,
        580, 579, 578, 576, 575, 574, 573, 571, 570, 569, 568, 567, 566, 565, 563, 562,
        561, 560, 559, 558, 557, 556, 555, 554, 553, 552, 552, 551, 550, 549, 548, 547,
        546, 545, 545, 544, 543, 542, 541, 541, 540, 539, 538, 537, 537, 536, 535, 535,
        534, 533, 532, 532, 531, 530, 530, 529, 528, 528, 527, 526, 526, 525, 524, 524
    };
    private const int G1MaxDiscount = 519;
    private const int G2MaxDiscount = 524;
    private const int DiscountMultiplier = 1000;

    // ── Encoding sizes ─────────────────────────────────────────────────────
    private const int FpSize   = 64;   // EIP-2537: 48-byte field element padded to 64
    private const int Fp2Size  = 128;  // two Fp elements
    private const int G1Size   = 128;  // X(64) + Y(64)
    private const int G2Size   = 256;  // X(Fp2=128) + Y(Fp2=128)
    private const int BlstFpSize  = 48; // blst native field element
    private const int BlstG1Size  = 96; // blst uncompressed G1
    private const int BlstG2Size  = 192;// blst uncompressed G2

    // ── Public dispatch ────────────────────────────────────────────────────

    public static (byte[]? output, ulong gasUsed) Execute(byte id, byte[] input, ulong gasLimit) =>
        id switch
        {
            0x0b => G1Add(input, gasLimit),
            0x0c => G1Msm(input, gasLimit),
            0x0d => G2Add(input, gasLimit),
            0x0e => G2Msm(input, gasLimit),
            0x0f => Pairing(input, gasLimit),
            0x10 => MapFpToG1(input, gasLimit),
            0x11 => MapFp2ToG2(input, gasLimit),
            _    => (Array.Empty<byte>(), 0)
        };

    private static readonly byte[] ZeroG1 = new byte[BlstG1Size];
    private static readonly byte[] ZeroG2 = new byte[BlstG2Size];

    private static Bls.P1 G1Infinity() => new Bls.P1(ZeroG1.AsSpan());
    private static Bls.P2 G2Infinity() => new Bls.P2(ZeroG2.AsSpan());

    private static (byte[]? output, ulong gasUsed) G1Add(byte[] input, ulong gasLimit)
    {
        if (input.Length != G1Size * 2) return (null, gasLimit); // OOG on bad length
        if (gasLimit < G1AddGas) return (null, gasLimit);

        try
        {
            var p1 = DecodeG1(input, 0);
            var p2 = DecodeG1(input, G1Size);
            var result = p1.ToJacobian().Add(p2);
            return (EncodeG1(result.ToAffine()), G1AddGas);
        }
        catch { return (null, gasLimit); }
    }

    // ── G1MSM (0x0c) ───────────────────────────────────────────────────────

    private const int G1MsmPairSize = G1Size + 32; // G1 point (128) + scalar (32)

    private static (byte[]? output, ulong gasUsed) G1Msm(byte[] input, ulong gasLimit)
    {
        if (input.Length == 0 || input.Length % G1MsmPairSize != 0) return (null, gasLimit);
        int k = input.Length / G1MsmPairSize;
        ulong gas = G1MsmGas(k);
        if (gasLimit < gas) return (null, gasLimit);

        try
        {
            var acc = G1Infinity(); // point at infinity
            for (int i = 0; i < k; i++)
            {
                int off = i * G1MsmPairSize;
                var pt = DecodeG1(input, off);
                if (!pt.InGroup()) return (null, gasLimit);
                var scalar = input.AsSpan(off + G1Size, 32);
                // Skip zero scalar
                bool allZero = true;
                foreach (var b in scalar) if (b != 0) { allZero = false; break; }
                if (allZero) continue;
                var ptJ = pt.ToJacobian().Mult(scalar);
                acc = acc.Add(ptJ);
            }
            return (EncodeG1(acc.ToAffine()), gas);
        }
        catch { return (null, gasLimit); }
    }

    private static ulong G1MsmGas(int k)
    {
        int discountIdx = Math.Min(k, G1Discount.Length) - 1;
        int discount = discountIdx >= 0 ? G1Discount[discountIdx] : G1MaxDiscount;
        return (ulong)k * G1MulGas * (ulong)discount / (ulong)DiscountMultiplier;
    }

    // ── G2ADD (0x0d) ───────────────────────────────────────────────────────

    private static (byte[]? output, ulong gasUsed) G2Add(byte[] input, ulong gasLimit)
    {
        if (input.Length != G2Size * 2) return (null, gasLimit);
        if (gasLimit < G2AddGas) return (null, gasLimit);

        try
        {
            var p1 = DecodeG2(input, 0);
            var p2 = DecodeG2(input, G2Size);
            var result = p1.ToJacobian().Add(p2);
            return (EncodeG2(result.ToAffine()), G2AddGas);
        }
        catch { return (null, gasLimit); }
    }

    // ── G2MSM (0x0e) ───────────────────────────────────────────────────────

    private const int G2MsmPairSize = G2Size + 32; // G2 point (256) + scalar (32)

    private static (byte[]? output, ulong gasUsed) G2Msm(byte[] input, ulong gasLimit)
    {
        if (input.Length == 0 || input.Length % G2MsmPairSize != 0) return (null, gasLimit);
        int k = input.Length / G2MsmPairSize;
        ulong gas = G2MsmGas(k);
        if (gasLimit < gas) return (null, gasLimit);

        try
        {
            var acc = G2Infinity(); // point at infinity
            for (int i = 0; i < k; i++)
            {
                int off = i * G2MsmPairSize;
                var pt = DecodeG2(input, off);
                if (!pt.InGroup()) return (null, gasLimit);
                var scalar = input.AsSpan(off + G2Size, 32);
                bool allZero = true;
                foreach (var b in scalar) if (b != 0) { allZero = false; break; }
                if (allZero) continue;
                var ptJ = pt.ToJacobian().Mult(scalar);
                acc = acc.Add(ptJ);
            }
            return (EncodeG2(acc.ToAffine()), gas);
        }
        catch { return (null, gasLimit); }
    }

    private static ulong G2MsmGas(int k)
    {
        int discountIdx = Math.Min(k, G2Discount.Length) - 1;
        int discount = discountIdx >= 0 ? G2Discount[discountIdx] : G2MaxDiscount;
        return (ulong)k * G2MulGas * (ulong)discount / (ulong)DiscountMultiplier;
    }

    // ── Pairing (0x0f) ─────────────────────────────────────────────────────

    private const int PairingPairSize = G1Size + G2Size; // 128 + 256 = 384

    private static (byte[]? output, ulong gasUsed) Pairing(byte[] input, ulong gasLimit)
    {
        if (input.Length == 0 || input.Length % PairingPairSize != 0) return (null, gasLimit);
        int k = input.Length / PairingPairSize;
        ulong gas = PairingBase + PairingPerPair * (ulong)k;
        if (gasLimit < gas) return (null, gasLimit);

        try
        {
            if (k == 0)
            {
                var one = new byte[32];
                one[31] = 1;
                return (one, gas);
            }

            // Compute product of pairings using PT.One() as identity
            var acc = Bls.PT.One();
            bool hasAny = false;
            for (int i = 0; i < k; i++)
            {
                int off = i * PairingPairSize;
                var g1 = DecodeG1(input, off);
                if (!g1.InGroup()) return (null, gasLimit);
                var g2 = DecodeG2(input, off + G1Size);
                if (!g2.InGroup()) return (null, gasLimit);

                // Pairing of infinity contributes identity; skip
                if (g1.IsInf() || g2.IsInf()) continue;

                var pt = new Bls.PT(g1, g2);
                acc = hasAny ? acc.Mul(pt) : pt;
                hasAny = true;
            }

            bool isOne = !hasAny || acc.FinalExp().IsOne();
            var output = new byte[32];
            if (isOne) output[31] = 1;
            return (output, gas);
        }
        catch { return (null, gasLimit); }
    }

    // ── MapFpToG1 (0x10) ───────────────────────────────────────────────────

    private static (byte[]? output, ulong gasUsed) MapFpToG1(byte[] input, ulong gasLimit)
    {
        if (input.Length != FpSize) return (null, gasLimit);
        if (gasLimit < G1MapGas) return (null, gasLimit);

        try
        {
            // Validate and extract 48-byte field element
            if (!IsZeroPadded(input, 0, 16)) return (null, gasLimit);
            CheckFieldElement(input, 0); // throws on invalid
            var fp = input.AsSpan(16, BlstFpSize);
            var p1 = Bls.P1.Generator().MapTo(fp);
            return (EncodeG1(p1.ToAffine()), G1MapGas);
        }
        catch { return (null, gasLimit); }
    }

    // ── MapFp2ToG2 (0x11) ──────────────────────────────────────────────────

    private static (byte[]? output, ulong gasUsed) MapFp2ToG2(byte[] input, ulong gasLimit)
    {
        if (input.Length != Fp2Size) return (null, gasLimit);
        if (gasLimit < G2MapGas) return (null, gasLimit);

        try
        {
            if (!IsZeroPadded(input, 0, 16) || !IsZeroPadded(input, 64, 16)) return (null, gasLimit);
            CheckFieldElement(input, 0);
            CheckFieldElement(input, 64);
            var c0 = input.AsSpan(16, BlstFpSize);
            var c1 = input.AsSpan(80, BlstFpSize);
            var p2 = Bls.P2.Generator().MapTo(c0, c1);
            return (EncodeG2(p2.ToAffine()), G2MapGas);
        }
        catch { return (null, gasLimit); }
    }

    // ── Encoding helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Decode a 128-byte EIP-2537 G1 point to a blst P1Affine.
    /// Format: X(64 bytes) + Y(64 bytes), each = 16 zero bytes + 48 value bytes.
    /// blst native: 96-byte uncompressed X(48) + Y(48).
    /// Throws on invalid padding, out-of-field, or off-curve.
    /// </summary>
    private static Bls.P1Affine DecodeG1(byte[] input, int offset)
    {
        if (!IsZeroPadded(input, offset, 16) || !IsZeroPadded(input, offset + FpSize, 16))
            throw new InvalidOperationException("G1: non-zero padding");
        CheckFieldElement(input, offset);
        CheckFieldElement(input, offset + FpSize);

        // Check for point at infinity: all zeros
        bool allZero = true;
        for (int i = offset; i < offset + G1Size && allZero; i++)
            if (input[i] != 0) allZero = false;
        if (allZero) return G1Infinity().ToAffine();

        var raw = new byte[BlstG1Size];
        input.AsSpan(offset + 16, BlstFpSize).CopyTo(raw);          // X
        input.AsSpan(offset + FpSize + 16, BlstFpSize).CopyTo(raw.AsSpan(BlstFpSize)); // Y
        var p = new Bls.P1Affine(raw.AsSpan());
        if (!p.OnCurve()) throw new InvalidOperationException("G1 not on curve");
        return p;
    }

    /// <summary>
    /// Encode a blst P1Affine to 128-byte EIP-2537 format.
    /// </summary>
    private static byte[] EncodeG1(Bls.P1Affine p)
    {
        if (p.IsInf()) return new byte[G1Size];

        var raw = p.Serialize(); // 96 bytes
        var result = new byte[G1Size];
        raw.AsSpan(0, BlstFpSize).CopyTo(result.AsSpan(16));           // X
        raw.AsSpan(BlstFpSize, BlstFpSize).CopyTo(result.AsSpan(FpSize + 16)); // Y
        return result;
    }

    /// <summary>
    /// Decode a 256-byte EIP-2537 G2 point to a blst P2Affine.
    /// Format: X.c0(64) + X.c1(64) + Y.c0(64) + Y.c1(64).
    /// blst native: 192-byte uncompressed = X.c1(48) + X.c0(48) + Y.c1(48) + Y.c0(48).
    /// </summary>
    private static Bls.P2Affine DecodeG2(byte[] input, int offset)
    {
        for (int i = 0; i < 4; i++)
        {
            if (!IsZeroPadded(input, offset + i * FpSize, 16))
                throw new InvalidOperationException("G2: non-zero padding");
            CheckFieldElement(input, offset + i * FpSize);
        }

        // Check infinity
        bool allZero = true;
        for (int i = offset; i < offset + G2Size && allZero; i++)
            if (input[i] != 0) allZero = false;
        if (allZero) return G2Infinity().ToAffine();

        // EIP-2537: X.c0, X.c1, Y.c0, Y.c1 (each 48 bytes value, 16-byte pad)
        // blst:     X.c1, X.c0, Y.c1, Y.c0
        var raw = new byte[BlstG2Size];
        input.AsSpan(offset + FpSize  + 16, BlstFpSize).CopyTo(raw.AsSpan(0));           // X.c1
        input.AsSpan(offset + 16,           BlstFpSize).CopyTo(raw.AsSpan(BlstFpSize));   // X.c0
        input.AsSpan(offset + 3*FpSize+ 16, BlstFpSize).CopyTo(raw.AsSpan(2*BlstFpSize));// Y.c1
        input.AsSpan(offset + 2*FpSize+ 16, BlstFpSize).CopyTo(raw.AsSpan(3*BlstFpSize));// Y.c0
        var p = new Bls.P2Affine(raw.AsSpan());
        if (!p.OnCurve()) throw new InvalidOperationException("G2 not on curve");
        return p;
    }

    /// <summary>
    /// Encode a blst P2Affine to 256-byte EIP-2537 format.
    /// </summary>
    private static byte[] EncodeG2(Bls.P2Affine p)
    {
        if (p.IsInf()) return new byte[G2Size];

        var raw = p.Serialize(); // 192 bytes: X.c1, X.c0, Y.c1, Y.c0
        var result = new byte[G2Size];
        raw.AsSpan(BlstFpSize,    BlstFpSize).CopyTo(result.AsSpan(16));              // X.c0
        raw.AsSpan(0,             BlstFpSize).CopyTo(result.AsSpan(FpSize + 16));     // X.c1
        raw.AsSpan(3*BlstFpSize,  BlstFpSize).CopyTo(result.AsSpan(2*FpSize + 16));  // Y.c0
        raw.AsSpan(2*BlstFpSize,  BlstFpSize).CopyTo(result.AsSpan(3*FpSize + 16));  // Y.c1
        return result;
    }

    // BLS12-381 field modulus
    private static readonly byte[] BlsFieldModulus = Convert.FromHexString(
        "1a0111ea397fe69a4b1ba7b6434bacd764774b84f38512bf6730d2a0f6b0f6241eabfffeb153ffffb9feffffffffaaab");

    private static void CheckFieldElement(byte[] input, int offset)
    {
        // Compare 48-byte big-endian value against field modulus
        // Element is bytes [offset+16 .. offset+63]
        for (int i = 0; i < BlstFpSize; i++)
        {
            byte v = input[offset + 16 + i];
            byte m = BlsFieldModulus[i];
            if (v < m) return;   // definitely in range
            if (v > m) throw new InvalidOperationException("Field element >= modulus");
        }
        throw new InvalidOperationException("Field element == modulus");
    }

    private static bool IsZeroPadded(byte[] input, int offset, int length)
    {
        if (offset + length > input.Length) return false;
        for (int i = offset; i < offset + length; i++)
            if (input[i] != 0) return false;
        return true;
    }
}
