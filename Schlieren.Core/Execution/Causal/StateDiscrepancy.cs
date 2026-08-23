using System.Numerics;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Execution.Causal;

public enum DiscrepancyKind
{
    MissingAccount,
    UnexpectedAccount,
    Balance,
    Nonce,
    Code,
    Storage,
    ReceiptStatus,
    ReceiptGasUsed,
    EngineException
}

/// <summary>A machine-readable expected/actual difference. Text is a projection, never an input.</summary>
public sealed record StateDiscrepancy
{
    public required DiscrepancyKind Kind { get; init; }
    public Address? Address { get; init; }
    public BigInteger? StorageSlot { get; init; }
    public BigInteger? ExpectedNumber { get; init; }
    public BigInteger? ActualNumber { get; init; }
    public bool? ExpectedBoolean { get; init; }
    public bool? ActualBoolean { get; init; }
    public string? Detail { get; init; }

    public string Category => Kind switch
    {
        DiscrepancyKind.MissingAccount => "missing_account",
        DiscrepancyKind.UnexpectedAccount => "unexpected_account",
        DiscrepancyKind.Balance => "balance",
        DiscrepancyKind.Nonce => "nonce",
        DiscrepancyKind.Code => "code",
        DiscrepancyKind.Storage => "storage",
        DiscrepancyKind.ReceiptStatus => "receipt_status",
        DiscrepancyKind.ReceiptGasUsed => "receipt_gas_used",
        DiscrepancyKind.EngineException => "engine_exception",
        _ => "other"
    };

    public string Render() => Kind switch
    {
        DiscrepancyKind.MissingAccount => $"missing account in actual state: {Address}",
        DiscrepancyKind.UnexpectedAccount => $"unexpected account in actual state: {Address}",
        DiscrepancyKind.Balance => $"balance mismatch for {Address}: expected={Hex(ExpectedNumber)}, actual={Hex(ActualNumber)}",
        DiscrepancyKind.Nonce => $"nonce mismatch for {Address}: expected={ExpectedNumber}, actual={ActualNumber}",
        DiscrepancyKind.Code => $"code mismatch for {Address}",
        DiscrepancyKind.Storage => $"storage mismatch for {Address} slot {Hex(StorageSlot)}: expected={Hex(ExpectedNumber)}, actual={Hex(ActualNumber)}",
        DiscrepancyKind.ReceiptStatus => $"receipt.status mismatch{(string.IsNullOrEmpty(Detail) ? "" : " " + Detail)}: expected={ExpectedBoolean}, actual={ActualBoolean}",
        DiscrepancyKind.ReceiptGasUsed => $"receipt.gasUsed mismatch {Detail}: expected={ExpectedNumber}, actual={ActualNumber}",
        DiscrepancyKind.EngineException => $"Unhandled engine exception: {Detail}",
        _ => Detail ?? Kind.ToString()
    };

    private static string Hex(BigInteger? value) =>
        value is null || value.Value.IsZero ? "0x0" : "0x" + value.Value.ToString("x");
}
