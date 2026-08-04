using Scrutor.Core.Execution;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.EELS.Tests.Harness;
using Scrutor.EELS.Tests.SpecData;
using ExecutionContext = Scrutor.Core.Execution.ExecutionContext;

namespace Scrutor.EELS.Tests.Conformance;

/// <summary>
/// Layer 2: verifies the engine's Cancun opcode gas accounting against the
/// normative EELS gas schedule extracted into <see cref="ForkGasData"/>.
///
/// Each row is a small bytecode program whose expected gas is the sum of the
/// Cancun spec constants it exercises (plus any memory-expansion surcharge).
/// The engine's opcode costs are baked-in literals, so these tests are the
/// bridge that keeps those literals honest against the EELS source of truth.
/// </summary>
public sealed class CancunOpcodeGasConformanceTests
{
    private const string Fork = "cancun";

    private static ulong Spec(string name) =>
        ForkGasData.Get(Fork, name) ??
        throw new Xunit.Sdk.XunitException($"cancun spec data lacks constant '{name}'.");

    private static async Task<ExecutionResult> RunAsync(
        byte[] code,
        Action<ExecutionContext>? configure = null)
    {
        var machine = new EvmMachine(OpcodeCatalog.CreateAll());
        var context = new ExecutionContext
        {
            Code = code,
            GasLimit = 30_000_000
        };
        configure?.Invoke(context);
        return await machine.ExecuteAsync(context);
    }

