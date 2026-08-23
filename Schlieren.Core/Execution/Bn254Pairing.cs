using System.Numerics;

namespace Schlieren.Core.Execution;

/// <summary>
/// BN254 (alt_bn128) Ate pairing check — EIP-197 precompile 0x08.
///
/// Semantics (matching geth runBn256Pairing / py_ecc optimized_bn128):
///   • Input length not a multiple of 192 → invalid (revert, return null).
///   • Each 192-byte chunk: G1 (64 bytes) then G2 (128 bytes).
///     – G1: (x, y) as 32-byte BE Fp elements. (0,0) = point at infinity.
///       Revert if either coordinate ≥ field modulus or point not on curve.
///     – G2: bytes [0..31]=x.c1, [32..63]=x.c0, [64..95]=y.c1, [96..127]=y.c0 (Fp2 big-endian).
///       Revert if any coordinate ≥ field modulus or point not on twisted curve.
///       G2 must also have order q (EIP-197 §Specification; EELS
///       alt_bn128_pairing_check multiplies by curve_order). geth's
///       bn256 Unmarshal only checks the twist equation — do not match that.
///   • Empty input (k=0) → success, output = 32-byte 1.
///   • All pairs valid → Ate pairing product; return 1 if = 1 in GT else 0.
///   • Any decode failure → return null (revert).
/// </summary>
internal static class Bn254Pairing
{
    // ── BN254 constants ────────────────────────────────────────────────────
    private static readonly BigInteger P =
        BigInteger.Parse("21888242871839275222246405745257275088696311157297823662689037894645226208583");

    private static readonly BigInteger R =
        BigInteger.Parse("21888242871839275222246405745257275088548364400416034343698204186575808495617");

    // Twisted curve coefficient b2 = 3 / (9+i) in Fp2
    // b2.c0 = 19485874751759354771024239261021720505790618469301721065564631296452457478373
    // b2.c1 = 266929791119991161246907387137283842545076965332900288569378510910307636690
    private static readonly BigInteger B2c0 =
        BigInteger.Parse("19485874751759354771024239261021720505790618469301721065564631296452457478373");
    private static readonly BigInteger B2c1 =
        BigInteger.Parse("266929791119991161246907387137283842545076965332900288569378510910307636690");

    // Pseudo-binary NAF encoding of ate loop count 29793968203157093288 (64 bits, index 0 = LSB)
    private static readonly int[] Naf =
    {
         0, 0, 0, 1, 0, 1, 0,-1, 0, 0, 1,-1, 0, 0, 1, 0,
         0, 1, 1, 0,-1, 0, 0, 1, 0,-1, 0, 0, 0, 0, 1, 1,
         1, 0, 0,-1, 0, 0, 1, 0, 0, 0, 0, 0,-1, 0, 0, 1,
         1, 0, 0,-1, 0, 0, 0, 1, 1, 0,-1, 0, 0, 1, 0, 1, 1
    };

    // ── Entry point ────────────────────────────────────────────────────────

    /// <summary>
    /// Run the EIP-197 pairing check.
    /// Returns null on any input validation failure (triggers revert in the VM).
    /// Returns 32-byte 0 or 1 on success.
    /// </summary>
    public static byte[]? Run(byte[] input)
    {
        if (input.Length % 192 != 0) return null;
        int k = input.Length / 192;
        if (k == 0) return Out(true);

        // Accumulate GT product across all pairs
        Fp12 acc = Fp12.One;

        for (int i = 0; i < k; i++)
        {
            int off = i * 192;

            if (!DecodeG1(input, off, out var g1)) return null;
            if (!DecodeG2(input, off + 64, out var g2)) return null;

            // Infinity in either → contributes 1 to product
            if (g1.IsInfinity || g2.IsInfinity) continue;

            acc = Fp12.Mul(acc, MillerLoop(g2, g1));
        }

        return Out(FinalExponentiate(acc).IsOne);
    }

