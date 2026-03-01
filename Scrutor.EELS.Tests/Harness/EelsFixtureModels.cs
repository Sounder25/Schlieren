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
    bool StateMatches,
    bool ReceiptStatusMatches,
    IReadOnlyList<string> Mismatches);
