namespace Scrutor.Core.Forks;

/// <summary>
/// Canonical Ethereum fork ordering. Each value equals its integer rank so
/// comparisons like <c>fork &gt;= Fork.London</c> work correctly.
/// Mirrors EELS ethereum/forks/ directory order.
/// </summary>
public enum Fork
{
    Frontier         = 0,
    Homestead        = 1,
    TangerineWhistle = 2,   // EIP-150: repricing for IO-heavy ops
    SpuriousDragon   = 3,   // EIP-161: state-clearing, EIP-155: replay protection
    Byzantium        = 4,   // EIP-140: REVERT, EIP-214: STATICCALL, EIP-211: RETURNDATASIZE
    Constantinople   = 5,   // EIP-145: bitshift, EIP-1014: CREATE2, EIP-1052: EXTCODEHASH
    Istanbul         = 6,   // EIP-1884: SLOAD repricing, EIP-2200: SSTORE metering
    Berlin           = 7,   // EIP-2929: access lists, EIP-2930: optional access lists
    London           = 8,   // EIP-1559: base fee, EIP-3529: refund cap, EIP-3541: EF-prefix
    Paris            = 9,   // EIP-3675: proof-of-stake, DIFFICULTY→PREVRANDAO
    Shanghai         = 10,  // EIP-3855: PUSH0, EIP-4895: withdrawals
    Cancun           = 11,  // EIP-1153: TLOAD/TSTORE, EIP-4844: blob tx, EIP-5656: MCOPY
    Prague           = 12,  // EIP-7702: set-code, EIP-7623: calldata floor, EIP-2537: BLS
    Osaka            = 13,  // future
}
