using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using EvmExecutionContext = Schlieren.Core.Execution.ExecutionContext;

namespace Schlieren.Tests.Execution;

public sealed class EvmMachineExceptionHaltTests
{
    [Fact]
    public async Task ThrowingStackPop_FailsFrameWithStackUnderflow_DoesNotRethrow()
    {
        var context = new EvmExecutionContext
        {
            Code = [0x50],
            GasLimit = 65_535,
            CaptureTrace = true
        };
        var machine = new EvmMachine([new ThrowingPopOpcode()]);

        var result = await machine.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.StackUnderflow, result.Error);
        Assert.Equal(context.GasLimit, result.GasUsed);
        Assert.NotEmpty(result.TraceSteps);
    }

    [Fact]
    public async Task ThrowingStackPush_FailsFrameWithStackOverflow_DoesNotRethrow()
    {
        var context = new EvmExecutionContext
        {
            Code = [0x60, 0x01],
            GasLimit = 65_535,
            CaptureTrace = true
        };
        for (var i = 0; i < 1024; i++)
            context.Stack.Push(i);

        var machine = new EvmMachine([new ThrowingPushOpcode()]);

        var result = await machine.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.StackOverflow, result.Error);
        Assert.Equal(context.GasLimit, result.GasUsed);
    }

    [Fact]
    public async Task ThrowingBadJump_FailsFrameWithBadJumpDestination_DoesNotRethrow()
    {
        var context = new EvmExecutionContext
        {
            Code = [0x56],
            GasLimit = 10_000,
            CaptureTrace = true
        };
        var machine = new EvmMachine([new ThrowingJumpOpcode()]);

        var result = await machine.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.BadJumpDestination, result.Error);
        Assert.Equal(context.GasLimit, result.GasUsed);
    }

    [Fact]
    public async Task ThrowingInvalidOpcode_FailsFrameWithInvalidOpcode_DoesNotRethrow()
    {
        var context = new EvmExecutionContext
        {
            Code = [0x0c],
            GasLimit = 10_000,
            CaptureTrace = true
        };
        var machine = new EvmMachine([new ThrowingInvalidOpcode()]);

        var result = await machine.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal(EvmError.InvalidOpcode, result.Error);
        Assert.Equal(context.GasLimit, result.GasUsed);
    }

    [Fact]
    public async Task UnexpectedException_StillRethrowsAsInternal()
    {
        var context = new EvmExecutionContext
        {
            Code = [0x00],
            GasLimit = 10_000
        };
        var machine = new EvmMachine([new UnexpectedThrowOpcode()]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => machine.ExecuteAsync(context));
        Assert.Equal("not a protocol halt", ex.Message);
    }

    private sealed class ThrowingPopOpcode : IOpcode
    {
        public byte Code => 0x50;
        public string Name => "POP";
        public ValueTask<(ExecutionResult, int)> ExecuteAsync(
            EvmExecutionContext context, CancellationToken ct = default)
        {
            context.Stack.Pop();
            return new((ExecutionResult.Success(2), context.ProgramCounter + 1));
        }
    }

    private sealed class ThrowingPushOpcode : IOpcode
    {
        public byte Code => 0x60;
        public string Name => "PUSH1";
        public ValueTask<(ExecutionResult, int)> ExecuteAsync(
            EvmExecutionContext context, CancellationToken ct = default)
        {
            context.Stack.Push(1);
            return new((ExecutionResult.Success(3), context.ProgramCounter + 2));
        }
    }

    private sealed class ThrowingJumpOpcode : IOpcode
    {
        public byte Code => 0x56;
        public string Name => "JUMP";
        public ValueTask<(ExecutionResult, int)> ExecuteAsync(
            EvmExecutionContext context, CancellationToken ct = default)
        {
            throw new EvmBadJumpDestinationException("destination is not JUMPDEST");
        }
    }

    private sealed class ThrowingInvalidOpcode : IOpcode
    {
        public byte Code => 0x0c;
        public string Name => "0x0C";
        public ValueTask<(ExecutionResult, int)> ExecuteAsync(
            EvmExecutionContext context, CancellationToken ct = default)
        {
            throw new EvmInvalidOpcodeException(0x0c);
        }
    }

    private sealed class UnexpectedThrowOpcode : IOpcode
    {
        public byte Code => 0x00;
        public string Name => "STOP";
        public ValueTask<(ExecutionResult, int)> ExecuteAsync(
            EvmExecutionContext context, CancellationToken ct = default)
        {
            throw new InvalidOperationException("not a protocol halt");
        }
    }
}
