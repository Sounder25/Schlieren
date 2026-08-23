using Schlieren.Core.Execution;
using Schlieren.Core.Opcodes;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Tests.Execution;

internal static class ReentrancyJournalFixture
{
    internal static readonly Address Sender = Address.FromHex("0x1000000000000000000000000000000000000001");
    internal static readonly Address Target = Address.FromHex("0xa00000000000000000000000000000000000000a");
    internal static readonly Address Attacker = Address.FromHex("0xb00000000000000000000000000000000000000b");

    internal static void Install(GlobalState state, bool attackerReverts)
    {
        state.SetBalance(Sender, 10_000_000);
        state.SetCode(Target, BuildTargetCode());
        state.SetCode(Attacker, BuildCallerCode(Target, attackerReverts));
    }

    internal static IReadOnlyList<IOpcode> Opcodes() =>
    [
        new OpcodeStop(), new OpcodePush1(), new OpcodePush2(), new OpcodePush20(),
        new OpcodeSload(), new OpcodeSstore(), new OpcodePop(), new OpcodeJumpi(),
        new OpcodeJumpDest(), new OpcodeCall(), new OpcodeRevert(), new OpcodeMstore()
    ];

    private static byte[] BuildTargetCode()
    {
        var code = new List<byte> { 0x60, 0x00, 0x54, 0x60, 0x00, 0x57 };
        code.AddRange([0x60, 0x01, 0x60, 0x00, 0x55]);
        AddCall(code, Attacker);
        code.AddRange([0x60, 0x01, 0x60, 0x01, 0x55, 0x00]);
        code[4] = checked((byte)code.Count);
        code.AddRange([0x5b, 0x60, 0x00, 0x54, 0x50, 0x00]);
        return code.ToArray();
    }

    private static byte[] BuildCallerCode(Address target, bool revert)
    {
        var code = new List<byte>();
        AddCall(code, target);
        code.AddRange(revert
            ? [0x60, 0x00, 0x60, 0x00, 0xfd]
            : [0x00]);
        return code.ToArray();
    }

    private static void AddCall(List<byte> code, Address to)
    {
        code.AddRange([0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x73]);
        code.AddRange(to.Bytes);
        code.AddRange([0x61, 0xc3, 0x50, 0xf1]);
    }
}
