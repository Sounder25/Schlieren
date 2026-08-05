using System.Numerics;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.EELS.Tests.Harness;

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
    bool? ExpectedReceiptStatus);

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
    IReadOnlyList<string> Mismatches);
