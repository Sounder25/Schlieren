using System.Collections.ObjectModel;

namespace Schlieren.Core.Gas;

public sealed record GasRuleCatalogEntry(
    GasRuleId RuleId,
    string Category,
    bool DiagnosticOnly);

/// <summary>
/// Stable catalog of every protocol and diagnostic gas rule in the migration
/// inventory. Formula implementations are registered separately in schedules.
/// </summary>
public static class GasRuleCatalog
{
    private static readonly string[] RuleIdValues =
    [
        "ACCESS.BALANCE",
        "ACCESS.EIP7702_AUTHORITY_WARM",
        "ACCESS.EXTCODEHASH",
        "ACCESS.EXTCODESIZE",
        "ACCESS.INITIAL_WARM_SET",
        "ACCESS.SLOAD",
        "ACCESS.TLOAD",
        "CALL.ACCESS_COST",
        "CALL.CHILD_GAS_RETURN",
        "CALL.DEPTH_LIMIT",
        "CALL.EIP150_FORWARDING",
        "CALL.EIP7702_DELEGATION_ACCESS",
        "CALL.EXCEPTIONAL_CHILD_BURN",
        "CALL.INSUF_BALANCE_EARLY_EXIT",
        "CALL.MEMORY_EXPANSION",
        "CALL.NEW_ACCOUNT_COST",
        "CALL.PRE_EIP150_CHARGE",
        "CALL.PRECOMPILE_DISPATCH",
        "CALL.REFUND_COUNTER_PROPAGATION",
        "CALL.STIPEND_GRANT",
        "CALL.VALUE_TRANSFER_COST",
        "CREATE.BASE",
        "CREATE.CHILD_GAS_RETURN",
        "CREATE.CODE_DEPOSIT",
        "CREATE.CODE_SIZE_LIMIT",
        "CREATE.COLLISION_BURN",
        "CREATE.DEPOSIT_OOG",
        "CREATE.EF_PREFIX_BURN",
        "CREATE.EIP150_FORWARDING",
        "CREATE.INITCODE_SIZE_LIMIT",
        "CREATE.MEMORY_EXPANSION",
        "CREATE.PRE_CHECK_NO_TRANSFER",
        "CREATE.REFUND_COUNTER_PROPAGATION",
        "CREATE.TOP_LEVEL_DEPOSIT",
        "CREATE.WARMING",
        "CREATE2.BASE",
        "DELEGATECALL.FORWARDING",
        "DIAG.BALANCE_DIRECTION",
        "DIAG.BALANCE_TO_GAS",
        "DIAG.FORK_CONTEXT",
        "DIAG.GAS_TREE_ACCESS_CLASS",
        "DIAG.GAS_TREE_EXECUTION",
        "DIAG.GAS_TREE_INTRINSIC",
        "DIAG.GAS_TREE_MEMORY_LEDGER",
        "DIAG.KNOWN_CONSTANT_MATCH",
        "DIAG.REFUND_CAP",
        "HALT.EVM_GAS_CAP",
        "HALT.EXCEPTIONAL_BURN",
        "HALT.OOG_BURN",
        "HALT.OPCODE_ACTIVATION",
        "HALT.REVERT_RETURN",
        "MEMORY.EXPANSION",
        "OP.ADD",
        "OP.ADDMOD",
        "OP.ADDRESS",
        "OP.AND",
        "OP.BASEFEE",
        "OP.BLOBBASEFEE",
        "OP.BLOBHASH",
        "OP.BLOCKHASH",
        "OP.BYTE",
        "OP.CALLDATACOPY",
        "OP.CALLDATALOAD",
        "OP.CALLDATASIZE",
        "OP.CALLER",
        "OP.CALLVALUE",
        "OP.CHAINID",
        "OP.CLZ",
        "OP.CODECOPY",
        "OP.CODESIZE",
        "OP.COINBASE",
        "OP.DIFFICULTY",
        "OP.DIV",
        "OP.DUP1_16",
        "OP.EQ",
        "OP.EXP",
        "OP.EXTCODECOPY",
        "OP.GAS",
        "OP.GASLIMIT",
        "OP.GASPRICE",
        "OP.GT",
        "OP.ISZERO",
        "OP.JUMP",
        "OP.JUMPDEST",
        "OP.JUMPI",
        "OP.KECCAK256",
        "OP.LOG0",
        "OP.LOG1",
        "OP.LOG2",
        "OP.LOG3",
        "OP.LOG4",
        "OP.LT",
        "OP.MCOPY",
        "OP.MLOAD",
        "OP.MOD",
        "OP.MSIZE",
        "OP.MSTORE",
        "OP.MSTORE8",
        "OP.MUL",
        "OP.MULMOD",
        "OP.NOT",
        "OP.NUMBER",
        "OP.OR",
        "OP.ORIGIN",
        "OP.PC",
        "OP.POP",
        "OP.PUSH0",
        "OP.PUSH1_32",
        "OP.RETURN",
        "OP.RETURNDATACOPY",
        "OP.RETURNDATASIZE",
        "OP.REVERT",
        "OP.SAR",
        "OP.SDIV",
        "OP.SELFBALANCE",
        "OP.SGT",
        "OP.SHL",
        "OP.SHR",
        "OP.SIGNEXTEND",
        "OP.SLT",
        "OP.SMOD",
        "OP.STOP",
        "OP.SUB",
        "OP.SWAP1_16",
        "OP.TIMESTAMP",
        "OP.XOR",
        "PRECOMPILE.BLAKE2F",
        "PRECOMPILE.BLS_G1ADD",
        "PRECOMPILE.BLS_G1MSM",
        "PRECOMPILE.BLS_G2ADD",
        "PRECOMPILE.BLS_G2MSM",
        "PRECOMPILE.BLS_MAP_FP_G1",
        "PRECOMPILE.BLS_MAP_FP2_G2",
        "PRECOMPILE.BLS_PAIRING",
        "PRECOMPILE.BN254_ADD",
        "PRECOMPILE.BN254_MUL",
        "PRECOMPILE.BN254_PAIRING",
        "PRECOMPILE.DISPATCH_BUDGET",
        "PRECOMPILE.ECRECOVER",
        "PRECOMPILE.IDENTITY",
        "PRECOMPILE.KZG_POINT_EVAL",
        "PRECOMPILE.MODEXP_EIP198",
        "PRECOMPILE.MODEXP_EIP2565",
        "PRECOMPILE.MODEXP_EIP7883",
        "PRECOMPILE.MODEXP_LENGTH_LIMIT",
        "PRECOMPILE.P256VERIFY",
        "PRECOMPILE.RIPEMD160",
        "PRECOMPILE.SHA256",
        "SELFDESTRUCT.BASE",
        "SELFDESTRUCT.COLD_ACCESS",
        "SELFDESTRUCT.NEW_ACCOUNT",
        "SELFDESTRUCT.REFUND",
        "SETTLE.BLOB_FEE",
        "SETTLE.CALLDATA_FLOOR_POST",
        "SETTLE.COINBASE_CREDIT",
        "SETTLE.EFFECTIVE_GAS_PRICE",
        "SETTLE.REFUND_CAP",
        "SETTLE.SENDER_REFUND",
        "SSTORE.COLD_SURCHARGE",
        "SSTORE.FORMULA_BERLIN",
        "SSTORE.FORMULA_FRONTIER",
        "SSTORE.FORMULA_ISTANBUL",
        "SSTORE.FORMULA_LONDON",
        "SSTORE.REENTRANCY_GUARD",
        "SSTORE.TSTORE",
        "STATICCALL.FORWARDING",
        "TX.ACCESS_LIST_ADDRESS",
        "TX.ACCESS_LIST_STORAGE_KEY",
        "TX.AUTHORIZATION_COST",
        "TX.AUTHORIZATION_REFUND",
        "TX.BASE",
        "TX.CALLDATA_FLOOR",
        "TX.CALLDATA_NONZERO",
        "TX.CALLDATA_ZERO",
        "TX.CREATE_SURCHARGE",
        "TX.INITCODE_WORD",
        "TX.MAX_GAS_LIMIT",
    ];

