using Schlieren.Core.Models;
using Schlieren.Core.Primitives;

namespace Schlieren.Guard;

public sealed record PinnedBase(
    ulong ChainId,
    ulong BlockNumber,
    string BlockHash,
    ulong Timestamp,
    ulong GasLimit,
    ulong BaseFeePerGas,
    Address Coinbase,
    string ForkName,
    int ScenarioVersion)
{
    public const int CurrentScenarioVersion = 1;

    public static PinnedBase FromBlock(ulong chainId, Block block, string forkName) =>
        new(
            chainId,
            block.Number,
            block.Hash,
            block.Timestamp,
            block.GasLimit == 0 ? 30_000_000UL : block.GasLimit,
            block.BaseFeePerGas == 0 ? 1_000_000_000UL : block.BaseFeePerGas,
            string.IsNullOrEmpty(block.Miner) ? Address.Zero : Address.FromHex(block.Miner),
            forkName,
            CurrentScenarioVersion);
}
