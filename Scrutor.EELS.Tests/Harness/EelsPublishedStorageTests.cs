using System.Numerics;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;

namespace Scrutor.EELS.Tests.Harness;

public sealed class EelsPublishedStorageTests
{
    [Fact]
    public async Task PublishedPostStorage_DoesNotInheritClearedPreStateSlots()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "fixtures", "state_tests", "cancun", "eip1153_tstore",
            "basic_tload"));
        var options = new EelsHarnessOptions(
            fixtureRoot,
            "Cancun",
            int.MaxValue,
            IncludeSubdirectories: true);

        var testCase = Assert.Single(
            new EelsStateFixtureLoader().LoadCases(options),
            testCase => testCase.CaseId.Contains(
                "test_basic_tload_after_store",
                StringComparison.Ordinal));
        var contract = Address.FromHex(
            "0x0000000000000000000000000000000000001000");
        var expectedStorage = testCase.ExpectedPostState[contract].Storage;

        Assert.False(expectedStorage.ContainsKey(BigInteger.One));
        Assert.Equal(BigInteger.One, expectedStorage[new BigInteger(2)]);

        var report = await new EelsStateFixtureExecutor().ExecuteAsync(testCase);
        Assert.True(
            report.StateMatches,
            string.Join(Environment.NewLine, report.Mismatches));
    }

    [Fact]
    public async Task StateComparison_RejectsUnexpectedNonZeroStorage()
    {
        var contract = Address.FromHex(
            "0x0000000000000000000000000000000000001000");
        var accountWithUnexpectedStorage = new EelsFixtureAccount(
            Nonce: 1,
            Balance: BigInteger.Zero,
            Code: [0x00],
            Storage: new Dictionary<BigInteger, BigInteger>
            {
                [BigInteger.One] = BigInteger.One
            });
        var expectedAccount = accountWithUnexpectedStorage with
        {
            Storage = new Dictionary<BigInteger, BigInteger>()
        };
        var testCase = new EelsStateCase(
            FixturePath: "synthetic",
            CaseId: "unexpected-storage",
            ForkName: "Cancun",
            BlockContext: new BlockContext
            {
                ChainId = 1,
                Number = 1,
                Timestamp = 1,
                GasLimit = 30_000_000,
                Coinbase = Address.Zero
            },
            Sender: Address.Zero,
            Transaction: new Transaction
            {
                From = Address.Zero,
                To = contract,
                GasLimit = 100_000,
                Authorization = TransactionAuthorization.Internal
            },
            PreState: new Dictionary<Address, EelsFixtureAccount>
            {
                [contract] = accountWithUnexpectedStorage
            },
            ExpectedPostState: new Dictionary<Address, EelsFixtureAccount>
            {
                [contract] = expectedAccount
            },
            ExpectedReceiptStatus: true);

        var report = await new EelsStateFixtureExecutor().ExecuteAsync(testCase);

        Assert.False(report.StateMatches);
        Assert.Contains(
            report.Mismatches,
            mismatch =>
                mismatch.Contains("slot 0x1", StringComparison.Ordinal) &&
                mismatch.Contains("expected=0x0", StringComparison.Ordinal) &&
                mismatch.Contains("actual=0x1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateComparison_RejectsUnexpectedNonEmptyAccount()
    {
        var expectedAddress = Address.FromHex(
            "0x0000000000000000000000000000000000001000");
        var unexpectedAddress = Address.FromHex(
            "0x0000000000000000000000000000000000002000");
        var emptyAccount = new EelsFixtureAccount(
            Nonce: 0,
            Balance: BigInteger.Zero,
            Code: [0x00],
            Storage: new Dictionary<BigInteger, BigInteger>());
        var unexpectedAccount = new EelsFixtureAccount(
            Nonce: 0,
            Balance: BigInteger.One,
            Code: Array.Empty<byte>(),
            Storage: new Dictionary<BigInteger, BigInteger>());
        var testCase = new EelsStateCase(
            FixturePath: "synthetic",
            CaseId: "unexpected-account",
            ForkName: "Cancun",
            BlockContext: new BlockContext(),
            Sender: Address.Zero,
            Transaction: new Transaction
            {
                From = Address.Zero,
                To = expectedAddress,
                GasLimit = 100_000,
                Authorization = TransactionAuthorization.Internal
            },
            PreState: new Dictionary<Address, EelsFixtureAccount>
            {
                [expectedAddress] = emptyAccount,
                [unexpectedAddress] = unexpectedAccount
            },
            ExpectedPostState: new Dictionary<Address, EelsFixtureAccount>
            {
                [expectedAddress] = emptyAccount
            },
            ExpectedReceiptStatus: true);

        var report = await new EelsStateFixtureExecutor().ExecuteAsync(testCase);

        Assert.False(report.StateMatches);
        Assert.Contains(
            report.Mismatches,
            mismatch =>
                mismatch.Contains(
                    "unexpected account in actual state",
                    StringComparison.Ordinal) &&
                mismatch.Contains(
                    unexpectedAddress.ToString(),
                    StringComparison.Ordinal));
    }
}