    // ── G1 (over Fp) ──────────────────────────────────────────────────────

    private readonly struct G1(BigInteger x, BigInteger y, bool inf)
    {
        public readonly BigInteger X = x, Y = y;
        public readonly bool IsInfinity = inf;
    }

    private static bool DecodeG1(byte[] data, int off, out G1 pt)
    {
        pt = default;
        var x = ReadFp(data, off);
        var y = ReadFp(data, off + 32);
        if (x < 0 || y < 0) return false;               // ≥ field modulus

        if (x.IsZero && y.IsZero) { pt = new G1(0, 0, true); return true; }

        // y² ≡ x³ + 3 (mod p)
        if (FpMul(FpMul(x, x), x) + 3 != FpMul(y, y) % P &&
            FpAdd(FpMul(FpMul(x, x), x), 3) != FpMul(y, y))
        {
            // Use clean comparison
            if (!OnCurveG1(x, y)) return false;
        }
        pt = new G1(x, y, false);
        return true;
    }

    private static bool OnCurveG1(BigInteger x, BigInteger y)
        => FpMul(y, y) == FpAdd(FpMul(FpMul(x, x), x), 3);

    // ── G2 (over Fp2) ─────────────────────────────────────────────────────

    private readonly struct G2(Fp2 x, Fp2 y, bool inf)
    {
        public readonly Fp2 X = x, Y = y;
        public readonly bool IsInfinity = inf;
    }

    private static bool DecodeG2(byte[] data, int off, out G2 pt)
    {
        pt = default;
        // EIP-197 / geth encoding: bytes [0..31]=x.c1, [32..63]=x.c0, [64..95]=y.c1, [96..127]=y.c0
        var xc1 = ReadFp(data, off);
        var xc0 = ReadFp(data, off + 32);
        var yc1 = ReadFp(data, off + 64);
        var yc0 = ReadFp(data, off + 96);
        if (xc1 < 0 || xc0 < 0 || yc1 < 0 || yc0 < 0) return false;

        if (xc0.IsZero && xc1.IsZero && yc0.IsZero && yc1.IsZero)
        {
            pt = new G2(Fp2.Zero, Fp2.Zero, true);
            return true;
        }

        var x = new Fp2(xc0, xc1);
        var y = new Fp2(yc0, yc1);

        // y² ≡ x³ + b2  (in Fp2)
        var lhs = Fp2.Mul(y, y);
        var rhs = Fp2.Add(Fp2.Mul(Fp2.Mul(x, x), x), new Fp2(B2c0, B2c1));
        if (!lhs.Equals(rhs)) return false;

        pt = new G2(x, y, false);

        // EIP-197 / EELS: G2 must lie in the r-order subgroup (r·P = ∞).
        if (!IsInG2Subgroup(pt)) return false;

        return true;
    }

    // ── Fp helpers ────────────────────────────────────────────────────────

    /// <summary>Read 32-byte BE Fp element; returns -1 if ≥ p (sentinel for error).</summary>
    private static BigInteger ReadFp(byte[] data, int off)
    {
        var buf = new byte[32];
        int avail = Math.Max(0, Math.Min(32, data.Length - off));
        if (avail > 0) Array.Copy(data, off, buf, 0, avail);
        var v = new BigInteger(buf, isUnsigned: true, isBigEndian: true);
        return v >= P ? BigInteger.MinusOne : v;
    }

    private static BigInteger FpAdd(BigInteger a, BigInteger b) => (a + b) % P;
    private static BigInteger FpSub(BigInteger a, BigInteger b) => ((a - b) % P + P) % P;
    private static BigInteger FpMul(BigInteger a, BigInteger b) => a * b % P;
    private static BigInteger FpNeg(BigInteger a) => a.IsZero ? BigInteger.Zero : P - a;
    private static BigInteger FpInv(BigInteger a) => BigInteger.ModPow(a, P - 2, P);

