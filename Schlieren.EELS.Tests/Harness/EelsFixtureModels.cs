using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Execution.Causal;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.EELS.Tests.Harness;

public sealed record EelsFixtureAccount(
    ulong Nonce,
    BigInteger Balance,
    byte[] Code,
    IReadOnlyDictionary<BigInteger, BigInteger> Storage);

public sealed record EelsStateCase(
    string FixturePath,
    string CaseId,
    string ForkName,
    BlockContext BlockContext,
    Address Sender,
    Transaction Transaction,
    IReadOnlyDictionary<Address, EelsFixtureAccount> PreState,
    IReadOnlyDictionary<Address, EelsFixtureAccount> ExpectedPostState,
    bool? ExpectedReceiptStatus,
    /// <summary>
    /// When set, the fixture declares the transaction as invalid (expectException field).
    /// The executor should treat this case as "tx should be rejected" — no state change,
    /// receipt status = false.  Populated from EELS modern-format fixture variants that
    /// carry an <c>expectException</c> property.
    /// </summary>
    string? ExpectedException = null);

public sealed record EelsCaseExecutionReport(
    string CaseId,
    bool ExecutionSucceeded,
    ulong GasUsed,
    /// <summary>
    /// Raw EVM gas refund counter BEFORE the EIP-3529 cap (gasUsed/5) is applied.
    /// Use this to reconstruct Term 4 of the sender balance ledger exactly:
    ///   term4 = min(GasRefundCounter, GasUsed / 5) × effectiveGasPrice
    /// Zero when execution failed before the EVM ran (e.g. intrinsic gas OOG).
    /// </summary>
    long GasRefundCounter,
    bool StateMatches,
    bool ReceiptStatusMatches,
    IReadOnlyList<string> Mismatches,
    EvmError Error = EvmError.None,
    string? LastOpcode = null,
    int LastPc = 0,
    IReadOnlyList<StateDiscrepancy>? Discrepancies = null);
