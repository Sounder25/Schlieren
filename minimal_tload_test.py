#!/usr/bin/env python3
"""
Minimal TLOAD test to isolate the 2,800 gas overcharge.
Run basic_tload_after_store and dump exact PC-level trace.
"""

import subprocess
import sys

# Bytecode from fixture: 0x5b601660015560015c6001556001600255
# Disassembly:
#   PC 0: JUMPDEST
#   PC 1: PUSH1 0x16
#   PC 3: PUSH1 0x01
#   PC 5: TSTORE
#   PC 6: PUSH1 0x01
#   PC 8: TLOAD
#   PC 9: PUSH1 0x01
#   PC 11: SSTORE
#   PC 12: PUSH1 0x01
#   PC 14: PUSH1 0x02
#   PC 16: SSTORE

print("Running basic_tload_after_store with Scrutor...")
print("Expected gas: ~21000 (intrinsic) + ~100 (TLOAD) + ~100 (TSTORE) + 2×SSTORE + overhead")
print("Actual over-charge: 2,800 gas")
print()
print("This suggests 28 extra 100-gas charges somewhere in the execution path.")
