using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrutor.Core.Configuration;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Opcodes;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.RPC.Handlers;
using Scrutor.RPC.Models;
using Scrutor.RPC.Server;
using Xunit;

namespace Scrutor.Tests.State;

/// <summary>
/// Regression tests for storage write persistence across the mining pipeline.
///
/// Critical invariant: a successful SSTORE in a mined transaction must be
/// visible to eth_getStorageAt and eth_call in subsequent blocks.
/// A broken overlay-commit path would cause the value to read back as zero.
/// </summary>
public class StoragePersistenceTests
{
    // PUSH1 0x01 / PUSH1 0x00 / SSTORE / STOP
    // Unconditionally sets slot 0 = 1 then halts.
    private static readonly byte[] SetSlot0To1 = { 0x60, 0x01, 0x60, 0x00, 0x55, 0x00 };

    // PUSH1 0x00 / SLOAD / PUSH1 0x00 / MSTORE / PUSH1 0x20 / PUSH1 0x00 / RETURN
    // Returns uint256 at slot 0 as 32-byte ABI word.
    private static readonly byte[] ReadSlot0 =
        { 0x60, 0x00, 0x54, 0x60, 0x00, 0x52, 0x60, 0x20, 0x60, 0x00, 0xf3 };

    // PUSH1 0x01 / PUSH1 0x00 / SSTORE / PUSH1 0x00 / PUSH1 0x00 / REVERT
    // Writes slot 0 = 1 then reverts — state must not persist.
    private static readonly byte[] WriteAndRevert =
        { 0x60, 0x01, 0x60, 0x00, 0x55, 0x60, 0x00, 0x60, 0x00, 0xfd };

    // ── Fixture ───────────────────────────────────────────────────────────────

