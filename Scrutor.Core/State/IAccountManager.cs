using Nethereum.HdWallet;
using Nethereum.Signer;
using NBitcoin;
using Scrutor.Core.Primitives;
using System.Collections.Concurrent;

namespace Scrutor.Core.State;

public interface IAccountManager
{
    void Initialize(int count, string? mnemonic = null, string? derivationPath = null);
    IReadOnlyList<Address> GetAddresses();
    string? GetPrivateKey(Address address);
    string? Mnemonic { get; }
}

public sealed class AccountManager : IAccountManager
{
    private readonly ConcurrentDictionary<Address, string> _accounts = new();
    private readonly List<Address> _orderedAddresses = new();
    private string? _mnemonic;

    public void Initialize(int count, string? mnemonic = null, string? derivationPath = null)
    {
        _mnemonic = mnemonic ?? GenerateMnemonic();
        derivationPath ??= "m/44'/60'/0'/0/";

        var wallet = new Wallet(_mnemonic, null, derivationPath);
        
        _accounts.Clear();
        _orderedAddresses.Clear();

        for (int i = 0; i < count; i++)
        {
            var account = wallet.GetAccount(i);
            var address = Address.FromHex(account.Address);
            var privateKey = account.PrivateKey;

            _accounts.TryAdd(address, privateKey);
            _orderedAddresses.Add(address);
        }
    }

    public IReadOnlyList<Address> GetAddresses() => _orderedAddresses.AsReadOnly();

    public string? GetPrivateKey(Address address)
    {
        return _accounts.TryGetValue(address, out var key) ? key : null;
    }

    public string? Mnemonic => _mnemonic;

    private static string GenerateMnemonic()
    {
        // Use Nethereum's Wallet to generate a new 12-word mnemonic
        var wallet = new Wallet(NBitcoin.Wordlist.English, NBitcoin.WordCount.Twelve);
        return string.Join(" ", wallet.Words);
    }
}