    // ── Fp2 ───────────────────────────────────────────────────────────────

    // Fp2 = Fp[u]/(u²+1):  element = c0 + c1·u
    private readonly struct Fp2(BigInteger c0, BigInteger c1)
    {
        public readonly BigInteger C0 = c0, C1 = c1;

        public static readonly Fp2 Zero = new(0, 0);
        public static readonly Fp2 One  = new(1, 0);

        public bool IsZero => C0.IsZero && C1.IsZero;

        public bool Equals(Fp2 o) => C0 == o.C0 && C1 == o.C1;

        public static Fp2 Add(Fp2 a, Fp2 b) => new(FpAdd(a.C0, b.C0), FpAdd(a.C1, b.C1));
        public static Fp2 Sub(Fp2 a, Fp2 b) => new(FpSub(a.C0, b.C0), FpSub(a.C1, b.C1));
        public static Fp2 Neg(Fp2 a)        => new(FpNeg(a.C0), FpNeg(a.C1));

        public static Fp2 Mul(Fp2 a, Fp2 b)
        {
            // (a0+a1·u)(b0+b1·u) = (a0b0 - a1b1) + (a0b1 + a1b0)·u
            var t1 = FpMul(a.C0, b.C0);
            var t2 = FpMul(a.C1, b.C1);
            return new(FpSub(t1, t2), FpAdd(FpMul(a.C0, b.C1), FpMul(a.C1, b.C0)));
        }

        public static Fp2 MulScalar(Fp2 a, BigInteger s) => new(FpMul(a.C0, s), FpMul(a.C1, s));

        public static Fp2 Inv(Fp2 a)
        {
            // 1/(a0+a1u) = (a0 - a1u)/(a0²+a1²)
            var d = FpInv(FpAdd(FpMul(a.C0, a.C0), FpMul(a.C1, a.C1)));
            return new(FpMul(a.C0, d), FpMul(FpNeg(a.C1), d));
        }

        // Frobenius: (a0+a1u)^p = a0 - a1u  (since u^2=-1, a0,a1 ∈ Fp)
        public static Fp2 FrobP(Fp2 a) => new(a.C0, FpNeg(a.C1));

        public static Fp2 Pow(Fp2 a, BigInteger e)
        {
            var r = One; var b = a;
            while (e > 0) { if (!e.IsEven) r = Mul(r, b); b = Mul(b, b); e >>= 1; }
            return r;
        }
    }

    // ── Fp12 = Fp2[w]/(w⁶ − ξ) where ξ = 9+u ─────────────────────────────
    // Element: c[0] + c[1]·w + c[2]·w² + c[3]·w³ + c[4]·w⁴ + c[5]·w⁵
    // w⁶ = ξ = (9,1) in Fp2