    public static IEnumerable<object[]> Programs()
    {
        static object[] Case(string name, byte[] code, ulong extraGas, params string[] spec) =>
            new object[] { name, code, extraGas, spec };

        // ---- push / stack ----
        yield return Case("PUSH0 + STOP", [0x5f, 0x00], 0, "OPCODE_PUSH0");
        yield return Case("PUSH1 + POP + STOP", [0x60, 0x01, 0x50, 0x00], 0, "OPCODE_PUSH", "OPCODE_POP");
        yield return Case("PUSH1 + DUP1 + STOP", [0x60, 0x01, 0x80, 0x00], 0, "OPCODE_PUSH", "OPCODE_DUP");
        yield return Case("PUSH1 PUSH1 SWAP1 + STOP", [0x60, 0x01, 0x60, 0x02, 0x90, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SWAP");

        // ---- control flow ----
        yield return Case("JUMPDEST + STOP", [0x5b, 0x00], 0, "OPCODE_JUMPDEST");
        yield return Case("PUSH1 JUMP JUMPDEST STOP", [0x60, 0x03, 0x56, 0x5b, 0x00], 0, "OPCODE_PUSH", "OPCODE_JUMP", "OPCODE_JUMPDEST");
        yield return Case("PUSH1 PUSH1 JUMPI JUMPDEST STOP", [0x60, 0x01, 0x60, 0x05, 0x57, 0x5b, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_JUMPI", "OPCODE_JUMPDEST");

        // ---- arithmetic ----
        yield return Case("ADD", [0x60, 0x02, 0x60, 0x02, 0x01, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_ADD");
        yield return Case("MUL", [0x60, 0x02, 0x60, 0x02, 0x02, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_MUL");
        yield return Case("SUB", [0x60, 0x02, 0x60, 0x02, 0x03, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SUB");
        yield return Case("DIV", [0x60, 0x02, 0x60, 0x02, 0x04, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_DIV");
        yield return Case("SDIV", [0x60, 0x02, 0x60, 0x02, 0x05, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SDIV");
        yield return Case("MOD", [0x60, 0x02, 0x60, 0x02, 0x06, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_MOD");
        yield return Case("SMOD", [0x60, 0x02, 0x60, 0x02, 0x07, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SMOD");
        yield return Case("ADDMOD", [0x60, 0x02, 0x60, 0x02, 0x60, 0x02, 0x08, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_ADDMOD");
        yield return Case("MULMOD", [0x60, 0x02, 0x60, 0x02, 0x60, 0x02, 0x09, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_MULMOD");
        yield return Case("EXP (zero exponent)", [0x60, 0x00, 0x60, 0x02, 0x0a, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_EXP_BASE");
        yield return Case("EXP (one-byte exponent)", [0x60, 0x02, 0x60, 0x02, 0x0a, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_EXP_BASE", "OPCODE_EXP_PER_BYTE");
        yield return Case("SIGNEXTEND", [0x60, 0x02, 0x60, 0x02, 0x0b, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SIGNEXTEND");

        // ---- comparisons / bitwise ----
        yield return Case("LT", [0x60, 0x02, 0x60, 0x02, 0x10, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_LT");
        yield return Case("GT", [0x60, 0x02, 0x60, 0x02, 0x11, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_GT");
        yield return Case("SLT", [0x60, 0x02, 0x60, 0x02, 0x12, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SLT");
        yield return Case("SGT", [0x60, 0x02, 0x60, 0x02, 0x13, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_SGT");
        yield return Case("EQ", [0x60, 0x02, 0x60, 0x02, 0x14, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_EQ");
        yield return Case("ISZERO", [0x60, 0x00, 0x15, 0x00], 0, "OPCODE_PUSH", "OPCODE_ISZERO");
        yield return Case("AND", [0x60, 0x02, 0x60, 0x02, 0x16, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_AND");
        yield return Case("OR", [0x60, 0x02, 0x60, 0x02, 0x17, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_OR");
        yield return Case("XOR", [0x60, 0x02, 0x60, 0x02, 0x18, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_XOR");
        yield return Case("NOT", [0x60, 0x00, 0x19, 0x00], 0, "OPCODE_PUSH", "OPCODE_NOT");
        yield return Case("BYTE", [0x60, 0x02, 0x60, 0x02, 0x1a, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_BYTE");

        // ---- environment ----
        yield return Case("ADDRESS", [0x30, 0x00], 0, "OPCODE_ADDRESS");
        yield return Case("ORIGIN", [0x32, 0x00], 0, "OPCODE_ORIGIN");
        yield return Case("CALLER", [0x33, 0x00], 0, "OPCODE_CALLER");
        yield return Case("CALLVALUE", [0x34, 0x00], 0, "OPCODE_CALLVALUE");
        yield return Case("CALLDATALOAD", [0x60, 0x00, 0x35, 0x00], 0, "OPCODE_PUSH", "OPCODE_CALLDATALOAD");
        yield return Case("CALLDATASIZE", [0x36, 0x00], 0, "OPCODE_CALLDATASIZE");
        yield return Case("CODESIZE", [0x38, 0x00], 0, "OPCODE_CODESIZE");
        yield return Case("GASPRICE", [0x3a, 0x00], 0, "OPCODE_GASPRICE");
        yield return Case("RETURNDATASIZE", [0x3d, 0x00], 0, "OPCODE_RETURNDATASIZE");
        yield return Case("BLOCKHASH", [0x60, 0x00, 0x40, 0x00], 0, "OPCODE_PUSH", "OPCODE_BLOCKHASH");
        yield return Case("COINBASE", [0x41, 0x00], 0, "OPCODE_COINBASE");
        yield return Case("TIMESTAMP", [0x42, 0x00], 0, "OPCODE_TIMESTAMP");
        yield return Case("NUMBER", [0x43, 0x00], 0, "OPCODE_NUMBER");
        yield return Case("PREVRANDAO", [0x44, 0x00], 0, "OPCODE_PREVRANDAO");
        yield return Case("GASLIMIT", [0x45, 0x00], 0, "OPCODE_GASLIMIT");
        yield return Case("CHAINID", [0x46, 0x00], 0, "OPCODE_CHAINID");
        yield return Case("BASEFEE", [0x48, 0x00], 0, "OPCODE_BASEFEE");
        yield return Case("PC", [0x58, 0x00], 0, "OPCODE_PC");
        yield return Case("MSIZE", [0x59, 0x00], 0, "OPCODE_MSIZE");
        yield return Case("GAS", [0x5a, 0x00], 0, "OPCODE_GAS");

        // ---- copy (zero length => no expansion) ----
        yield return Case("CALLDATACOPY (0 len)", [0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x37, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_CALLDATACOPY_BASE");
        yield return Case("CODECOPY (0 len)", [0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x39, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_CODECOPY_BASE");
        yield return Case("RETURNDATACOPY (0 len)", [0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x3e, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_RETURNDATACOPY_BASE");

        // ---- crypto / logging ----
        yield return Case("KECCAK256 (empty)", [0x60, 0x00, 0x60, 0x00, 0x20, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_KECCAK256_BASE");
        yield return Case("LOG0 (empty)", [0x60, 0x00, 0x60, 0x00, 0xa0, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_LOG_BASE");
        yield return Case("LOG1 (1 topic)", [0x60, 0x01, 0x60, 0x00, 0x60, 0x00, 0xa1, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_LOG_BASE", "OPCODE_LOG_TOPIC");

        // ---- memory expansion (one word at offset 0 => 3 gas) ----
        yield return Case("MLOAD (1 word)", [0x60, 0x00, 0x51, 0x00], 3, "OPCODE_PUSH", "OPCODE_MLOAD_BASE");
        yield return Case("MSTORE (1 word)", [0x60, 0x00, 0x60, 0x00, 0x52, 0x00], 3, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_MSTORE_BASE");
        yield return Case("MSTORE8 (1 word)", [0x60, 0x00, 0x60, 0x00, 0x53, 0x00], 3, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_MSTORE8_BASE");
        yield return Case("MCOPY (0 len)", [0x60, 0x00, 0x60, 0x00, 0x60, 0x00, 0x5e, 0x00], 0, "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_PUSH", "OPCODE_MCOPY_BASE");

        // ---- cancun blob ----
        yield return Case("BLOBHASH", [0x60, 0x00, 0x49, 0x00], 0, "OPCODE_PUSH", "OPCODE_BLOBHASH");
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task OpcodeGas_MatchesCancunSpec(
        string name,
        byte[] code,
        ulong extraGas,
        string[] specConstants)
    {
        var result = await RunAsync(code);

        Assert.True(result.IsSuccess, $"{name}: execution failed with {result.Error}");

        var expected = specConstants.Aggregate(0UL, (sum, n) => sum + Spec(n)) + extraGas;
        Assert.Equal(expected, result.GasUsed);
    }

    [Fact]
    public async Task Sload_ColdThenWarm_MatchesEip2929AccessCosts()
    {
        // First SLOAD on a cold slot: COLD_STORAGE_ACCESS (2100).
        // Second SLOAD on the same slot: WARM_ACCESS (100).
        var machine = new EvmMachine(OpcodeCatalog.CreateAll());
        var context = new ExecutionContext
        {
            Code = [0x60, 0x00, 0x54, 0x54, 0x00],
            GasLimit = 30_000_000,
            ContractAddress = Address.FromHex("0x00000000000000000000000000000000000000aa"),
            StorageAddress = Address.FromHex("0x00000000000000000000000000000000000000aa")
        };

        var result = await machine.ExecuteAsync(context);

        Assert.True(result.IsSuccess, result.Error.ToString());
        Assert.Equal(
            Spec("OPCODE_PUSH") + Spec("COLD_STORAGE_ACCESS") + Spec("WARM_ACCESS"),
            result.GasUsed);
    }

    [Fact]
    public async Task IntrinsicGas_MatchesCancunSpec()
    {
        var tx = new Transaction
        {
            To = Address.FromHex("0x0000000000000000000000000000000000001000"),
            Data = [0x00, 0xFF, 0x00, 0xFF],
            GasLimit = 1_000_000
        };

        var expected = Spec("TX_BASE")
                       + 2 * Spec("TX_DATA_PER_ZERO")
                       + 2 * Spec("TX_DATA_PER_NON_ZERO");
        Assert.Equal(expected, IntrinsicGas.Compute(tx));
    }
}
