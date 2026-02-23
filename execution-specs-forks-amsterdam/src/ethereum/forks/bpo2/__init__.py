"""
The second blob parameter only (BPO) fork, BPO2, includes only changes to the
blob fee schedule.

### Changes

- [EIP-7892: Blob Parameter Only Hardforks][EIP-7892]

### Upgrade Schedule

| Network | Timestamp    | Date & Time (UTC)       | Fork Hash    | Beacon Chain Epoch |
|---------|--------------|-------------------------|--------------|--------------------|
| Holesky | `          ` |                         | `          ` | `      `           |
| Sepolia | `          ` |                         | `          ` | `      `           |
| Hoodi   | `          ` |                         | `          ` |  `     `           |
| Mainnet | `1767747671` | 2025-12-11 21:15:54     | `0x07c9462e` | `      `           |

[EIP-7892]: https://eips.ethereum.org/EIPS/eip-7892
"""  # noqa: E501

from ethereum.fork_criteria import ByTimestamp, ForkCriteria

FORK_CRITERIA: ForkCriteria = ByTimestamp(1767747671)
