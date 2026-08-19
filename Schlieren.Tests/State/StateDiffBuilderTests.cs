using System.Numerics;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.State;

public sealed class StateDiffBuilderTests
{
    private static Address Addr(byte last) =>
        new(Enumerable.Repeat((byte)0, 19).Append(last).ToArray());

    [Fact]
    public void CreatedEmptyEoa_DoesNotMarkCodeChanged()
    {
        var pre = new Dictionary<Address, Account>();
        var post = new Dictionary<Address, Account>
        {
            [Addr(0xAA)] = new Account { Balance = 1_000, Nonce = 0, Code = Array.Empty<byte>() }
        };

        var diff = StateDiffBuilder.Compare(pre, post);
        var acct = Assert.Single(diff.Accounts);

        Assert.Equal(StateDiffBuilder.AccountDeltaKind.Created, acct.Kind);
        Assert.False(acct.CodeChanged);
        Assert.DoesNotContain("Code: CHANGED", StateDiffBuilder.RenderText(diff), StringComparison.Ordinal);
    }

    [Fact]
    public void CreatedContract_MarksCodeChanged()
    {
        var pre = new Dictionary<Address, Account>();
        var post = new Dictionary<Address, Account>
        {
            [Addr(0xBB)] = new Account { Balance = 0, Nonce = 1, Code = [0x60, 0x00] }
        };

        var diff = StateDiffBuilder.Compare(pre, post);
        var acct = Assert.Single(diff.Accounts);

        Assert.Equal(StateDiffBuilder.AccountDeltaKind.Created, acct.Kind);
        Assert.True(acct.CodeChanged);
        Assert.Contains("Code: CHANGED", StateDiffBuilder.RenderText(diff), StringComparison.Ordinal);
    }

    [Fact]
    public void DeletedEmptyEoa_DoesNotMarkCodeChanged()
    {
        var pre = new Dictionary<Address, Account>
        {
            [Addr(0xCC)] = new Account { Balance = 50, Code = Array.Empty<byte>() }
        };
        var post = new Dictionary<Address, Account>();

        var diff = StateDiffBuilder.Compare(pre, post);
        var acct = Assert.Single(diff.Accounts);

        Assert.Equal(StateDiffBuilder.AccountDeltaKind.Deleted, acct.Kind);
        Assert.False(acct.CodeChanged);
        Assert.DoesNotContain("Code: CHANGED", StateDiffBuilder.RenderText(diff), StringComparison.Ordinal);
    }

    [Fact]
    public void DeletedContract_MarksCodeChanged()
    {
        var pre = new Dictionary<Address, Account>
        {
            [Addr(0xDD)] = new Account { Nonce = 1, Code = [0xff] }
        };
        var post = new Dictionary<Address, Account>();

        var diff = StateDiffBuilder.Compare(pre, post);
        var acct = Assert.Single(diff.Accounts);

        Assert.Equal(StateDiffBuilder.AccountDeltaKind.Deleted, acct.Kind);
        Assert.True(acct.CodeChanged);
    }

    [Fact]
    public void ModifiedBalanceOnly_DoesNotMarkCodeChanged()
    {
        var addr = Addr(0xEE);
        var pre = new Dictionary<Address, Account>
        {
            [addr] = new Account { Balance = 1, Code = Array.Empty<byte>() }
        };
        var post = new Dictionary<Address, Account>
        {
            [addr] = new Account { Balance = 2, Code = Array.Empty<byte>() }
        };

        var diff = StateDiffBuilder.Compare(pre, post);
        var acct = Assert.Single(diff.Accounts);

        Assert.Equal(StateDiffBuilder.AccountDeltaKind.Modified, acct.Kind);
        Assert.False(acct.CodeChanged);
        Assert.Equal(BigInteger.One, acct.BalanceDelta);
    }

    [Fact]
    public void ModifiedRuntimeUpgrade_MarksCodeChanged()
    {
        var addr = Addr(0xFF);
        var pre = new Dictionary<Address, Account>
        {
            [addr] = new Account { Code = [0x00] }
        };
        var post = new Dictionary<Address, Account>
        {
            [addr] = new Account { Code = [0x5b] }
        };

        var diff = StateDiffBuilder.Compare(pre, post);
        var acct = Assert.Single(diff.Accounts);

        Assert.True(acct.CodeChanged);
    }
}
