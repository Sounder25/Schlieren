using System;
using System.Threading.Tasks;
using Xunit;
using Scrutor.Core.Execution;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using System.Collections.Generic;
using System.Reflection;
using System.Numerics;

namespace Scrutor.Core.Tests;

public class CallFailureTest
{
    private static IReadOnlyList<IOpcode> CreateAllOpcodes()
    {
        var opcodeType = typeof(IOpcode);
        var assembly = opcodeType.Assembly;

        var instances = new List<IOpcode>();
        foreach (var type in assembly.GetTypes())
        {
            if (!opcodeType.IsAssignableFrom(type) ||
                type.IsAbstract ||
                type.IsInterface)
            {
                continue;
            }

            var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null);
            if (ctor is null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is IOpcode opcode)
            {
                instances.Add(opcode);
            }
        }

        return instances
            .OrderBy(op => op.Code)
            .ToArray();
    }

    [Fact]
    public async Task TestCallWithFailingChild()
    {
        // Create a context with a failing sub-call
        var globalState = new GlobalState();
        var access = new AccessTracker();
        var callerAddress = new Address(new byte[20] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var contractAddress = new Address(new byte[20] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var toAddress = new Address(new byte[20] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100 }); // Address ending in 100 - NOT a precompile
        
        // Create context with a mock sub-call that always fails
        var context = new Scrutor.Core.Execution.ExecutionContext
        {
            GlobalState = globalState,
            Access = access,
            Caller = callerAddress,
            ContractAddress = contractAddress,
            CallValue = 0,
            IsStatic = false,
            CaptureTrace = true,
            GasLimit = 1_000_000,
            Code = new byte[] { 0xF1, 0x00 }, // CALL opcode, STOP
            CallDepth = 1,
            SubCall = async (tx, isStatic, creationAddress, codeAddress) => 
            {
                Console.WriteLine($"[CHILD_HANDLER] SubCall invoked! tx.To={tx.To} tx.Value={tx.Value}");
                // Return a failure result to simulate a failing child call
                return ExecutionResult.Failure(EvmError.InternalError);
            }
        };
        
        // Set up stack for CALL: gas, address, value, argsOffset, argsLength, retOffset, retLength
        context.Stack.TryPush(new BigInteger(0)); // retLength
        context.Stack.TryPush(new BigInteger(0)); // retOffset
        context.Stack.TryPush(new BigInteger(0)); // argsLength
        context.Stack.TryPush(new BigInteger(0)); // argsOffset
        context.Stack.TryPush(new BigInteger(0)); // value
        context.Stack.Push(new BigInteger(toAddress.Bytes, isUnsigned: true)); // address - use the same address we're calling
        context.Stack.TryPush(new BigInteger(100000)); // gas
        
        // Create EvmMachine with all opcodes
        var opcodes = CreateAllOpcodes();
        var machine = new EvmMachine(opcodes);
        
        // Run the machine
        var result = await machine.ExecuteAsync(context, CancellationToken.None);
        
        // Verify the result - CALL with failing child should succeed (push 0 to stack)
        // The opcode itself succeeds, it's the child that fails
        Console.WriteLine($"[TEST_RESULT] success={result.IsSuccess} error={result.Error}");
        Console.WriteLine($"[STACK_TOP] stack[0]={context.Stack.Peek()}");
        
        // After CALL with failing child, the stack should have 0 (failure indicator)
        Assert.True(result.IsSuccess, "The CALL opcode itself should succeed even if child fails");
        Assert.Equal(BigInteger.Zero, context.Stack.Peek()); // 0 means child failed
    }
}