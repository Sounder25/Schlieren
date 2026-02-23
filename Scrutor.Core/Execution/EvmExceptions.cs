namespace Scrutor.Core.Execution;

public sealed class EvmStackOverflowException : Exception
{
    public EvmStackOverflowException(string message) : base(message) { }
}

public sealed class EvmStackUnderflowException : Exception
{
    public EvmStackUnderflowException(string message) : base(message) { }
}

public sealed class EvmOutOfGasException : Exception
{
    public EvmOutOfGasException(string message) : base(message) { }
}

public sealed class EvmInvalidOpcodeException : Exception
{
    public EvmInvalidOpcodeException(byte opcode) : base($"Invalid opcode: 0x{opcode:X2}") { }
}

public sealed class EvmBadJumpDestinationException : Exception
{
    public EvmBadJumpDestinationException(string message) : base(message) { }
}