    private readonly struct Fp12
    {
        public readonly Fp2[] C; // length 6

        public Fp12(Fp2 c0, Fp2 c1, Fp2 c2, Fp2 c3, Fp2 c4, Fp2 c5)
            => C = [c0, c1, c2, c3, c4, c5];

        private Fp12(Fp2[] c) => C = c;

        public static readonly Fp12 One = new(Fp2.One, Fp2.Zero, Fp2.Zero, Fp2.Zero, Fp2.Zero, Fp2.Zero);

        private static readonly Fp2 Xi = new(9, 1); // w⁶ = ξ

        public bool IsOne
        {
            get
            {
                if (!C[0].Equals(Fp2.One)) return false;
                for (int i = 1; i < 6; i++) if (!C[i].IsZero) return false;
                return true;
            }
        }

        public static Fp12 Add(Fp12 a, Fp12 b)
        {
            var c = new Fp2[6];
            for (int i = 0; i < 6; i++) c[i] = Fp2.Add(a.C[i], b.C[i]);
            return new(c);
        }

        public static Fp12 Neg(Fp12 a)
        {
            var c = new Fp2[6];
            for (int i = 0; i < 6; i++) c[i] = Fp2.Neg(a.C[i]);
            return new(c);
        }

        // Schoolbook multiplication mod w⁶ = ξ
        public static Fp12 Mul(Fp12 a, Fp12 b)
        {
            var t = new Fp2[11];
            for (int i = 0; i < 11; i++) t[i] = Fp2.Zero;

            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    t[i + j] = Fp2.Add(t[i + j], Fp2.Mul(a.C[i], b.C[j]));

            // Reduce: t[k] for k≥6 contributes ξ·t[k] to t[k-6]
            for (int k = 10; k >= 6; k--)
            {
                t[k - 6] = Fp2.Add(t[k - 6], Fp2.Mul(t[k], Xi));
                t[k] = Fp2.Zero;
            }

            return new(t[0], t[1], t[2], t[3], t[4], t[5]);
        }

        public static Fp12 Pow(Fp12 a, BigInteger e)
        {
            if (e.IsZero) return One;
            var r = One; var b = a;
            while (e > 0) { if (!e.IsEven) r = Mul(r, b); b = Mul(b, b); e >>= 1; }
            return r;
        }

        // Scalar multiplication: multiply all coefficients by a field scalar
        public static Fp12 MulFp2(Fp12 a, Fp2 s)
        {
            var c = new Fp2[6];
            for (int i = 0; i < 6; i++) c[i] = Fp2.Mul(a.C[i], s);
            return new(c);
        }

        // Inverse via Fermat: a^(p¹²-2) — used only in Miller loop cleanup
        public static Fp12 Inv(Fp12 a) => Pow(a, BigInteger.Pow(P, 12) - 2);

        public static Fp12 FromFp2(Fp2 v)
        {
            var c = new Fp2[6];
            for (int i = 0; i < 6; i++) c[i] = Fp2.Zero;
            c[0] = v;
            return new(c);
        }

        public static Fp12 FromFp(BigInteger v) => FromFp2(new Fp2(v, 0));

        // Frobenius on Fp12: apply the map coefficient-wise.
        // For power k=1: c[i] -> FrobP(c[i]) * gamma1[i]
        // gamma1[i] = ξ^(i*(p-1)/6) in Fp2
        public static Fp12 Frob(Fp12 a, int k)
        {
            var coeffs = k == 1 ? _frob1 : k == 2 ? _frob2 : _frob3;
            var c = new Fp2[6];
            for (int i = 0; i < 6; i++)
            {
                var ci = a.C[i];
                for (int j = 0; j < k; j++) ci = Fp2.FrobP(ci);
                c[i] = Fp2.Mul(ci, coeffs[i]);
            }
            return new(c);
        }

        // Precomputed Frobenius coefficients γ[i] = ξ^(i*(p-1)/6) for i=0..5
        private static readonly Fp2[] _frob1 = PrecomputeFrob(1);
        private static readonly Fp2[] _frob2 = PrecomputeFrob(2);
        private static readonly Fp2[] _frob3 = PrecomputeFrob(3);

        private static Fp2[] PrecomputeFrob(int k)
        {
            // exp_i = i * k * (P-1)/6
            var c = new Fp2[6];
            var xi = new Fp2(9, 1);
            var pMinus1Over6 = (P - 1) / 6;
            c[0] = Fp2.One;
            for (int i = 1; i < 6; i++)
                c[i] = Fp2.Pow(xi, (BigInteger)(i * k) * pMinus1Over6);
            return c;
        }

        public bool IsZero
        {
            get { for (int i = 0; i < 6; i++) if (!C[i].IsZero) return false; return true; }
        }
    }

    // ── Projective G2 for Miller loop ─────────────────────────────────────
    // Homogeneous projective (X:Y:Z) over Fp2.  Affine = (X/Z, Y/Z).

