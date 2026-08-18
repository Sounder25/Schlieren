
import sys
sys.path.insert(0, r"C:/projects/execution-specs/src")

from ethereum.forks.berlin import vm
from ethereum.forks.berlin.fork import process_transaction
from ethereum_types.bytes import Bytes20, Bytes32
from ethereum.base_types import U256, U64, Uint

Address = Bytes20

CALLER  = Address(bytes.fromhex("0000000000000000000000000000000000000001"))
PARENT  = Address(bytes.fromhex("00000000000000000000000000000000000000aa"))
CHILD   = Address(bytes.fromhex("00000000000000000000000000000000000000bb"))
ZERO    = Address(bytes.fromhex("0000000000000000000000000000000000000000"))

parent_code = bytes.fromhex(
    "6000600060006000600073"
    "00000000000000000000000000000000000000bb"
    "5af15000"
)
child_code = bytes.fromhex("600060005500")

ETH = 10**18

# Build BlockState (trie-based)
state = vm.BlockState()

# Set accounts via the vm module functions
from ethereum.forks.berlin.vm import set_account, get_account, set_storage
from ethereum.state import Account

set_account(state, CALLER, Account(balance=U256(10*ETH), nonce=Uint(0), code=b"", storage_root=b""))
set_account(state, PARENT, Account(balance=U256(10*ETH), nonce=Uint(0), code=parent_code, storage_root=b""))
set_account(state, CHILD,  Account(balance=U256(10*ETH), nonce=Uint(0), code=child_code, storage_root=b""))
set_storage(state, CHILD, Bytes32(b"\x00"*32), U256(0xAA))

block_env = vm.BlockEnvironment(
    chain_id=U64(1),
    state=state,
    block_gas_limit=Uint(30_000_000),
    block_hashes=[],
    coinbase=ZERO,
    number=Uint(1),
    time=U256(1),
    difficulty=Uint(1),
    base_fee_per_gas=Uint(1),
    blob_versioned_hashes=(),
    excess_blob_gas=U256(0),
    parent_beacon_block_root=Bytes32(b"\x00"*32),
)

from ethereum.forks.berlin.transactions import LegacyTransaction
tx = LegacyTransaction(
    nonce=U256(0),
    gas_price=U256(1),
    gas=Uint(10_000_000),
    to=PARENT,
    value=U256(0),
    data=b"",
    v=U256(27), r=U256(1), s=U256(1),
)

block_output = vm.BlockOutput()
process_transaction(block_env, block_output, tx, Uint(0))
print(f"EELS Berlin XToZero gas_used = {block_output.block_gas_used}")
