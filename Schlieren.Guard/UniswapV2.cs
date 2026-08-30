using System.Numerics;
using Schlieren.Core.Primitives;

namespace Schlieren.Guard;

public static class UniswapV2
{
    public static readonly Address Router02 = Address.FromHex("0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D");
    public static readonly Address Factory = Address.FromHex("0x5C69bEe701ef814a2B6a3EDD4B1652CB9cc5aA6f");
    public static readonly Address Weth = Address.FromHex("0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2");
    public static readonly Address Usdc = Address.FromHex("0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48");

    public static byte[] WethCall() =>
        Abi.EncodeCall("WETH()");

    public static byte[] FactoryCall() =>
        Abi.EncodeCall("factory()");

    public static byte[] GetPair(Address tokenA, Address tokenB) =>
        Abi.EncodeCall("getPair(address,address)", Abi.Word(tokenA), Abi.Word(tokenB));

    public static byte[] BalanceOf(Address owner) =>
        Abi.EncodeCall("balanceOf(address)", Abi.Word(owner));

    public static byte[] Approve(Address spender, BigInteger amount) =>
        Abi.EncodeCall("approve(address,uint256)", Abi.Word(spender), Abi.Word(amount));

    public static byte[] SwapExactEthForTokensSupportingFeeOnTransfer(
        BigInteger amountOutMin,
        IReadOnlyList<Address> path,
        Address to,
        BigInteger deadline) =>
        Concat(
            Abi.Selector("swapExactETHForTokensSupportingFeeOnTransferTokens(uint256,address[],address,uint256)"),
            Abi.Word(amountOutMin),
            Abi.Word(128),
            Abi.Word(to),
            Abi.Word(deadline),
            Abi.Word(path.Count),
            PathWords(path));

    public static byte[] SwapExactTokensForEthSupportingFeeOnTransfer(
        BigInteger amountIn,
        BigInteger amountOutMin,
        IReadOnlyList<Address> path,
        Address to,
        BigInteger deadline) =>
        Concat(
            Abi.Selector("swapExactTokensForETHSupportingFeeOnTransferTokens(uint256,uint256,address[],address,uint256)"),
            Abi.Word(amountIn),
            Abi.Word(amountOutMin),
            Abi.Word(160),
            Abi.Word(to),
            Abi.Word(deadline),
            Abi.Word(path.Count),
            PathWords(path));

    private static byte[] PathWords(IReadOnlyList<Address> path)
    {
        var words = new byte[path.Count * 32];
        for (var i = 0; i < path.Count; i++)
            Abi.Word(path[i]).CopyTo(words, i * 32);
        return words;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var payload = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(payload, offset);
            offset += part.Length;
        }
        return payload;
    }
}
