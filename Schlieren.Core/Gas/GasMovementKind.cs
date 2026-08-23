namespace Schlieren.Core.Gas;

public enum GasMovementKind
{
    Charge,
    TransferOut,
    TransferIn,
    Return,
    RefundCounterDelta,
    Burn,
    Settlement
}