import struct

IV = [
    0x6a09e667f3bcc908, 0xbb67ae8584caa73b,
    0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
    0x510e527fade682d1, 0x9b05688c2b3e6c1f,
    0x1f83d9abfb41bd6b, 0x5be0cd19137e2179
]

SIGMA = [
    [  0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15 ],
    [ 14, 10,  4,  8,  9, 15, 13,  6,  1, 12,  0,  2, 11,  7,  5,  3 ],
    [ 11,  8, 12,  0,  5,  2, 15, 13, 10, 14,  3,  6,  7,  1,  9,  4 ],
    [  7,  9,  3,  1, 13, 12, 11, 14,  2,  6,  5, 10,  4,  0, 15,  8 ],
    [  9,  0,  5,  7,  2,  4, 10, 15, 14,  1, 11, 12,  6,  8,  3, 13 ],
    [  2, 12,  6, 10,  0, 11,  8,  3,  4, 13,  7,  5, 15, 14,  1,  9 ],
    [ 12,  5,  1, 15, 14, 13,  4, 10,  0,  7,  6,  3,  9,  2,  8, 11 ],
    [ 13, 11,  7, 14, 12,  1,  3,  9,  5,  0, 15,  4,  8,  6,  2, 10 ],
    [  6, 15, 14,  9, 11,  3,  0,  8, 12,  2, 13,  7,  1,  4, 10,  5 ],
    [ 10,  2,  8,  4,  7,  6,  1,  5, 15, 11,  9, 14,  3, 12, 13,  0 ]
]

def ror64(x, y):
    return ((x >> y) | (x << (64 - y))) & 0xFFFFFFFFFFFFFFFF

def G(v, a, b, c, d, x, y):
    v[a] = (v[a] + v[b] + x) & 0xFFFFFFFFFFFFFFFF
    v[d] = ror64(v[d] ^ v[a], 32)
    v[c] = (v[c] + v[d]) & 0xFFFFFFFFFFFFFFFF
    v[b] = ror64(v[b] ^ v[c], 24)
    v[a] = (v[a] + v[b] + y) & 0xFFFFFFFFFFFFFFFF
    v[d] = ror64(v[d] ^ v[a], 16)
    v[c] = (v[c] + v[d]) & 0xFFFFFFFFFFFFFFFF
    v[b] = ror64(v[b] ^ v[c], 63)

def blake2b_compress(h, m, t, f, rounds):
    v = h[:] + IV[:]
    v[12] ^= t[0]
    v[13] ^= t[1]
    if f:
        v[14] ^= 0xFFFFFFFFFFFFFFFF
        
    for r in range(rounds):
        s = SIGMA[r % 10]
        G(v, 0, 4, 8, 12, m[s[0]], m[s[1]])
        G(v, 1, 5, 9, 13, m[s[2]], m[s[3]])
        G(v, 2, 6, 10, 14, m[s[4]], m[s[5]])
        G(v, 3, 7, 11, 15, m[s[6]], m[s[7]])
        G(v, 0, 5, 10, 15, m[s[8]], m[s[9]])
        G(v, 1, 6, 11, 12, m[s[10]], m[s[11]])
        G(v, 2, 7, 8, 13, m[s[12]], m[s[13]])
        G(v, 3, 4, 9, 14, m[s[14]], m[s[15]])
        print(f"Round {r} v[0..15]: " + " ".join(f"{x:016x}" for x in v))
        
    for i in range(8):
        h[i] ^= v[i] ^ v[i+8]
    print(f"Final h[0..7]: " + " ".join(f"{x:016x}" for x in h))
    return h

if __name__ == '__main__':
    # Parse hex string into LE ulongs
    data_hex = "0000000c48c9bdf267e6096a3ba7ca8485ae67bb2bf894fe72f36e3cf1361d5f3af54fa5d182e6ad7f520e511f6c3e2b8c68059b6bbd41fbabd9831f79217e1319cde05b61626300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000300000000000000000000000000000001"
    b = bytes.fromhex(data_hex)
    
    rounds = struct.unpack(">I", b[0:4])[0]
    h = list(struct.unpack("<8Q", b[4:68]))
    m = list(struct.unpack("<16Q", b[68:196]))
    t = list(struct.unpack("<2Q", b[196:212]))
    f = b[212] == 1
    
    print(f"Rounds: {rounds}")
    print(f"h: {['%x' % x for x in h]}")
    print(f"m: {['%x' % x for x in m]}")
    print(f"t: {['%x' % x for x in t]}")
    print(f"f: {f}")
    
    res = blake2b_compress(h, m, t, f, rounds)
    output = b"".join(struct.pack("<Q", x) for x in res)
    print("Output:", output.hex())
    print("Expected:", "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923")
