namespace Schlieren.UI.Services;

/// <summary>
/// Static EVM bytecode disassembler. Walks raw bytes and produces one entry
/// per opcode regardless of execution path.
/// </summary>
public static class BytecodeDisassembler
{
    public readonly record struct DisassembledOp(int Pc, string Name, int Size, string Description);

    /// <summary>
    /// Disassemble the full bytecode into a list of opcodes with PC offsets.
    /// </summary>
    public static List<DisassembledOp> Disassemble(byte[] bytecode)
    {
        var result = new List<DisassembledOp>();
        var pc = 0;

        while (pc < bytecode.Length)
        {
            var b = bytecode[pc];
            var (name, pushBytes) = Decode(b);
            var size = 1 + pushBytes;
            var desc = BytecodeExecutionService.DescribeOpcode(name);
            result.Add(new DisassembledOp(pc, name, size, desc));
            pc += size;
        }

        return result;
    }

    /// <summary>
    /// Parse a hex string (0x-prefixed or bare) into a byte array.
    /// </summary>
    public static byte[] ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();

        var s = hex.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];

        if (s.Length % 2 != 0)
            s = "0" + s;

        var bytes = new byte[s.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);

        return bytes;
    }

    private static (string Name, int PushBytes) Decode(byte opcode) => opcode switch
    {
        0x00 => ("STOP", 0),
        0x01 => ("ADD", 0),
        0x02 => ("MUL", 0),
        0x03 => ("SUB", 0),
        0x04 => ("DIV", 0),
        0x05 => ("SDIV", 0),
        0x06 => ("MOD", 0),
        0x07 => ("SMOD", 0),
        0x08 => ("ADDMOD", 0),
        0x09 => ("MULMOD", 0),
        0x0A => ("EXP", 0),
        0x0B => ("SIGNEXTEND", 0),
        0x10 => ("LT", 0),
        0x11 => ("GT", 0),
        0x12 => ("SLT", 0),
        0x13 => ("SGT", 0),
        0x14 => ("EQ", 0),
        0x15 => ("ISZERO", 0),
        0x16 => ("AND", 0),
        0x17 => ("OR", 0),
        0x18 => ("XOR", 0),
        0x19 => ("NOT", 0),
        0x1A => ("BYTE", 0),
        0x1B => ("SHL", 0),
        0x1C => ("SHR", 0),
        0x1D => ("SAR", 0),
        0x20 => ("KECCAK256", 0),
        0x30 => ("ADDRESS", 0),
        0x31 => ("BALANCE", 0),
        0x32 => ("ORIGIN", 0),
        0x33 => ("CALLER", 0),
        0x34 => ("CALLVALUE", 0),
        0x35 => ("CALLDATALOAD", 0),
        0x36 => ("CALLDATASIZE", 0),
        0x37 => ("CALLDATACOPY", 0),
        0x38 => ("CODESIZE", 0),
        0x39 => ("CODECOPY", 0),
        0x3A => ("GASPRICE", 0),
        0x3B => ("EXTCODESIZE", 0),
        0x3C => ("EXTCODECOPY", 0),
        0x3D => ("RETURNDATASIZE", 0),
        0x3E => ("RETURNDATACOPY", 0),
        0x3F => ("EXTCODEHASH", 0),
        0x40 => ("BLOCKHASH", 0),
        0x41 => ("COINBASE", 0),
        0x42 => ("TIMESTAMP", 0),
        0x43 => ("NUMBER", 0),
        0x44 => ("PREVRANDAO", 0),
        0x45 => ("GASLIMIT", 0),
        0x46 => ("CHAINID", 0),
        0x47 => ("SELFBALANCE", 0),
        0x48 => ("BASEFEE", 0),
        0x49 => ("BLOBHASH", 0),
        0x4A => ("BLOBBASEFEE", 0),
        0x50 => ("POP", 0),
        0x51 => ("MLOAD", 0),
        0x52 => ("MSTORE", 0),
        0x53 => ("MSTORE8", 0),
        0x54 => ("SLOAD", 0),
        0x55 => ("SSTORE", 0),
        0x56 => ("JUMP", 0),
        0x57 => ("JUMPI", 0),
        0x58 => ("PC", 0),
        0x59 => ("MSIZE", 0),
        0x5A => ("GAS", 0),
        0x5B => ("JUMPDEST", 0),
        0x5C => ("TLOAD", 0),
        0x5D => ("TSTORE", 0),
        0x5E => ("MCOPY", 0),
        0x5F => ("PUSH0", 0),
        // PUSH1..PUSH32
        >= 0x60 and <= 0x7F => ($"PUSH{opcode - 0x5F}", opcode - 0x5F),
        // DUP1..DUP16
        >= 0x80 and <= 0x8F => ($"DUP{opcode - 0x7F}", 0),
        // SWAP1..SWAP16
        >= 0x90 and <= 0x9F => ($"SWAP{opcode - 0x8F}", 0),
        // LOG0..LOG4
        >= 0xA0 and <= 0xA4 => ($"LOG{opcode - 0xA0}", 0),
        0xF0 => ("CREATE", 0),
        0xF1 => ("CALL", 0),
        0xF2 => ("CALLCODE", 0),
        0xF3 => ("RETURN", 0),
        0xF4 => ("DELEGATECALL", 0),
        0xF5 => ("CREATE2", 0),
        0xFA => ("STATICCALL", 0),
        0xFD => ("REVERT", 0),
        0xFE => ("INVALID", 0),
        0xFF => ("SELFDESTRUCT", 0),
        _ => ($"0x{opcode:X2}", 0)
    };
}
