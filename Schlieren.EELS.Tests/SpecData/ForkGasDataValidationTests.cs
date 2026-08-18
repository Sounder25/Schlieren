using Schlieren.EELS.Tests.SpecData;

namespace Schlieren.EELS.Tests.SpecData;

public sealed class ForkGasDataValidationTests
{
    private static readonly string[] ExpectedForks =
    {
        "amsterdam", "frontier", "homestead", "dao_fork", "tangerine_whistle",
        "spurious_dragon", "byzantium", "constantinople", "istanbul",
        "muir_glacier", "berlin", "london", "arrow_glacier", "paris",
        "gray_glacier", "shanghai", "cancun", "prague", "osaka",
        "bpo1", "bpo2", "bpo3", "bpo4", "bpo5",
    };

    private static ulong Constant(string fork, string name) =>
        ForkGasData.Get(fork, name) is { } c
            ? c
            : throw new Xunit.Sdk.XunitException(
                $"Fork '{fork}' does not define gas constant '{name}'.");

    private static void AssertValue(string fork, string name, ulong expected) =>
        Assert.Equal(expected, Constant(fork, name));

    [Fact]
    public void AllForks_ContainsEverySpecifiedFork()
    {
        var actual = ForkGasData.AllForks.Select(f => f.Fork).ToArray();
        Assert.Equal(ExpectedForks.OrderBy(f => f), actual.OrderBy(f => f));
    }

    [Fact]
    public void EveryFork_HasNonEmptyConstantSet()
    {
        Assert.All(ForkGasData.AllForks, fork =>
        {
            Assert.NotEmpty(fork.Constants);
            Assert.False(string.IsNullOrWhiteSpace(fork.SourceFile));
        });
    }

    [Fact]
    public void EveryConstant_CarriesAuditTrail()
    {
        Assert.All(ForkGasData.AllForks, fork =>
        {
            foreach (var (key, c) in fork.Constants)
            {
                Assert.Equal(key, c.Name);
                Assert.False(string.IsNullOrWhiteSpace(c.Raw), $"no raw expr for {fork.Fork}.{c.Name}");
                Assert.True(c.SourceLine > 0, $"{fork.Fork}.{c.Name} has no source line");
                Assert.Equal(fork.SourceFile, c.SourceFile);
            }
        });
    }

    [Fact]
    public void EveryConstant_IsNonNegative_AndOnlyZeroConstantIsZero()
    {
        Assert.All(ForkGasData.AllForks, fork =>
        {
            Assert.All(fork.Constants.Values, c =>
            {
                if (c.Value > 0)
                {
                    return;
                }

                Assert.True(c.Value == 0 && c.Name == "ZERO",
                    $"{fork.Fork}.{c.Name} resolves to 0 but is not the ZERO constant.");
            });
        });
    }

