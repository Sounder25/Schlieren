using System.Numerics;
using Schlieren.Core.Forking;
using Schlieren.Core.Primitives;

namespace Schlieren.Guard;

public sealed class TokenRiskChecker
{
    public static readonly BigInteger WeiPerEth = BigInteger.Parse("1000000000000000000");

    private readonly IForkProvider _fork;
    private readonly string _forkName;

    public TokenRiskChecker(IForkProvider fork, string forkName = "Prague")
    {
        _fork = fork;
        _forkName = forkName;
    }

    public async Task<GuardReport> EvaluateUniswapV2Async(
        Address token,
        ulong? blockNumber = null,
        BigInteger? buyWei = null,
        CancellationToken ct = default)
    {
        var buyer = DisposableBuyer();
        var session = await ScenarioSession.OpenAsync(
            _fork, blockNumber, buyer, token, _forkName, ct: ct);

        session.FundBuyer(2 * WeiPerEth);
        var spend = buyWei ?? WeiPerEth / 20; // 0.05 ETH

        var weth = await session.CallAddressAsync(UniswapV2.Router02, UniswapV2.WethCall(), ct);
        if (weth.Equals(Address.Zero))
            weth = UniswapV2.Weth;

        var pair = await session.CallAddressAsync(
            UniswapV2.Factory, UniswapV2.GetPair(token, weth), ct);
        if (pair.Equals(Address.Zero))
        {
            pair = await session.CallAddressAsync(
                UniswapV2.Factory, UniswapV2.GetPair(token, UniswapV2.Usdc), ct);
        }

        if (pair.Equals(Address.Zero))
        {
            return new GuardReport
            {
                Pin = session.Pinned,
                Token = token.ToString(),
                Router = UniswapV2.Router02.ToString(),
                Buyer = buyer.ToString(),
                Verdict = new GuardVerdict(
                    GuardOutcomeKind.Inconclusive,
                    "INCONCLUSIVE — no Uniswap V2 pool",
                    "Factory.getPair returned the zero address for WETH and USDC. No Router02 path was executed.",
                    null,
                    null,
                    false),
                Steps = session.Steps
            };
        }

        var deadline = new BigInteger(session.Pinned.Timestamp + 600);
        var buyPath = new[] { weth, token };
        var sellPath = new[] { token, weth };

        var buy = await session.ExecuteAsync(
            "buy",
            UniswapV2.Router02,
            UniswapV2.SwapExactEthForTokensSupportingFeeOnTransfer(1, buyPath, buyer, deadline),
            spend,
            ct: ct);

        ScenarioStep? approve = null;
        ScenarioStep? sell = null;
        ScenarioStep? delayed = null;

        if (buy.Succeeded && buy.TokenBalanceAfter > 0)
        {
            approve = await session.ExecuteAsync(
                "approve",
                token,
                UniswapV2.Approve(UniswapV2.Router02, buy.TokenBalanceAfter),
                BigInteger.Zero,
                ct: ct);

            sell = await session.ExecuteAsync(
                "sell",
                UniswapV2.Router02,
                UniswapV2.SwapExactTokensForEthSupportingFeeOnTransfer(
                    buy.TokenBalanceAfter, 1, sellPath, buyer, deadline),
                BigInteger.Zero,
                ct: ct);

            if (!sell.Succeeded)
            {
                delayed = await session.ExecuteAsync(
                    "sell-delayed",
                    UniswapV2.Router02,
                    UniswapV2.SwapExactTokensForEthSupportingFeeOnTransfer(
                        await session.ReadTokenBalanceAsync(token, ct), 1, sellPath, buyer, deadline + 12),
                    BigInteger.Zero,
                    extraBlocks: 1,
                    extraSeconds: 12,
                    ct: ct);
            }
        }
        else
        {
            sell = buy;
        }

        var verdict = GuardAdjudicator.Adjudicate(buy, approve, sell!, delayed);
        return new GuardReport
        {
            Pin = session.Pinned,
            Token = token.ToString(),
            Router = UniswapV2.Router02.ToString(),
            Buyer = buyer.ToString(),
            Verdict = verdict,
            Steps = session.Steps
        };
    }

    public static Address DisposableBuyer()
    {
        var bytes = new byte[20];
        Random.Shared.NextBytes(bytes);
        bytes[0] = 0x67; // keep it clearly synthetic
        return new Address(bytes);
    }
}
