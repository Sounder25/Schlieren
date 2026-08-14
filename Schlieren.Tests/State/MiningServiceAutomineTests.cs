using Microsoft.Extensions.Logging;
using Moq;
using Schlieren.Core.Configuration;
using Schlieren.Core.Execution;
using Schlieren.Core.Models;
using Schlieren.Core.State;
using Xunit;

namespace Schlieren.Tests.State;

public class MiningServiceAutomineTests
{
    [Fact]
    public async Task BackgroundLoop_OnlyMinesWhenAutomineEnabled()
    {
        var mempool = new Mock<ITxMempool>();
        mempool.SetupGet(x => x.Count).Returns(1);
        mempool.Setup(x => x.PopBest()).Returns((Transaction?)null);

        var globalState = new GlobalState();
        var chainState = new ChainState(1, new BlockStore()) { Automine = false };
        var stateTransition = new Mock<IStateTransition>();
        var logger = new Mock<ILogger<MiningService>>();
        var miningService = new MiningService(
            mempool.Object,
            globalState,
            chainState,
            stateTransition.Object,
            logger.Object);

        await miningService.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(350);
            Assert.Equal(0UL, chainState.CurrentBlock.Number);

            chainState.Automine = true;
            await Task.Delay(350);
            Assert.True(chainState.CurrentBlock.Number > 0UL);
        }
        finally
        {
            await miningService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackgroundLoop_IntervalMining_RespectsBlockTimeCadence()
    {
        var mempool = new Mock<ITxMempool>();
        mempool.SetupGet(x => x.Count).Returns(1);
        mempool.Setup(x => x.PopBest()).Returns((Transaction?)null);

        var config = new NodeConfiguration
        {
            ChainId = 1,
            Automine = true,
            BlockTime = 1
        };

        var globalState = new GlobalState();
        var chainState = new ChainState(config, new BlockStore());
        var stateTransition = new Mock<IStateTransition>();
        var logger = new Mock<ILogger<MiningService>>();
        var miningService = new MiningService(
            mempool.Object,
            globalState,
            chainState,
            stateTransition.Object,
            logger.Object);

        await miningService.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(350);
            Assert.Equal(0UL, chainState.CurrentBlock.Number);

            await Task.Delay(1000);
            Assert.True(chainState.CurrentBlock.Number >= 1UL);
        }
        finally
        {
            await miningService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackgroundLoop_InstantAutomine_MinesWithoutIntervalDelay()
    {
        var mempool = new Mock<ITxMempool>();
        mempool.SetupGet(x => x.Count).Returns(1);
        mempool.Setup(x => x.PopBest()).Returns((Transaction?)null);

        var config = new NodeConfiguration
        {
            ChainId = 1,
            Automine = true,
            BlockTime = 0
        };

        var globalState = new GlobalState();
        var chainState = new ChainState(config, new BlockStore());
        var stateTransition = new Mock<IStateTransition>();
        var logger = new Mock<ILogger<MiningService>>();
        var miningService = new MiningService(
            mempool.Object,
            globalState,
            chainState,
            stateTransition.Object,
            logger.Object);

        await miningService.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(350);
            Assert.True(chainState.CurrentBlock.Number > 0UL);
        }
        finally
        {
            await miningService.StopAsync(CancellationToken.None);
        }
    }
}