    private readonly struct PG2(Fp2 x, Fp2 y, Fp2 z)
    {
        public readonly Fp2 X = x, Y = y, Z = z;
        public bool IsInfinity => Z.IsZero;

        public static PG2 From(G2 p) => new(p.X, p.Y, Fp2.One);
        public static readonly PG2 Infinity = new(Fp2.One, Fp2.One, Fp2.Zero);
    }

    private static PG2 Dbl(PG2 p)
    {
        var W = Fp2.MulScalar(Fp2.Mul(p.X, p.X), 3);
        var S = Fp2.Mul(p.Y, p.Z);
        var B = Fp2.Mul(Fp2.Mul(p.X, p.Y), S);
        var H = Fp2.Sub(Fp2.Mul(W, W), Fp2.MulScalar(B, 8));
        var S2 = Fp2.Mul(S, S);
        return new PG2(
            Fp2.MulScalar(Fp2.Mul(H, S), 2),
            Fp2.Sub(Fp2.Mul(W, Fp2.Sub(Fp2.MulScalar(B, 4), H)),
                    Fp2.MulScalar(Fp2.Mul(Fp2.Mul(p.Y, p.Y), S2), 8)),
            Fp2.MulScalar(Fp2.Mul(S, S2), 8));
    }

    private static PG2 AddG2(PG2 a, PG2 b)
    {
        if (a.IsInfinity) return b;
        if (b.IsInfinity) return a;
        var U1 = Fp2.Mul(b.Y, a.Z); var U2 = Fp2.Mul(a.Y, b.Z);
        var V1 = Fp2.Mul(b.X, a.Z); var V2 = Fp2.Mul(a.X, b.Z);
        if (V1.Equals(V2))
            return U1.Equals(U2) ? Dbl(a) : PG2.Infinity;
        var U = Fp2.Sub(U1, U2); var V = Fp2.Sub(V1, V2);
        var Vsq = Fp2.Mul(V, V); var VsqV2 = Fp2.Mul(Vsq, V2); var Vc = Fp2.Mul(V, Vsq);
        var W = Fp2.Mul(a.Z, b.Z);
        var A = Fp2.Sub(Fp2.Sub(Fp2.Mul(Fp2.Mul(U, U), W), Vc), Fp2.MulScalar(VsqV2, 2));
        return new PG2(
            Fp2.Mul(V, A),
            Fp2.Sub(Fp2.Mul(U, Fp2.Sub(VsqV2, A)), Fp2.Mul(Vc, U2)),
            Fp2.Mul(Vc, W));
    }

    private static PG2 NegG2(PG2 p) => new(p.X, Fp2.Neg(p.Y), p.Z);

    /// <summary>
    /// G2 subgroup check: r * P must equal the point at infinity.
    /// Uses double-and-add scalar multiplication on G2 with the curve order r.
    /// </summary>
    private static bool IsInG2Subgroup(G2 pt)
    {
        // r = 21888242871839275222246405745257275088548364400416034343698204186575808495617
        var result = ScalarMulG2(PG2.From(pt), R);
        return result.IsInfinity;
    }

    private static PG2 ScalarMulG2(PG2 p, BigInteger scalar)
    {
        var result = PG2.Infinity;
        var addend = p;
        while (scalar > BigInteger.Zero)
        {
            if (!scalar.IsEven)
                result = AddG2(result, addend);
            addend = Dbl(addend);
            scalar >>= 1;
        }
        return result;
    }

    // ── Twist: lift G2 from Fp2 to Fp12 ──────────────────────────────────
    // In the Fp12 tower w⁶=ξ:  x' = x·w², y' = y·w³
    // Slot 2 (index 2) carries x; slot 3 (index 3) carries y.
    private static (Fp12 x, Fp12 y, Fp12 z) Twist(PG2 q)
    {
        Fp12 MakeSlot(Fp2 v, int slot)
        {
            var c = new Fp2[6];
            for (int i = 0; i < 6; i++) c[i] = Fp2.Zero;
            c[slot] = v;
            return new Fp12(c[0], c[1], c[2], c[3], c[4], c[5]);
        }
        return (MakeSlot(q.X, 2), MakeSlot(q.Y, 3), Fp12.FromFp2(q.Z));
    }