    [Theory]
    [InlineData("frontier", "TX_BASE", 21000UL)]             // Ethereum Yellow Paper, 7.1
    [InlineData("frontier", "TX_DATA_PER_ZERO", 4UL)]        // YP 7.1
    [InlineData("frontier", "TX_DATA_PER_NON_ZERO", 68UL)]   // YP 7.1 (68 -> 16 in EIP-2028)
    [InlineData("spurious_dragon", "TX_CREATE", 32000UL)]    // EIP-2 / YP 7.1
    [InlineData("frontier", "CALL_STIPEND", 2300UL)]         // YP Appendix G
    [InlineData("frontier", "CALL_VALUE", 9000UL)]           // YP Appendix G, G_callvalue
    [InlineData("frontier", "NEW_ACCOUNT", 25000UL)]         // YP Appendix G, G_newaccount
    [InlineData("frontier", "SLOAD", 50UL)]                  // YP Appendix G, G_sload
    [InlineData("frontier", "STORAGE_SET", 20000UL)]         // YP Appendix G, G_sset
    [InlineData("frontier", "OPCODE_BALANCE", 20UL)]         // YP Appendix G, G_balance
    [InlineData("frontier", "OPCODE_CALL_BASE", 40UL)]       // YP Appendix G, G_call
    [InlineData("frontier", "OPCODE_EXP_BASE", 10UL)]        // YP Appendix G, G_exp
    [InlineData("frontier", "OPCODE_EXP_PER_BYTE", 10UL)]    // YP Appendix G, G_expbyte (10 -> 50 in EIP-160)
    [InlineData("frontier", "OPCODE_KECCAK256_BASE", 30UL)]  // YP Appendix G, G_sha3
    [InlineData("frontier", "OPCODE_KECCAK256_PER_WORD", 6UL)]
    [InlineData("frontier", "OPCODE_COPY_PER_WORD", 3UL)]    // YP Appendix G, G_copy
    [InlineData("frontier", "OPCODE_LOG_BASE", 375UL)]       // YP Appendix G, G_log
    [InlineData("frontier", "OPCODE_LOG_TOPIC", 375UL)]      // YP Appendix G, G_logtopic
    [InlineData("frontier", "OPCODE_LOG_DATA_PER_BYTE", 8UL)]
    [InlineData("frontier", "MEMORY_PER_WORD", 3UL)]         // YP Appendix G, G_memory
    [InlineData("frontier", "CODE_DEPOSIT_PER_BYTE", 200UL)] // YP Appendix G, G_codedeposit
    [InlineData("frontier", "LIMIT_ADJUSTMENT_FACTOR", 1024UL)] // YP 4.3
    [InlineData("frontier", "LIMIT_MINIMUM", 5000UL)]        // YP 4.3
    [InlineData("frontier", "PRECOMPILE_ECRECOVER", 3000UL)] // YP Appendix E
    [InlineData("frontier", "PRECOMPILE_SHA256_BASE", 60UL)] // YP Appendix E
    [InlineData("frontier", "PRECOMPILE_SHA256_PER_WORD", 12UL)]
    [InlineData("frontier", "PRECOMPILE_RIPEMD160_BASE", 600UL)]
    [InlineData("frontier", "PRECOMPILE_RIPEMD160_PER_WORD", 120UL)]
    [InlineData("frontier", "PRECOMPILE_IDENTITY_BASE", 15UL)]
    [InlineData("frontier", "PRECOMPILE_IDENTITY_PER_WORD", 3UL)]
    public void Fork_PinsFrontierCosts(string fork, string name, ulong expected) =>
        AssertValue(fork, name, expected);

    [Theory]
    [InlineData("tangerine_whistle", "SLOAD", 200UL)]         // EIP-150 (G_sload 50 -> 200)
    [InlineData("tangerine_whistle", "OPCODE_BALANCE", 400UL)] // EIP-150 (20 -> 400)
    [InlineData("tangerine_whistle", "OPCODE_CALL_BASE", 700UL)] // EIP-150 (40 -> 700)
    [InlineData("spurious_dragon", "OPCODE_EXP_PER_BYTE", 50UL)] // EIP-160 (10 -> 50)
    [InlineData("spurious_dragon", "TX_CREATE", 32000UL)]     // EIP-2 contract creation cost
    [InlineData("istanbul", "TX_DATA_PER_NON_ZERO", 16UL)]    // EIP-2028 (68 -> 16)
    [InlineData("istanbul", "SLOAD", 800UL)]                  // EIP-2200
    [InlineData("istanbul", "OPCODE_BALANCE", 700UL)]         // EIP-1884 (400 -> 700)
    [InlineData("istanbul", "PRECOMPILE_ECADD", 150UL)]       // EIP-1108
    [InlineData("istanbul", "PRECOMPILE_ECMUL", 6000UL)]      // EIP-1108
    [InlineData("istanbul", "PRECOMPILE_ECPAIRING_BASE", 45000UL)]   // EIP-1108
    [InlineData("istanbul", "PRECOMPILE_ECPAIRING_PER_POINT", 34000UL)] // EIP-1108
    [InlineData("istanbul", "PRECOMPILE_BLAKE2F_PER_ROUND", 1UL)] // EIP-152
    public void Fork_PinsForkTransitionCosts(string fork, string name, ulong expected) =>
        AssertValue(fork, name, expected);

    [Theory]
    [InlineData("berlin", "WARM_ACCESS", 100UL)]                    // EIP-2929
    [InlineData("berlin", "COLD_ACCOUNT_ACCESS", 2600UL)]           // EIP-2929
    [InlineData("berlin", "COLD_STORAGE_ACCESS", 2100UL)]           // EIP-2929
    [InlineData("berlin", "COLD_STORAGE_WRITE", 5000UL)]            // EIP-2929
    [InlineData("berlin", "TX_ACCESS_LIST_ADDRESS", 2400UL)]        // EIP-2930
    [InlineData("berlin", "TX_ACCESS_LIST_STORAGE_KEY", 1900UL)]    // EIP-2930
    [InlineData("berlin", "CALL_VALUE", 9000UL)]
    [InlineData("london", "REFUND_STORAGE_CLEAR", 4800UL)]          // EIP-3529
    [InlineData("shanghai", "OPCODE_PUSH0", 2UL)]                   // EIP-3855
    [InlineData("shanghai", "CODE_INIT_PER_WORD", 2UL)]             // EIP-3860
    public void Fork_PinsPostEip2929Costs(string fork, string name, ulong expected) =>
        AssertValue(fork, name, expected);