    private static readonly IReadOnlyDictionary<GasRuleId, GasRuleCatalogEntry> ById;

    static GasRuleCatalog()
    {
        var entries = RuleIdValues
            .Select(value =>
            {
                var id = new GasRuleId(value);
                var separator = value.IndexOf('.');
                var category = separator > 0 ? value[..separator] : value;
                return new GasRuleCatalogEntry(
                    id,
                    category,
                    value.StartsWith("DIAG.", StringComparison.Ordinal));
            })
            .OrderBy(entry => entry.RuleId.Value, StringComparer.Ordinal)
            .ToArray();

        All = new ReadOnlyCollection<GasRuleCatalogEntry>(entries);
        ProtocolRules = new ReadOnlyCollection<GasRuleCatalogEntry>(
            entries.Where(entry => !entry.DiagnosticOnly).ToArray());
        DiagnosticRules = new ReadOnlyCollection<GasRuleCatalogEntry>(
            entries.Where(entry => entry.DiagnosticOnly).ToArray());
        ById = new ReadOnlyDictionary<GasRuleId, GasRuleCatalogEntry>(
            entries.ToDictionary(entry => entry.RuleId));
    }

    public static IReadOnlyList<GasRuleCatalogEntry> All { get; }
    public static IReadOnlyList<GasRuleCatalogEntry> ProtocolRules { get; }
    public static IReadOnlyList<GasRuleCatalogEntry> DiagnosticRules { get; }

    public static GasRuleCatalogEntry GetRequired(GasRuleId id)
    {
        if (ById.TryGetValue(id, out var entry))
            return entry;

        throw new GasScheduleException($"Gas rule '{id}' is not present in the catalog.");
    }
}