    // ── Fp12-projective helpers for Miller loop ────────────────────────────

    private static (Fp12 x, Fp12 y, Fp12 z) DblFp12(Fp12 x, Fp12 y, Fp12 z)
    {
        var W  = Fp12.Mul(Fp12.Mul(x, x), Fp12.FromFp(3));
        var S  = Fp12.Mul(y, z);
        var B  = Fp12.Mul(Fp12.Mul(x, y), S);
        var H  = Fp12.Add(Fp12.Mul(W, W), Fp12.Neg(Fp12.Mul(B, Fp12.FromFp(8))));
        var S2 = Fp12.Mul(S, S);
        return (
            Fp12.Mul(Fp12.Mul(H, S), Fp12.FromFp(2)),
            Fp12.Add(Fp12.Mul(W, Fp12.Add(Fp12.Mul(B, Fp12.FromFp(4)), Fp12.Neg(H))),
                     Fp12.Neg(Fp12.Mul(Fp12.Mul(Fp12.Mul(y, y), S2), Fp12.FromFp(8)))),
            Fp12.Mul(Fp12.Mul(S, S2), Fp12.FromFp(8)));
    }

    private static (Fp12 x, Fp12 y, Fp12 z) AddFp12(
        Fp12 ax, Fp12 ay, Fp12 az,
        Fp12 bx, Fp12 by, Fp12 bz)
    {
        var U1 = Fp12.Mul(by, az); var U2 = Fp12.Mul(ay, bz);
        var V1 = Fp12.Mul(bx, az); var V2 = Fp12.Mul(ax, bz);
        var U  = Fp12.Add(U1, Fp12.Neg(U2));
        var V  = Fp12.Add(V1, Fp12.Neg(V2));
        var Vsq = Fp12.Mul(V, V); var VsqV2 = Fp12.Mul(Vsq, V2); var Vc = Fp12.Mul(V, Vsq);
        var W   = Fp12.Mul(az, bz);
        var A   = Fp12.Add(Fp12.Add(Fp12.Mul(Fp12.Mul(U, U), W), Fp12.Neg(Vc)),
                           Fp12.Neg(Fp12.Mul(VsqV2, Fp12.FromFp(2))));
        return (
            Fp12.Mul(V, A),
            Fp12.Add(Fp12.Mul(U, Fp12.Add(VsqV2, Fp12.Neg(A))), Fp12.Neg(Fp12.Mul(Vc, U2))),
            Fp12.Mul(Vc, W));
    }

    // ── Line function in Fp12 ─────────────────────────────────────────────
    private static (Fp12 num, Fp12 den) LineFp12(
        Fp12 x1, Fp12 y1, Fp12 z1,
        Fp12 x2, Fp12 y2, Fp12 z2,
        Fp12 xt, Fp12 yt, Fp12 zt)
    {
        var mN = Fp12.Add(Fp12.Mul(y2, z1), Fp12.Neg(Fp12.Mul(y1, z2)));
        var mD = Fp12.Add(Fp12.Mul(x2, z1), Fp12.Neg(Fp12.Mul(x1, z2)));


        // xt*z1 - x1*zt  and  yt*z1 - y1*zt
        var dx = Fp12.Add(Fp12.Mul(xt, z1), Fp12.Neg(Fp12.Mul(x1, zt)));
        var dy = Fp12.Add(Fp12.Mul(yt, z1), Fp12.Neg(Fp12.Mul(y1, zt)));

        if (!mD.IsZero)
        {
            return (Fp12.Add(Fp12.Mul(mN, dx), Fp12.Neg(Fp12.Mul(mD, dy))),
                    Fp12.Mul(Fp12.Mul(mD, zt), z1));
        }
        else if (!mN.IsZero)
        {
            // Vertical: return (xt*z1 - x1*zt, z1*zt)
            return (dx, Fp12.Mul(z1, zt));
        }
        else
        {
            // Doubling: m = 3x²/(2yz)
            var mN2 = Fp12.Mul(Fp12.Mul(x1, x1), Fp12.FromFp(3));
            var mD2 = Fp12.Mul(Fp12.Mul(y1, Fp12.FromFp(2)), z1);
            return (Fp12.Add(Fp12.Mul(mN2, dx), Fp12.Neg(Fp12.Mul(mD2, dy))),
                    Fp12.Mul(Fp12.Mul(mD2, zt), z1));
        }
    }

