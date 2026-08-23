using System.Numerics;
using Schlieren.Core.Forks;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Causal;

/// <summary>
/// Compact record from one failed canonical run. Enough to score inventory rules
/// without a second execution path.
/// </summary>
public sealed class FailureEvidence
{
    public required string CaseId { get; init; }
    public required string ForkName { get; init; }
    public required string FixturePath { get; init; }
    public Fork Fork { get; init; }
    public string TestFamily { get; init; } = "";
    public bool ExecutionSucceeded { get; init; }
    public EvmError Error { get; init; }
    public ulong GasUsed { get; init; }
    public long RefundCounter { get; init; }
    public ulong TxGasLimit { get; init; }
    public bool IsCreateTx { get; init; }
    public int InitcodeLength { get; init; }
    public string? LastOpcode { get; init; }
    public int LastPc { get; init; }
    public string? ExpectException { get; init; }
    public bool? ExpectedReceiptSuccess { get; init; }
    public Address Sender { get; init; }
    public Address Coinbase { get; init; }
    public Address? To { get; init; }
    public BigInteger EffectiveGasPrice { get; init; }
    public IReadOnlyList<StateDiscrepancy> Discrepancies { get; init; } = Array.Empty<StateDiscrepancy>();

    public bool HasMissingAccount { get; init; }
    public bool HasUnexpectedAccount { get; init; }
    public bool HasStorageMismatch { get; init; }
    public bool HasCodeMismatch { get; init; }
    public bool HasNonceMismatch { get; init; }
    public bool HasBalanceMismatch { get; init; }
    public bool HasReceiptMismatch { get; init; }
    public bool ReceiptExpectedSuccessActualFail { get; init; }
    public bool ReceiptExpectedFailActualSuccess { get; init; }

    /// <summary>Sender (actual − expected) / effectiveGasPrice. Null if not divisible or no sender balance line.</summary>
    public long? SenderGasResidual { get; init; }

    /// <summary>Sender actual − expected, wei.</summary>
    public BigInteger? SenderWeiDelta { get; init; }

    /// <summary>Coinbase actual − expected, wei.</summary>
    public BigInteger? CoinbaseWeiDelta { get; init; }

    /// <summary>
    /// When sender and coinbase residuals cancel (fee shift), this is |senderWei| / gasPrice.
    /// Isolated gas-used error. Downstream of the first wrong charge.
    /// </summary>
    public long? FeePairGas { get; init; }

    public IForkRules Rules { get; init; } = default!;
}