    [Theory]
    [InlineData("cancun", "PER_BLOB", 131072UL)]                    // EIP-4844, 2^17
    [InlineData("cancun", "BLOB_TARGET_GAS_PER_BLOCK", 393216UL)]   // EIP-4844, 3 * 2^17
    [InlineData("cancun", "BLOB_MIN_GASPRICE", 1UL)]                // EIP-4844
    [InlineData("cancun", "BLOB_BASE_FEE_UPDATE_FRACTION", 3338477UL)] // EIP-4844
    [InlineData("cancun", "OPCODE_MCOPY_BASE", 3UL)]                // EIP-5656
    [InlineData("cancun", "OPCODE_BLOBHASH", 3UL)]                  // EIP-4844
    [InlineData("cancun", "PRECOMPILE_POINT_EVALUATION", 50000UL)]  // EIP-4844
    public void Fork_PinsCancunCosts(string fork, string name, ulong expected) =>
        AssertValue(fork, name, expected);

    [Fact]
    public void ExpPerByte_IsTenBeforeSpuriousDragon_AndFiftyAfter()
    {
        foreach (var pre in new[] { "frontier", "homestead", "dao_fork", "tangerine_whistle" })
        {
            AssertValue(pre, "OPCODE_EXP_PER_BYTE", 10UL);
        }

        foreach (var post in new[] { "spurious_dragon", "byzantium", "istanbul", "berlin", "cancun", "prague", "osaka" })
        {
            AssertValue(post, "OPCODE_EXP_PER_BYTE", 50UL);
        }
    }

    [Fact]
    public void RefundStorageClear_DropsTo4800AtLondon_Eip3529()
    {
        foreach (var pre in new[] { "frontier", "homestead", "berlin" })
        {
            AssertValue(pre, "REFUND_STORAGE_CLEAR", 15000UL);
        }

        foreach (var post in new[] { "london", "arrow_glacier", "paris", "shanghai", "cancun" })
        {
            AssertValue(post, "REFUND_STORAGE_CLEAR", 4800UL);
        }
    }

    [Fact]
    public void RefundSelfDestruct_IsRemovedAtLondon_Eip3529()
    {
        AssertValue("berlin", "REFUND_SELF_DESTRUCT", 24000UL);

        foreach (var fork in new[] { "london", "paris", "cancun", "prague", "osaka" })
        {
            Assert.Null(ForkGasData.Get(fork, "REFUND_SELF_DESTRUCT"));
        }
    }

    [Fact]
    public void StoredGas_MatchesSpecExpressionForReferencedConstants()
    {
        // BLOB_TARGET_GAS_PER_BLOCK is authored in EELS as
        // PER_BLOB * BLOB_SCHEDULE_TARGET; for Cancun the schedule target is 3.
        Assert.Equal(
            Constant("cancun", "PER_BLOB") * 3UL,
            Constant("cancun", "BLOB_TARGET_GAS_PER_BLOCK"));

        // amsterdam expresses REFUND_STORAGE_CLEAR as
        // int((STORAGE_WRITE + COLD_STORAGE_ACCESS) * 4800 // 5000);
        // STORAGE_WRITE=10000, COLD_STORAGE_ACCESS=3000 -> 12480.
        Assert.Equal(12480UL, Constant("amsterdam", "REFUND_STORAGE_CLEAR"));
        Assert.Equal(10000UL, Constant("amsterdam", "STORAGE_WRITE"));
        Assert.Equal(3000UL, Constant("amsterdam", "COLD_STORAGE_ACCESS"));
    }

    [Fact]
    public void ForkData_IsStablePerQuery()
    {
        var first = ForkGasData.GetFork("cancun");
        var second = ForkGasData.GetFork("cancun");
        Assert.Same(first, second);
        Assert.Equal(Constant("cancun", "TX_BASE"), ForkGasData.Get("cancun", "TX_BASE"));
        Assert.Null(ForkGasData.Get("cancun", "DOES_NOT_EXIST"));
    }
}