    // ── Miller loop ────────────────────────────────────────────────────────
    private static Fp12 MillerLoop(G2 q, G1 p)
    {
        // Lift Q via twist to Fp12 projective
        var (qx, qy, qz) = Twist(PG2.From(q));
        // Lift P to Fp12 (embed Fp into Fp12 via slot 0)
        var (px, py, pz) = (Fp12.FromFp(p.X), Fp12.FromFp(p.Y), Fp12.FromFp(1));

        var (rx, ry, rz) = (qx, qy, qz);
        var fN = Fp12.One; var fD = Fp12.One;

        for (int i = 63; i >= 0; i--)
        {
            var (_n, _d) = LineFp12(rx, ry, rz, rx, ry, rz, px, py, pz);
            fN = Fp12.Mul(Fp12.Mul(fN, fN), _n);
            fD = Fp12.Mul(Fp12.Mul(fD, fD), _d);
            (rx, ry, rz) = DblFp12(rx, ry, rz);

            int v = Naf[i];
            if (v == 1)
            {
                var (_n2, _d2) = LineFp12(rx, ry, rz, qx, qy, qz, px, py, pz);
                fN = Fp12.Mul(fN, _n2); fD = Fp12.Mul(fD, _d2);
                (rx, ry, rz) = AddFp12(rx, ry, rz, qx, qy, qz);
            }
            else if (v == -1)
            {
                var nqy = Fp12.Neg(qy);
                var (_n2, _d2) = LineFp12(rx, ry, rz, qx, nqy, qz, px, py, pz);
                fN = Fp12.Mul(fN, _n2); fD = Fp12.Mul(fD, _d2);
                (rx, ry, rz) = AddFp12(rx, ry, rz, qx, nqy, qz);
            }
        }

        // Frobenius cleanup: Q1 = Q^p, nQ2 = -(Q1^p)
        var q1x = Fp12.Frob(qx, 1); var q1y = Fp12.Frob(qy, 1); var q1z = Fp12.Frob(qz, 1);
        var (_n1, _d1) = LineFp12(rx, ry, rz, q1x, q1y, q1z, px, py, pz);
        (rx, ry, rz) = AddFp12(rx, ry, rz, q1x, q1y, q1z);

        var nq2x = Fp12.Frob(q1x, 1); var nq2y = Fp12.Neg(Fp12.Frob(q1y, 1)); var nq2z = Fp12.Frob(q1z, 1);
        var (_n2f, _d2f) = LineFp12(rx, ry, rz, nq2x, nq2y, nq2z, px, py, pz);

        var num = Fp12.Mul(Fp12.Mul(fN, _n1), _n2f);
        var den = Fp12.Mul(Fp12.Mul(fD, _d1), _d2f);
        return Fp12.Mul(num, Fp12.Inv(den));
    }

    // ── Final exponentiation ───────────────────────────────────────────────
    // f^((p¹²-1)/r)
    private static Fp12 FinalExponentiate(Fp12 f)
    {
        var exp = (BigInteger.Pow(P, 12) - 1) / R;
        return Fp12.Pow(f, exp);
    }

    // ── Output encoding ───────────────────────────────────────────────────
    private static byte[] Out(bool success)
    {
        var b = new byte[32];
        if (success) b[31] = 1;
        return b;
    }
}