    private static (GlobalState gs, ChainState cs, StateTransition st) BuildCore()
    {
        var gs = new GlobalState();
        var cs = new ChainState(31337, new BlockStore());

        // Reflect all IOpcode implementations exactly as production DI does.
        var opcodeInstances = typeof(IOpcode).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IOpcode).IsAssignableFrom(t))
            .Select(t => (IOpcode)Activator.CreateInstance(t)!)
            .ToList();

        var st = new StateTransition(new EvmMachine(opcodeInstances));
        return (gs, cs, st);
    }

    private static EthHandlers BuildHandlers(
        GlobalState gs, ChainState cs, IStateTransition st)
    {
        return new EthHandlers(
            gs,
            new Mock<ITxMempool>().Object,
            cs,
            st,
            new Mock<IMiningService>().Object,
            new ImpersonationService(),
            new AccountManager(),
            new NodeConfiguration { Accounts = 0, ChainId = 31337 },
            new Mock<IStateManager>().Object);
    }

    private static Transaction InternalTx(Address sender, Address? to, byte[] data = null!, ulong nonce = 0) =>
        new()
        {
            From  = sender,
            To    = to,
            Nonce = nonce,
            Data  = data ?? Array.Empty<byte>(),
            GasLimit = 200_000,
            GasPrice = 1,
            Authorization = TransactionAuthorization.Internal,
        };

    private static BlockContext NextBlock(ChainState cs) => new()
    {
        ChainId = 31337,
        Number  = cs.CurrentBlock.Number + 1,
        Timestamp = 1_700_000_000 + cs.CurrentBlock.Number,
        GasLimit = 30_000_000,
        BaseFeePerGas = 0,
        Coinbase = Address.Zero,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Successful_SSTORE_Persists_To_GlobalState_After_Commit()
    {
        var (gs, cs, st) = BuildCore();
        var contract = Address.FromHex("0xDeAdBeEf00000000000000000000000000000001");
        gs.SetCode(contract, SetSlot0To1);

        var sender = Address.FromHex("0x000000000000000000000000000000000000CAFE");
        gs.SetBalance(sender, BigInteger.Pow(10, 18));

        var before = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.Equal(BigInteger.Zero, before);

        var result = await st.ApplyTransactionAsync(
            InternalTx(sender, contract), gs, NextBlock(cs), commit: true);
        Assert.True(result.IsSuccess, $"tx failed: {result.Error}");

        var after = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.Equal(BigInteger.One, after);
    }

    [Fact]
    public async Task Reverted_Transaction_Does_Not_Persist_Storage()
    {
        var (gs, cs, st) = BuildCore();
        var contract = Address.FromHex("0xDeAdBeEf00000000000000000000000000000002");
        gs.SetCode(contract, WriteAndRevert);

        var sender = Address.FromHex("0x000000000000000000000000000000000000DEAD");
        gs.SetBalance(sender, BigInteger.Pow(10, 18));

        var result = await st.ApplyTransactionAsync(
            InternalTx(sender, contract), gs, NextBlock(cs), commit: true);
        Assert.False(result.IsSuccess, "expected revert, got success");

        var slot0 = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.Equal(BigInteger.Zero, slot0);
    }

    [Fact]
    public async Task Dry_Run_Commit_False_Does_Not_Persist_Storage()
    {
        var (gs, cs, st) = BuildCore();
        var contract = Address.FromHex("0xDeAdBeEf00000000000000000000000000000003");
        gs.SetCode(contract, SetSlot0To1);

        var sender = Address.FromHex("0x000000000000000000000000000000000000B00B");
        gs.SetBalance(sender, BigInteger.Pow(10, 18));

        // commit=false is the eth_estimateGas / eth_call probe path
        var result = await st.ApplyTransactionAsync(
            InternalTx(sender, contract), gs, NextBlock(cs), commit: false);
        Assert.True(result.IsSuccess, $"dry-run failed: {result.Error}");

        var slot0 = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.True(slot0 == BigInteger.Zero, $"commit=false must not mutate canonical state — slot0 was {slot0}");
    }

    [Fact]
    public async Task EthCall_Sees_Storage_Written_By_Prior_Committed_Transaction()
    {
        var (gs, cs, st) = BuildCore();

        // Single contract: calling it writes slot 0 = 1 and then reads + returns it.
        // Bytecode: PUSH1 0x01 / PUSH1 0x00 / SSTORE / PUSH1 0x00 / SLOAD /
        //           PUSH1 0x00 / MSTORE / PUSH1 0x20 / PUSH1 0x00 / RETURN
        // (slot 0 = 1, then return slot 0 as 32-byte ABI word)
        byte[] writeReadContract =
        {
            0x60, 0x01, // PUSH1 0x01
            0x60, 0x00, // PUSH1 0x00
            0x55,       // SSTORE  slot0 ← 1
            0x60, 0x00, // PUSH1 0x00
            0x54,       // SLOAD   → stack: slot0 value
            0x60, 0x00, // PUSH1 0x00
            0x52,       // MSTORE  mem[0] = slot0
            0x60, 0x20, // PUSH1 0x20
            0x60, 0x00, // PUSH1 0x00
            0xf3        // RETURN  32 bytes from mem[0]
        };

        var contract = Address.FromHex("0xDeAdBeEf00000000000000000000000000000004");
        gs.SetCode(contract, writeReadContract);

        var sender = Address.FromHex("0x000000000000000000000000000000000000BABE");
        gs.SetBalance(sender, BigInteger.Pow(10, 18));

        // Mine a write tx so slot 0 = 1 is in canonical state
        var writeResult = await st.ApplyTransactionAsync(
            InternalTx(sender, contract), gs, NextBlock(cs), commit: true);
        Assert.True(writeResult.IsSuccess, $"write tx failed: {writeResult.Error}");

        // Advance canonical head so "latest" resolves to this block
        cs.UpdateHead(new Block { Number = 1, Hash = "0x" + new string('a', 64) });

        // Direct canonical state read must show 1
        var directSlot = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.Equal(BigInteger.One, directSlot);

        // eth_call against same contract (commit=false): SLOAD must read 1 from parent state
        var handlers = BuildHandlers(gs, cs, st);
        var callParam = JsonSerializer.Deserialize<JsonElement>(
            $"{{\"to\":\"{contract}\",\"gas\":\"0x30000\"}}");

        var hexResult = await handlers.HandleEthCall(
            new object[] { callParam, "latest" });

        Assert.NotNull(hexResult);
        var raw = (hexResult!.StartsWith("0x") ? hexResult[2..] : hexResult).TrimStart('0');
        var value = raw.Length == 0
            ? BigInteger.Zero
            : BigInteger.Parse("0" + raw, System.Globalization.NumberStyles.HexNumber);
        Assert.Equal(BigInteger.One, value);
    }

    [Fact]
    public async Task Storage_Persists_Across_Multiple_Sequential_Blocks()
    {
        var (gs, cs, st) = BuildCore();
        var contract = Address.FromHex("0xDeAdBeEf00000000000000000000000000000006");
        gs.SetCode(contract, SetSlot0To1);

        var sender = Address.FromHex("0x000000000000000000000000000000000000F00D");
        gs.SetBalance(sender, BigInteger.Pow(10, 18));

        // Mine once
        var tx = InternalTx(sender, contract);
        var r1 = await st.ApplyTransactionAsync(tx, gs, NextBlock(cs), commit: true);
        Assert.True(r1.IsSuccess, $"block 1 tx failed: {r1.Error}");
        cs.UpdateHead(new Block { Number = 1, Hash = "0x" + new string('1', 64) });

        // Verify slot 0 = 1 after block 1
        var after1 = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.Equal(BigInteger.One, after1);

        // Mine an unrelated block 2 (no writes)
        var noopContract = Address.FromHex("0xDeAdBeEf00000000000000000000000000000007");
        gs.SetCode(noopContract, new byte[] { 0x00 }); // STOP
        var tx2 = InternalTx(sender, noopContract, nonce: 1);
        var r2 = await st.ApplyTransactionAsync(tx2, gs, NextBlock(cs), commit: true);
        Assert.True(r2.IsSuccess, $"block 2 tx failed: {r2.Error}");
        cs.UpdateHead(new Block { Number = 2, Hash = "0x" + new string('2', 64) });

        // Storage must still be 1 after block 2
        var after2 = await gs.GetStorageAtAsync(contract, BigInteger.Zero);
        Assert.True(after2 == BigInteger.One, $"storage was lost after mining a second block — got {after2}");
    }
}
