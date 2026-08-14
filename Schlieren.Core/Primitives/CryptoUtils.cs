using Nethereum.Signer.Crypto;
using Nethereum.Util;
using Org.BouncyCastle.Math;

namespace Schlieren.Core.Primitives;

public static class CryptoUtils
{
    private static readonly Sha3Keccack _keccak = new();

    /// <summary>
    /// Indicates if cryptographic functions are currently stubbed.
    /// </summary>
    public static bool IsStubbedCrypto => false;

    /// <summary>
    /// Computes the Keccak256 hash of the given data.
    /// </summary>
    public static byte[] Keccak256(byte[] data)
    {
        return _keccak.CalculateHash(data);
    }

    /// <summary>
    /// Recovers the sender address from a signed transaction.
    /// Enforces EIP-2 Low-S rule (s ≤ n/2) — use for transaction validation only.
    /// For the ecRecover precompile use <see cref="RecoverAddressForPrecompile"/>.
    /// </summary>
    public static Address RecoverAddress(byte[] hash32, int v, byte[] r, byte[] s)
    {
        if (hash32 is null || hash32.Length != 32)
            throw new ArgumentException("hash must be exactly 32 bytes", nameof(hash32));
        if (r is null) throw new ArgumentNullException(nameof(r));
        if (s is null) throw new ArgumentNullException(nameof(s));

        r = Pad32(r);
        s = Pad32(s);

        // Unsigned r/s (critical)
        var rBI = new BigInteger(1, r);
        var sBI = new BigInteger(1, s);

        // Validation (EIP-2 Low-S and range)
        var curve = Org.BouncyCastle.Asn1.X9.ECNamedCurveTable.GetByName("secp256k1");
        var n = curve.N;
        if (rBI.CompareTo(BigInteger.One) < 0 || rBI.CompareTo(n) >= 0)
            throw new ArgumentException("r out of range");
        if (sBI.CompareTo(BigInteger.One) < 0 || sBI.CompareTo(n) >= 0)
            throw new ArgumentException("s out of range");
        
        // Low-S check (transaction validation — EIP-2 Homestead+)
        var halfN = n.ShiftRight(1);
        if (sBI.CompareTo(halfN) > 0)
            throw new ArgumentException("s must be in lower half of n (Low-S rule)");

        var sig = new ECDSASignature(rBI, sBI);

        // Preferred candidate (if v is sane), then fall back to brute-force
        int? preferred = TryMapVToRecoveryId(v);

        foreach (var recId in RecoveryIdCandidates(preferred))
        {
            try
            {
                // false => uncompressed public key
                var ecKey = ECKey.RecoverFromSignature(recId, sig, hash32, false);
                if (ecKey == null) continue;

                // Nethereum requires explicit isCompressed
                var pubKey65 = ecKey.GetPubKey(false); // 65 bytes: 0x04 + X(32) + Y(32)

                // Ethereum address: last 20 bytes of Keccak256(X||Y) where X||Y is 64 bytes (drop 0x04)
                var pubKeyNoPrefix = pubKey65.Skip(1).ToArray(); // 64 bytes
                var keccak = _keccak.CalculateHash(pubKeyNoPrefix); // 32 bytes
                var addrBytes = keccak.Skip(12).ToArray();       // last 20 bytes

                return new Address(addrBytes);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid point compression", StringComparison.OrdinalIgnoreCase))
            {
                // wrong recId for this signature; try next
                continue;
            }
            catch
            {
                continue;
            }
        }

        throw new InvalidOperationException("Failed to recover public key from signature using any recovery id (0..3).");
    }

    /// <summary>
    /// Recovers an address from a signature — for use by the ecRecover precompile (0x01) ONLY.
    /// Unlike <see cref="RecoverAddress"/>, this does NOT enforce the Low-S rule, because
    /// EELS ecrecover accepts any s in [1, N) (the Low-S rule only applies to tx signing).
    /// Returns null for out-of-range r/s or if recovery produces no valid key.
    /// </summary>
    public static Address? RecoverAddressForPrecompile(byte[] hash32, int v, byte[] r, byte[] s)
    {
        if (hash32 is null || hash32.Length != 32) return null;
        if (r is null || s is null) return null;

        r = Pad32(r);
        s = Pad32(s);

        var rBI = new BigInteger(1, r);
        var sBI = new BigInteger(1, s);

        var curve = Org.BouncyCastle.Asn1.X9.ECNamedCurveTable.GetByName("secp256k1");
        var n = curve.N;
        // Range check: r and s must be in [1, N)  (but NO Low-S enforcement)
        if (rBI.CompareTo(BigInteger.One) < 0 || rBI.CompareTo(n) >= 0) return null;
        if (sBI.CompareTo(BigInteger.One) < 0 || sBI.CompareTo(n) >= 0) return null;

        var sig = new ECDSASignature(rBI, sBI);
        int? preferred = TryMapVToRecoveryId(v);

        foreach (var recId in RecoveryIdCandidates(preferred))
        {
            try
            {
                var ecKey = ECKey.RecoverFromSignature(recId, sig, hash32, false);
                if (ecKey == null) continue;

                var pubKey65 = ecKey.GetPubKey(false);
                var pubKeyNoPrefix = pubKey65.Skip(1).ToArray();
                var keccak = _keccak.CalculateHash(pubKeyNoPrefix);
                var addrBytes = keccak.Skip(12).ToArray();
                return new Address(addrBytes);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid point compression", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            catch
            {
                continue;
            }
        }

        return null; // no valid recovery id found
    }

    public static Address DeriveContractAddress(Address sender, ulong nonce)
    {
        // RLP of [sender, nonce]
        var nonceBytes = ToBytesForRLP(nonce);
        
        var rlp = Nethereum.RLP.RLP.EncodeList(
            Nethereum.RLP.RLP.EncodeElement(sender.Bytes),
            Nethereum.RLP.RLP.EncodeElement(nonceBytes)
        );
        var hash = Keccak256(rlp);
        
        var addrBytes = new byte[20];
        Array.Copy(hash, 12, addrBytes, 0, 20);
        return new Address(addrBytes);
    }

    public static Address DeriveContractAddress2(Address sender, byte[] salt32, byte[] initCode)
    {
        if (salt32.Length != 32) throw new ArgumentException("Salt must be 32 bytes");
        
        var initCodeHash = Keccak256(initCode);
        var buffer = new byte[1 + 20 + 32 + 32];
        buffer[0] = 0xff;
        Buffer.BlockCopy(sender.Bytes, 0, buffer, 1, 20);
        Buffer.BlockCopy(salt32, 0, buffer, 21, 32);
        Buffer.BlockCopy(initCodeHash, 0, buffer, 53, 32);
        
        var hash = Keccak256(buffer);
        var addrBytes = new byte[20];
        Array.Copy(hash, 12, addrBytes, 0, 20);
        return new Address(addrBytes);
    }

    public static byte[] ToBytesForRLP(ulong value)
    {
        if (value == 0) return Array.Empty<byte>();
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return bytes.SkipWhile(b => b == 0).ToArray();
    }

    private static int? TryMapVToRecoveryId(int v)
    {
        // Map common Ethereum encodings to a starting candidate.
        return v switch
        {
            0 or 1 or 2 or 3 => v,
            27 or 28 => v - 27,          // yields 0 or 1
            >= 35 => (v - 35) & 1,       // yields 0 or 1 (parity)
            _ => null
        };
    }

    private static int[] RecoveryIdCandidates(int? preferred)
    {
        // Try preferred first if present, then try the rest of 0..3
        if (preferred is >= 0 and <= 3)
        {
            return new[] { preferred.Value, 0, 1, 2, 3 }.Distinct().ToArray();
        }
        return new[] { 0, 1, 2, 3 };
    }

    private static byte[] Pad32(byte[] x)
    {
        if (x.Length == 32) return x;
        if (x.Length > 32) return x.Skip(x.Length - 32).ToArray(); // keep rightmost 32
        var padded = new byte[32];
        Buffer.BlockCopy(x, 0, padded, 32 - x.Length, x.Length);
        return padded;
    }

    [Obsolete("Use Keccak256(data). This method will be removed.", error: false)]
    public static byte[] Keccak256Stub(byte[] data) => Keccak256(data);

    [Obsolete("Use RecoverAddress(hash, v, r, s). This method will be removed.", error: false)]
    public static Address RecoverAddressStub(byte[] hash, int v, byte[] r, byte[] s) => RecoverAddress(hash, v, r, s);
}
