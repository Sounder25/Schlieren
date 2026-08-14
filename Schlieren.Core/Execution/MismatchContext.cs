namespace Schlieren.Core.Execution;

/// <summary>
/// Normalized mismatch signals for Layer 2 structural pattern rules.
/// Built by the harness/UI bridge from fixture execution reports.
/// </summary>
public sealed record MismatchContext(
    string ForkName,
    string FixturePath,
    string EipFolder,
    ulong GasUsed,
    long GasRefundCounter,
    bool HasBalanceMismatch,
    bool HasStorageMismatch,
    bool HasNonceMismatch,
    bool HasCodeMismatch,
    bool HasReceiptMismatch,
    bool HasMissingAccount,
    bool HasUnexpectedAccount,
    bool StorageWriteWhenExpectedEmpty,
    bool StorageEmptyWhenExpectedNonZero,
    bool BalanceActualBelowExpected,
    bool BalanceActualAboveExpected,
    /// <summary>Primary sender/account balance delta in gas units (actual−expected)/gasPrice; null if unknown.</summary>
    long? PrimaryBalanceDeltaGas,
    bool ReceiptExpectedFailActualSuccess,
    bool ReceiptExpectedSuccessActualFail,
    bool SenderNoncePlusOne,
    bool ContractNonceZeroWhenExpectedOne,
    bool TouchesCoinbaseBalance,
    bool IsOsakaOrLater,
    bool IsPragueOrLater);
