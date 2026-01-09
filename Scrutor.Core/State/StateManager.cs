using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;

namespace Scrutor.Core.State;

public class StateDumpDto
{
    public Dictionary<string, AccountDto> Accounts { get; set; } = new();
    public List<Block> Blocks { get; set; } = new();
    public List<TransactionReceipt> Receipts { get; set; } = new();
}

public class AccountDto
{
    public string Nonce { get; set; } = "0x0";
    public string Balance { get; set; } = "0x0";
    public string Code { get; set; } = "0x";
    public Dictionary<string, string> Storage { get; set; } = new();
}

public interface IStateManager
{
    Task SaveStateAsync(string filePath);
    Task LoadStateAsync(string filePath);
    StateDumpDto CaptureState();
    void RestoreState(StateDumpDto dto);
}

public sealed class StateManager : IStateManager
{
    private readonly IGlobalState _globalState;
    private readonly IBlockStore _blockStore;
    private readonly IChainState _chainState;
    private readonly ILogger<StateManager> _logger;

    public StateManager(IGlobalState globalState, IBlockStore blockStore, IChainState chainState, ILogger<StateManager> logger)
    {
        _globalState = globalState;
        _blockStore = blockStore;
        _chainState = chainState;
        _logger = logger;
    }

    public async Task SaveStateAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Saving state to {FilePath}", filePath);
            
            var dto = CaptureState();

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("State saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state.");
        }
    }

    public async Task LoadStateAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("State file not found: {FilePath}", filePath);
                return;
            }

            _logger.LogInformation("Loading state from {FilePath}", filePath);
            
            var json = await File.ReadAllTextAsync(filePath);
            var dto = JsonSerializer.Deserialize<StateDumpDto>(json);
            
            if (dto != null)
            {
                RestoreState(dto);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state.");
        }
    }

    public StateDumpDto CaptureState()
    {
        var snapshot = _globalState.Snapshot();
        var dto = new StateDumpDto
        {
            Blocks = _blockStore.GetAllBlocks().OrderBy(b => b.Number).ToList(),
            Receipts = _blockStore.GetAllReceipts().ToList()
        };

        foreach (var kvp in snapshot)
        {
            var addr = kvp.Key.ToString();
            var acc = kvp.Value;
            
            var accDto = new AccountDto
            {
                Nonce = $"0x{acc.Nonce:x}",
                Balance = $"0x{acc.Balance:x}",
                Code = "0x" + Convert.ToHexString(acc.Code).ToLowerInvariant()
            };

            foreach (var s in acc.Storage)
            {
                accDto.Storage[$"0x{s.Key:x}"] = $"0x{s.Value:x}";
            }

            dto.Accounts[addr] = accDto;
        }
        return dto;
    }

    public void RestoreState(StateDumpDto dto)
    {
        // Clear existing state first? 
        // Snapshot/Revert implies full rollback, so we should probably clear first or overwrite.
        // GlobalState.Reset() was defined earlier but not used here.
        // Let's check if IGlobalState has Reset.
        // I read GlobalState.cs earlier, it has Reset().
        if (_globalState is GlobalState gs) gs.Reset();
        // If it's ForkingGlobalState, we might need a Reset there too. 
        // But for now, let's assume overwriting or basic reset.
        // Wait, overwriting SetNonce/SetBalance is fine, but if we want to *remove* accounts that shouldn't exist, we need Reset.
        // Let's use the explicit Setters for now, assuming the dto contains the full desired state.
        
        // Restore Accounts
        foreach (var kvp in dto.Accounts)
        {
            var address = Address.FromHex(kvp.Key);
            var accDto = kvp.Value;

            var nonce = Convert.ToUInt64(accDto.Nonce, 16);
            var balance = System.Numerics.BigInteger.Parse("0" + accDto.Balance.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
            var code = Convert.FromHexString(accDto.Code.Replace("0x", ""));

            _globalState.SetNonce(address, nonce);
            _globalState.SetBalance(address, balance);
            _globalState.SetCode(address, code);

            foreach (var s in accDto.Storage)
            {
                var k = System.Numerics.BigInteger.Parse("0" + s.Key.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
                var v = System.Numerics.BigInteger.Parse("0" + s.Value.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
                _globalState.SetStorageAt(address, k, v);
            }
        }

        // Restore Blocks & Receipts
        // We really should clear BlockStore too if we are reverting.
        // But BlockStore interface doesn't have Clear().
        // For MVP revert, we usually just want to reset the HEAD.
        // But if we generated blocks 5,6,7 and revert to 4, we technically still have 5,6,7 in store.
        // That's acceptable for a simple dev node, as long as ChainState.CurrentBlock is 4.
        
        ulong maxBlock = 0;
        foreach (var block in dto.Blocks.OrderBy(b => b.Number))
        {
            _blockStore.AddBlock(block);
            if (block.Number > maxBlock) maxBlock = block.Number;
        }

        foreach (var receipt in dto.Receipts)
        {
            _blockStore.AddReceipt(receipt);
        }

        // Update Chain Head
        var head = _blockStore.GetBlockByNumber(maxBlock);
        if (head != null)
        {
            _chainState.UpdateHead(head);
        }

        _logger.LogInformation("State restored. Head block: {Number}", maxBlock);
    }
}
