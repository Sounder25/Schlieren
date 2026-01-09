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
            
            if (dto == null) return;

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

            _logger.LogInformation("State loaded. Head block: {Number}", maxBlock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state.");
        }
    }
}
