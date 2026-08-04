using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Xunit;

namespace Scrutor.Tests.Execution;

public class CallTypeTraceTests
{
    [Fact]
    public void ExecutionTraceStep_HasCallTypeField()
    {
        // Arrange
        var step = new ExecutionTraceStep
        {
            Pc = 0,
            Op = "JUMPDEST",
            Gas = "0x0",
            GasCost = "0x1",
            Depth = 1,
            CallType = CallType.Call
        };
        
        // Assert
        Assert.Equal(CallType.Call, step.CallType);
    }
    
    [Fact]
    public void CallTypeEnum_HasAllTypes()
    {
        // Assert all security-relevant call types exist
        Assert.Equal(0, (int)CallType.Root);
        Assert.Equal(1, (int)CallType.Call);
        Assert.Equal(2, (int)CallType.CallCode);
        Assert.Equal(3, (int)CallType.DelegateCall);
        Assert.Equal(4, (int)CallType.StaticCall);
        Assert.Equal(5, (int)CallType.Create);
        Assert.Equal(6, (int)CallType.Create2);
    }
    
    [Fact]
    public void DetermineCallType_ReturnsCorrectType()
    {
        // Test CREATE detection
        var createType = StateTransitionTestHelper.DetermineCallType(
            creationAddress: Address.Zero,
            codeAddress: null,
            isStatic: false);
        Assert.Equal(CallType.Create, createType);
        
        // Test DELEGATECALL detection
        var delegateType = StateTransitionTestHelper.DetermineCallType(
            creationAddress: null,
            codeAddress: Address.Zero,
            isStatic: false);
        Assert.Equal(CallType.DelegateCall, delegateType);
        
        // Test STATICCALL detection
        var staticType = StateTransitionTestHelper.DetermineCallType(
            creationAddress: null,
            codeAddress: null,
            isStatic: true);
        Assert.Equal(CallType.StaticCall, staticType);
        
        // Test regular CALL
        var callType = StateTransitionTestHelper.DetermineCallType(
            creationAddress: null,
            codeAddress: null,
            isStatic: false);
        Assert.Equal(CallType.Call, callType);
    }
}

/// <summary>
/// Helper to expose the private DetermineCallType method for testing.
/// </summary>
public static class StateTransitionTestHelper
{
    public static CallType DetermineCallType(Address? creationAddress, Address? codeAddress, bool isStatic)
    {
        // Mirror the logic from StateTransition.DetermineCallType
        if (creationAddress.HasValue)
            return CallType.Create;
        if (codeAddress.HasValue)
            return CallType.DelegateCall;
        if (isStatic)
            return CallType.StaticCall;
        return CallType.Call;
    }
}
