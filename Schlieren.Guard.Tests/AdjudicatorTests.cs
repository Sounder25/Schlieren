using System.Numerics;
using Schlieren.Core.Execution;
using Schlieren.Core.Primitives;
using Schlieren.Core.State;

namespace Schlieren.Guard.Tests;

public sealed class AdjudicatorTests
{
    [Fact]
    public void FailedSell_IsNotAutomaticallyHoneypot()
    {
        var buy = Step("buy", true, 10, 9);
        var sell = Step("sell", false, 9, 9);
        var verdict = GuardAdjudicator.Adjudicate(buy, null, sell);
        Assert.Equal(GuardOutcomeKind.SellBlocked, verdict.Kind);
        Assert.False(verdict.LooksLikeHoneypot);
        Assert.DoesNotContain("HONEYPOT", verdict.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RouterCalldata_UsesFeeOnTransferSelectors()
    {
        var buy = UniswapV2.SwapExactEthForTokensSupportingFeeOnTransfer(
            1, new[] { UniswapV2.Weth, UniswapV2.Usdc }, UniswapV2.Weth, 1);
        var sell = UniswapV2.SwapExactTokensForEthSupportingFeeOnTransfer(
            1, 1, new[] { UniswapV2.Usdc, UniswapV2.Weth }, UniswapV2.Weth, 1);
        Assert.Equal(
            Convert.ToHexString(Abi.Selector("swapExactETHForTokensSupportingFeeOnTransferTokens(uint256,address[],address,uint256)")),
            Convert.ToHexString(buy[..4]));
        Assert.Equal(
            Convert.ToHexString(Abi.Selector("swapExactTokensForETHSupportingFeeOnTransferTokens(uint256,uint256,address[],address,uint256)")),
            Convert.ToHexString(sell[..4]));
    }

    private static ScenarioStep Step(string name, bool success, int ethBefore, int ethAfter)
    {
        var result = success
            ? ExecutionResult.Success(21000)
            : ExecutionResult.Failure(EvmError.Revert, 21000);
        return new ScenarioStep(
            name,
            new Transaction { From = Address.Zero },
            BlockContext.Genesis,
            result,
            ethBefore,
            ethAfter,
            0,
            success ? 1 : 0);
    }
}
