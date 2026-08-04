using System.Numerics;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Xunit;

namespace Scrutor.Tests.Execution;

/// <summary>
/// Verifies that deep CALL recursion (up to 1024 depth) does not cause
/// a StackOverflowException when run on a thread with sufficient stack space.
/// </summary>
public class DeepCallRecursionTests
{
    /// <summary>
    /// Runs an async task on a thread with a 32MB stack.
    /// </summary>
    private static Task<T> RunOnLargeStackAsync<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = action().GetAwaiter().GetResult();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, 32 * 1024 * 1024);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    [Fact]
    public async Task DeepSelfCall_DoesNotStackOverflow_AtDepth512()
    {
        var selfAddr = Address.FromHex("0xdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        var selfAddrBytes = selfAddr.Bytes;

        // Bytecode: PUSH1 0 x5, PUSH20 self, PUSH4 0xFFFFFFFF, CALL, STOP
        var code = new List<byte>();
        code.Add(0x60); code.Add(0x00); // retLen
        code.Add(0x60); code.Add(0x00); // retOff
        code.Add(0x60); code.Add(0x00); // argsLen
        code.Add(0x60); code.Add(0x00); // argsOff
        code.Add(0x60); code.Add(0x00); // value
        code.Add(0x73); code.AddRange(selfAddrBytes); // PUSH20 addr
        code.Add(0x63); code.Add(0xFF); code.Add(0xFF); code.Add(0xFF); code.Add(0xFF); // PUSH4 gas
        code.Add(0xF1); // CALL
        code.Add(0x00); // STOP
        var bytecode = code.ToArray();

        var state = new GlobalState();
        state.SetCode(selfAddr, bytecode);
        state.SetBalance(selfAddr, BigInteger.Zero);

        var machine = new EvmMachine(typeof(IOpcode).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(type))
            .Select(type => (IOpcode)Activator.CreateInstance(type)!));
        var stateTransition = new StateTransition(machine);

        var tx = new Transaction
        {
            From = Address.FromHex("0x1111111111111111111111111111111111111111"),
            To = selfAddr,
            Value = BigInteger.Zero,
            Data = Array.Empty<byte>(),
            GasLimit = 30_000_000,
            GasPrice = BigInteger.One,
            Nonce = 0,
            Authorization = TransactionAuthorization.Impersonated
        };

        var block = new BlockContext
        {
            Number = 1,
            Timestamp = 1000,
            BaseFeePerGas = 1,
            Coinbase = Address.Zero,
            GasLimit = 30_000_000,
            Difficulty = 0
        };

        // Run on large stack — must NOT throw StackOverflowException
        var result = await RunOnLargeStackAsync(() =>
            stateTransition.ApplyTransactionAsync(tx, state, block));

        Assert.True(result.GasUsed > 0);
    }

    [Fact]
    public async Task DeepCall_ExceedsDepthLimit_ReturnsFailureNotCrash()
    {
        var selfAddr = Address.FromHex("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        // Bytecode: PUSH1 0 x5, PUSH20 self, GAS, CALL, STOP
        var code = new List<byte>();
        code.Add(0x60); code.Add(0x00); // retLen
        code.Add(0x60); code.Add(0x00); // retOff
        code.Add(0x60); code.Add(0x00); // argsLen
        code.Add(0x60); code.Add(0x00); // argsOff
        code.Add(0x60); code.Add(0x00); // value
        code.Add(0x73); code.AddRange(selfAddr.Bytes); // PUSH20 addr
        code.Add(0x5A); // GAS — forward all remaining
        code.Add(0xF1); // CALL
        code.Add(0x00); // STOP
        var bytecode = code.ToArray();

        var state = new GlobalState();
        state.SetCode(selfAddr, bytecode);

        var machine = new EvmMachine(typeof(IOpcode).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IOpcode).IsAssignableFrom(type))
            .Select(type => (IOpcode)Activator.CreateInstance(type)!));
        var stateTransition = new StateTransition(machine);

        var tx = new Transaction
        {
            From = Address.FromHex("0x1111111111111111111111111111111111111111"),
            To = selfAddr,
            Value = BigInteger.Zero,
            Data = Array.Empty<byte>(),
            GasLimit = 100_000_000,
            GasPrice = BigInteger.One,
            Nonce = 0,
            Authorization = TransactionAuthorization.Impersonated
        };

        var block = new BlockContext
        {
            Number = 1,
            Timestamp = 1000,
            BaseFeePerGas = 1,
            Coinbase = Address.Zero,
            GasLimit = 100_000_000,
            Difficulty = 0
        };

        // Run on large stack — must complete without StackOverflowException
        var result = await RunOnLargeStackAsync(() =>
            stateTransition.ApplyTransactionAsync(tx, state, block));

        Assert.True(result.GasUsed > 0);
    }
}
