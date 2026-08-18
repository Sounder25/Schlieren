using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schlieren.Core.Configuration;
using Schlieren.Core.State;

namespace Schlieren.Core.Services;

public sealed class BootstrapService : IHostedService
{
    private readonly IAccountManager _accountManager;
    private readonly IGlobalState _globalState;
    private readonly IImpersonationService _impersonation;
    private readonly IStateManager _stateManager;
    private readonly NodeConfiguration _config;
    private readonly ILogger<BootstrapService> _logger;

    public BootstrapService(
        IAccountManager accountManager,
        IGlobalState globalState,
        IImpersonationService impersonation,
        IStateManager stateManager,
        NodeConfiguration config,
        ILogger<BootstrapService> logger)
    {
        _accountManager = accountManager;
        _globalState = globalState;
        _impersonation = impersonation;
        _stateManager = stateManager;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bootstrapping Schlieren Node...");

        // 1. Load State (if configured)
        if (!string.IsNullOrEmpty(_config.InitState))
        {
            await _stateManager.LoadStateAsync(_config.InitState);
        }

        // 2. Initialize Accounts (if mnemonic not already loaded from state? 
        // actually AccountManager generates keys, GlobalState holds balances. 
        // We always init key manager.)
        _accountManager.Initialize(_config.Accounts, _config.Mnemonic, _config.DerivationPath);
        _logger.LogInformation("Initialized {Count} accounts from HD wallet.", _config.Accounts);

        // 3. Fund & Impersonate Test Accounts
        var initialBalanceWei = (System.Numerics.BigInteger)(_config.Balance * 1_000_000_000_000_000_000m);
        foreach (var address in _accountManager.GetAddresses())
        {
            // Only overwrite if balance is zero (preserve loaded state)
            var current = await _globalState.GetBalanceAsync(address, cancellationToken);
            if (current == 0)
            {
                _globalState.SetBalance(address, initialBalanceWei);
            }

            if (_config.AutoImpersonate)
            {
                _impersonation.Impersonate(address);
            }
        }
        
        _logger.LogInformation("Bootstrap complete.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Dump state on shutdown if configured
        if (!string.IsNullOrEmpty(_config.DumpState))
        {
            await _stateManager.SaveStateAsync(_config.DumpState);
        }
    }
}
