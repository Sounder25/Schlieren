using System.Numerics;
using Moq;
using Scrutor.Core.Configuration;
using Scrutor.Core.Execution;
using Scrutor.Core.Models;
using Scrutor.Core.Primitives;
using Scrutor.Core.State;
using Scrutor.RPC;
using Scrutor.RPC.Handlers;
using Xunit;

namespace Scrutor.Tests.RPC;

public class ImpersonationTests
{
    private readonly EthHandlers _handlers;
    private readonly IGlobalState _globalState;
    private readonly ITxMempool _mempool;
    private readonly IChainState _chainState;
    private readonly IImpersonationService _impersonation;
    private readonly IAccountManager _accountManager;

    public ImpersonationTests()
    {
        _globalState = new GlobalState();
        _mempool = new TxMempool();
        _chainState = new ChainState(1, new BlockStore());
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        var miningService = new Mock<IMiningService>();
        _impersonation = new ImpersonationService();
        _accountManager = new AccountManager();
        _handlers = new EthHandlers(_globalState, _mempool, _chainState, stateTransition, miningService.Object, _impersonation, _accountManager, new NodeConfiguration { Accounts = 0 });
    }

    [Fact]
    public async Task SendTransaction_RejectsIfNotImpersonated()
    {
        // Arrange
        var address = Address.FromHex("0x1234567890123456789012345678901234567890");
        var txJson = "{\"from\": \"" + address.ToString() + "\", \"to\": \"0x0000000000000000000000000000000000000001\", \"value\": \"0x100\"}";
        var parameters = new object[] { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(txJson) };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<RpcException>(() => _handlers.HandleSendTransaction(parameters));
        Assert.Contains("not impersonated or unlocked", ex.Message);
    }

    [Fact]
    public async Task SendTransaction_SucceedsIfImpersonated()
    {
        // Arrange
        var address = Address.FromHex("0x1234567890123456789012345678901234567890");
        _impersonation.Impersonate(address);
        
        var txJson = "{\"from\": \"" + address.ToString() + "\", \"to\": \"0x0000000000000000000000000000000000000001\", \"value\": \"0x100\"}";
        var parameters = new object[] { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(txJson) };

        // Act
        var hash = await _handlers.HandleSendTransaction(parameters);

        // Assert
        Assert.NotNull(hash);
        Assert.Equal(1, _mempool.Count);
        var tx = _mempool.PopBest();
        Assert.Equal(TransactionAuthorization.Impersonated, tx.Authorization);
        Assert.Equal(address, tx.From);
    }

    [Fact]
    public async Task SendRawTransaction_AlwaysRequiresSignature_EvenIfImpersonated()
    {
        // Arrange
        var address = Address.FromHex("0x71562b71999873DB5b280dFEEf2c2015d7AF40c5");
        _impersonation.Impersonate(address);

        // Invalid signature (bad r/s/v for this hash)
        // [nonce, gasPrice, gasLimit, to, value, data, v, r, s]
        var rawTx = Nethereum.RLP.RLP.EncodeList(
            Nethereum.RLP.RLP.EncodeElement(CryptoUtils.ToBytesForRLP(0)),
            Nethereum.RLP.RLP.EncodeElement(CryptoUtils.ToBytesForRLP(1000000000)),
            Nethereum.RLP.RLP.EncodeElement(CryptoUtils.ToBytesForRLP(21000)),
            Nethereum.RLP.RLP.EncodeElement(new byte[20]),
            Nethereum.RLP.RLP.EncodeElement(CryptoUtils.ToBytesForRLP(0)),
            Nethereum.RLP.RLP.EncodeElement(Array.Empty<byte>()),
            Nethereum.RLP.RLP.EncodeElement(CryptoUtils.ToBytesForRLP(27)),
            Nethereum.RLP.RLP.EncodeElement(new byte[32]), // bad r
            Nethereum.RLP.RLP.EncodeElement(new byte[32])  // bad s
        );
        var hex = "0x" + Convert.ToHexString(rawTx);

        // Act & Assert
        // Recovery will likely fail or recover wrong address
        var recoveredHash = _handlers.HandleSendRawTransaction(new object[] { hex });
        
        // Now try to execute it
        var tx = _mempool.PopBest();
        var stateTransition = new StateTransition(new EvmMachine(new List<IOpcode>()));
        
        // This should throw because RecoverAddress will fail on zero r/s
        await Assert.ThrowsAsync<Exception>(() => stateTransition.ApplyTransactionAsync(tx!, _globalState, BlockContext.Genesis));
    }

    [Fact]
    public async Task GetNextNonce_UnderParallelSubmission_IsDeterministic()
    {
        // Arrange
        var address = Address.FromHex("0x1234567890123456789012345678901234567890");
        _impersonation.Impersonate(address);
        _globalState.SetNonce(address, 10);

        var txJson = "{\"from\": \"" + address.ToString() + "\", \"to\": \"0x0000000000000000000000000000000000000001\", \"value\": \"0x100\"}";
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(txJson);
        var parameters = new object[] { element };

        // Act - Submit 20 transactions in parallel
        var tasks = new List<Task<string>>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(_handlers.HandleSendTransaction(parameters));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(20, _mempool.Count);
        var nonces = new List<ulong>();
        while (_mempool.Count > 0)
        {
            nonces.Add(_mempool.PopBest()!.Nonce);
        }

        var expectedNonces = Enumerable.Range(10, 20).Select(n => (ulong)n).ToList();
        nonces.Sort();
        Assert.Equal(expectedNonces, nonces);
    }
}