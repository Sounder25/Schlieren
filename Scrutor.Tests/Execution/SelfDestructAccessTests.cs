using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using EvmExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.Tests.Execution;

public sealed class SelfDestructAccessTests
{
    [Fact]
    public async Task SelfDestruct_ChargesStaticCostForWarmBeneficiary()
    {
        var beneficiary = Address.FromHex(
            "0x0000000000000000000000000000000000001000");
        var context = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex(
                "0x0000000000000000000000000000000000002000"),
            GlobalState = new GlobalState(),
            GasLimit = 100_000
        };
        context.Access.WarmAddress(beneficiary);
        context.Stack.Push(new BigInteger(
            beneficiary.Bytes,
            isUnsigned: true,
            isBigEndian: true));

        var (result, _) = await new OpcodeSelfDestruct().ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000UL, result.GasUsed);
    }

    [Fact]
    public async Task Create_WarmsCreatedAddress()
    {
        var context = CreateContext();
        var createdAddress = CryptoUtils.DeriveContractAddress(
            context.ContractAddress,
            nonce: 0);
        PushCreateArguments(context);

        await new OpcodeCreate().ExecuteAsync(context);

        Assert.True(context.Access.TouchAddress(createdAddress));
    }

    [Fact]
    public async Task Create2_WarmsCreatedAddress()
    {
        var context = CreateContext();
        var salt = new byte[32];
        var createdAddress = CryptoUtils.DeriveContractAddress2(
            context.ContractAddress,
            salt,
            []);
        context.Stack.Push(BigInteger.Zero); // salt
        PushCreateArguments(context);

        await new OpcodeCreate2().ExecuteAsync(context);

        Assert.True(context.Access.TouchAddress(createdAddress));
    }

    private static EvmExecutionContext CreateContext()
    {
        var context = new EvmExecutionContext
        {
            ContractAddress = Address.FromHex(
                "0x0000000000000000000000000000000000002000"),
            GlobalState = new GlobalState(),
            GasLimit = 100_000
        };
        context.SubCall = (transaction, _, _, _) =>
            Task.FromResult(ExecutionResult.Success(transaction.GasLimit));
        return context;
    }

    private static void PushCreateArguments(EvmExecutionContext context)
    {
        context.Stack.Push(BigInteger.Zero); // length
        context.Stack.Push(BigInteger.Zero); // offset
        context.Stack.Push(BigInteger.Zero); // value
    }
}
