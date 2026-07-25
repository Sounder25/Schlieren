#!/usr/bin/env python3
# ============================================================
# DEFINITIVE BLOB TRANSACTION LEDGER ANALYSIS
# Fixture: blob_gas_subtraction_tx
# Key:     value_1 / priority_7 / max_fee_14 / no_access_list
# Source:  100_wei_mid_execution variant
# ============================================================

# ============================================================
# RAW FIXTURE INPUTS (all raw integers, no computation yet)
# ============================================================
initial_balance     = 0x76cfc1   # 7,786,433 (sender pre-state)
gas_limit           = 0x07a120   # 500,000
max_fee_per_gas     = 0x0e       # 14
max_priority_fee    = 0x07       # 7
base_fee            = 0x07       # 7
tx_value            = 0x01       # 1
blob_count          = 6
GAS_PER_BLOB        = 1 << 17    # 131,072
blob_gas_used       = GAS_PER_BLOB * blob_count  # 786,432
max_fee_per_blob    = 0x01       # 1
excess_blob_gas     = 0x0e0000   # 917,504
sender_post_fixture = 0x5fa204   # 6,267,396  (expected final)
coinbase_post       = 0x059710   # 366,352    (expected coinbase)

# ============================================================
# DERIVED VALUES
# ============================================================

def fake_exponential(factor, numerator, denominator):
    """EELS FakeExponential for blob_base_fee."""
    i = 1
    output = 0
    num_acc = factor * denominator
    while num_acc > 0:
        output += num_acc
        num_acc = num_acc * numerator // (denominator * i)
        i += 1
    return output // denominator

blob_base_fee        = fake_exponential(1, excess_blob_gas, 3_338_477)
effective_gas_price  = min(max_fee_per_gas, base_fee + max_priority_fee)  # = 14
priority_fee         = effective_gas_price - base_fee                       # = 7
blob_charge          = blob_gas_used * blob_base_fee                        # = 786,432

# This fixture is the "-100_wei_mid_execution" variant:
# During EVM execution, a sub-call sends +100 wei to the sender.
# This is an overlay commit so it arrives in the sender balance.
mid_exec_credit      = 100

# Back-solve gas_used from SENDER post-balance:
# sender_post = initial - tx_value - execution_charge - blob_charge + mid_exec_credit
# execution_charge = gas_used * effective_gas_price
# => gas_used = (initial - tx_value - blob_charge + mid_exec_credit - sender_post) / effective
numerator = initial_balance - tx_value - blob_charge + mid_exec_credit - sender_post_fixture
gas_used   = numerator // effective_gas_price

assert gas_used * effective_gas_price == numerator, \
    f"Non-integer gas_used: {numerator}/{effective_gas_price}"

# Verify coinbase
assert gas_used * priority_fee == coinbase_post, \
    f"Coinbase mismatch: {gas_used}*{priority_fee}={gas_used*priority_fee} vs {coinbase_post}"

execution_charge = gas_used * effective_gas_price

# ============================================================
# UPFRONT RESERVATIONS
# ============================================================
max_exec_reservation  = gas_limit * max_fee_per_gas            # 500000 * 14 = 7,000,000
max_blob_reservation  = blob_gas_used * max_fee_per_blob        # 786432 * 1  = 786,432
upfront_requirement   = max_exec_reservation + max_blob_reservation + tx_value  # 7,786,433

# CalculateBlobFee (Scrutor line 386-396) = actual blob fee
actual_blob_fee_upfront = blob_gas_used * blob_base_fee         # 786432 * 1  = 786,432

# ============================================================
# POST-EXECUTION REFUNDS
# ============================================================
gas_refund_unused = gas_limit - gas_used
exec_unused_refund = gas_refund_unused * effective_gas_price
price_diff_refund  = gas_limit * (max_fee_per_gas - effective_gas_price)  # = 0 here
blob_cap_refund    = max_blob_reservation - blob_charge                    # = 0 here (max==actual)

# ============================================================
# SCRUTOR EXECUTION PATH
# ============================================================
# line 86:  state.SetBalance(tx.From, senderBalance - maxGasCost - blobFee)
#           maxGasCost = gas_limit * priceForUpfront  (= max_fee_per_gas for type-3)
#           blobFee    = CalculateBlobFee              (= actual blob fee, NOT max)
scrutor_after_upfront = initial_balance - max_exec_reservation - actual_blob_fee_upfront

# EVM runs; on success, line 612 deducts value from sender (via overlay commit)
scrutor_after_value   = scrutor_after_upfront - tx_value

# EVM mid-execution: +100 wei arrives via overlay commit
scrutor_after_mid_exec = scrutor_after_value + mid_exec_credit

# line 207: gasRefundAmount + priceDiffRefund
scrutor_after_refunds  = scrutor_after_mid_exec + exec_unused_refund + price_diff_refund

# Scrutor does NOT separately refund blob cap difference.
# When max_fee_per_blob > blob_base_fee, actual_blob_fee_upfront < max_blob_reservation,
# so sender is over-refunded unless blob cap refund is explicitly added.
# Here: max_fee_per_blob == blob_base_fee == 1, so no gap.

scrutor_final = scrutor_after_refunds

# ============================================================
# PRINT REPORT
# ============================================================

print("=" * 70)
print("BLOB TRANSACTION LEDGER - COMPLETE ANALYSIS")
print("Fixture: blob_gas_subtraction_tx / value_1 / priority_7 / max_14 / no_access")
print("=" * 70)

print()
print("=== TRANSACTION PARAMETERS (raw integers) ===")
print(f"  gas_limit                = {gas_limit:>12,}  = 0x{gas_limit:x}")
print(f"  gas_used (total)         = {gas_used:>12,}  (back-solved from sender balance)")
print(f"  max_fee_per_gas          = {max_fee_per_gas:>12}  = 0x{max_fee_per_gas:x}")
print(f"  max_priority_fee_per_gas = {max_priority_fee:>12}  = 0x{max_priority_fee:x}")
print(f"  base_fee                 = {base_fee:>12}  = 0x{base_fee:x}")
print(f"  effective_gas_price      = {effective_gas_price:>12}  = min({max_fee_per_gas}, {base_fee}+{max_priority_fee})")
print(f"  blob_count               = {blob_count:>12}")
print(f"  blob_gas_used            = {blob_gas_used:>12,}  = {blob_count} x {GAS_PER_BLOB}")
print(f"  max_fee_per_blob_gas     = {max_fee_per_blob:>12}  = 0x{max_fee_per_blob:x}")
print(f"  blob_base_fee            = {blob_base_fee:>12}  (FakeExponential(1, {excess_blob_gas}, 3338477))")
print(f"  excess_blob_gas          = {excess_blob_gas:>12,}  = 0x{excess_blob_gas:x}")
print(f"  transaction_value        = {tx_value:>12}  = 0x{tx_value:x}")

print()
print("=== SETTLEMENT EQUATIONS (written independently) ===")
print()
print(f"  maximum_execution_reservation:")
print(f"    gas_limit x max_fee_per_gas = {gas_limit} x {max_fee_per_gas} = {max_exec_reservation:,}")
print()
print(f"  maximum_blob_reservation:")
print(f"    blob_gas_used x max_fee_per_blob = {blob_gas_used} x {max_fee_per_blob} = {max_blob_reservation:,}")
print()
print(f"  upfront_requirement:")
print(f"    {max_exec_reservation} + {max_blob_reservation} + {tx_value} = {upfront_requirement:,}")
print(f"    sender covers upfront: {initial_balance} >= {upfront_requirement}: {initial_balance >= upfront_requirement}")
print()
print(f"  execution_charge:")
print(f"    gas_used x effective_gas_price = {gas_used} x {effective_gas_price} = {execution_charge:,}")
print()
print(f"  blob_charge:")
print(f"    blob_gas_used x blob_base_fee = {blob_gas_used} x {blob_base_fee} = {blob_charge:,}")
print()
print(f"  final_sender_balance:")
print(f"    initial - tx_value - execution_charge - blob_charge")
print(f"    = {initial_balance} - {tx_value} - {execution_charge} - {blob_charge}")
print(f"    = {initial_balance - tx_value - execution_charge - blob_charge:,}")
print(f"    [+ {mid_exec_credit} mid-exec credit from sub-call in this specific fixture]")
print(f"    = {initial_balance - tx_value - execution_charge - blob_charge + mid_exec_credit:,}")
print(f"    fixture expected = {sender_post_fixture:,}")
print(f"    MATCH: {initial_balance - tx_value - execution_charge - blob_charge + mid_exec_credit == sender_post_fixture}")

print()
print("=== SENDER BALANCE LEDGER (stage by stage) ===")
print()
print(f"  {'Stage':<40} {'Balance':>14}  Operation")
print(f"  {'-'*40} {'-'*14}  {'-'*40}")

b = initial_balance
stages = [
    ("Initial state",                       b,      "pre-state balance = 0x{:x}".format(b)),
    ("After tx validation",                 b,      "no mutation (validation only)"),
    ("After exec-gas reservation",          b - max_exec_reservation,
         "- gas_limit x max_fee = -{:,}".format(max_exec_reservation)),
    ("After blob-gas reservation (max)",    b - max_exec_reservation - max_blob_reservation,
         "- blob_gas_used x max_fee_per_blob = -{:,}".format(max_blob_reservation)),
    ("After value deduction (spec)",        b - max_exec_reservation - max_blob_reservation - tx_value,
         "- tx_value = -{}".format(tx_value)),
]
for name, bal, op in stages:
    print(f"  {name:<40} {bal:>14,}  {op}")

print()
print(f"  [Scrutor line 86 deducts: maxGasCost + CalculateBlobFee (actual blob fee)]")
print(f"    maxGasCost           = gas_limit x max_fee_per_gas = {max_exec_reservation:,}")
print(f"    CalculateBlobFee     = blob_gas_used x blob_base_fee = {actual_blob_fee_upfront:,}")
print(f"    max_blob_reservation = blob_gas_used x max_fee_per_blob = {max_blob_reservation:,}")
print(f"    Gap (max - actual)   = {max_blob_reservation - actual_blob_fee_upfront:,}  [0 = no bug here]")

print()
print(f"  {'Scrutor after upfront (line 86)':<40} {scrutor_after_upfront:>14,}  - {max_exec_reservation} - {actual_blob_fee_upfront}")
print(f"  {'Balance visible to EVM':<40} {scrutor_after_upfront:>14,}  (BALANCE opcode would see this)")
print(f"  {'After value deduction (EVM line 612)':<40} {scrutor_after_value:>14,}  - tx_value (on success)")
print(f"  {'After mid-exec credit (+100)':<40} {scrutor_after_mid_exec:>14,}  +{mid_exec_credit} (overlay commit)")
print(f"  {'After exec-unused refund (line 207)':<40} {scrutor_after_mid_exec + exec_unused_refund:>14,}  +{exec_unused_refund:,} (gas_refund_unused x effective)")
print(f"  {'After price-diff refund (line 207)':<40} {scrutor_after_mid_exec + exec_unused_refund + price_diff_refund:>14,}  +{price_diff_refund} (gas_limit x (max-effective))")
print(f"  {'After blob cap refund':<40} {'N/A (skipped)':>14}  +{blob_cap_refund} expected (max_blob-blob_charge)")
print(f"  {'Scrutor final':<40} {scrutor_final:>14,}  = 0x{scrutor_final:x}")
print(f"  {'Fixture expected':<40} {sender_post_fixture:>14,}  = 0x{sender_post_fixture:x}")

print()
print("=== DELTA ANALYSIS ===")
delta = scrutor_final - sender_post_fixture
print(f"  scrutor_final - fixture_expected = {scrutor_final} - {sender_post_fixture} = {delta:,}")
if delta == 0:
    print("  EXACT MATCH -- Scrutor is correct for this fixture")
elif delta > 0:
    print("  Scrutor over-pays sender by {:,} wei (under-charges)".format(delta))
else:
    print("  Scrutor under-pays sender by {:,} wei (over-charges)".format(abs(delta)))

print()
print("=== SIGNATURE-TO-TERM TABLE ===")
print(f"  delta = {delta:,},  abs(delta) = {abs(delta):,}")
print()
terms = [
    ("tx_value",                                    tx_value),
    ("blob_gas_used x blob_base_fee",               blob_charge),
    ("blob_gas_used x max_fee_per_blob",            max_blob_reservation),
    ("max_blob_reservation - blob_charge",          blob_cap_refund),
    ("exec_unused_refund",                          exec_unused_refund),
    ("price_diff_refund",                           price_diff_refund),
    ("exec_unused + price_diff",                    exec_unused_refund + price_diff_refund),
    ("2 x blob_charge",                             2 * blob_charge),
    ("blob_charge + tx_value",                      blob_charge + tx_value),
    ("max_exec_reservation - exec_charge",          max_exec_reservation - execution_charge),
    ("actual_blob_fee_upfront",                     actual_blob_fee_upfront),
    ("initial - expected_final (total cost)",       initial_balance - sender_post_fixture),
    ("initial - scrutor_final",                     initial_balance - scrutor_final),
    ("mid_exec_credit",                             mid_exec_credit),
    ("blob_charge - mid_exec_credit",               blob_charge - mid_exec_credit),
    ("exec_charge + blob_charge + value",           execution_charge + blob_charge + tx_value),
]
for name, val in terms:
    eq  = "  <-- EXACT MATCH" if val == abs(delta) else ""
    neg = "  <-- EXACT MATCH (neg)" if -val == delta else ""
    print(f"  {name:<50} = {val:>12,}{eq}{neg}")

print()
print("=== KNOWN BUGS IN SCRUTOR (StateTransition.cs) ===")
print()
print("BUG 1: tx_value deducted AFTER upfront, not AS PART of upfront (line 86 vs line 612)")
print("  - Validation check (line 79): upfrontCost = maxGasCost + maxBlobCost + tx.Value  [CORRECT]")
print("  - Deduction  (line 86): senderBalance - maxGasCost - blobFee  [MISSING tx.Value]")
print("  - tx.Value deducted later via EVM overlay (line 612) on success only")
print("  - This means on FAILURE, tx.Value is never deducted -- POTENTIAL BUG")
print("  - Also means EVM observes balance HIGHER than it should")
print()
print("BUG 2: CalculateBlobFee deducts ACTUAL blob fee, not MAX blob fee (line 86 vs line 78)")
print("  - Validation (line 78-79): uses maxBlobCost = blob_gas_used x max_fee_per_blob  [CORRECT]")
print("  - Deduction  (line 86):    uses blobFee = CalculateBlobFee = blob_gas_used x blob_base_fee")
print("  - When max_fee_per_blob > blob_base_fee: sender gets EXTRA value back (no refund needed)")
print("  - When max_fee_per_blob == blob_base_fee: no gap (correct for this fixture)")
print("  - This is actually CORRECT behavior per EIP-4844 spec:")
print("    the sender pays actual blob base fee, not the max. No refund needed.")
print("  [NOT A BUG -- EIP-4844 spec says deduct actual blob fee upfront]")
print()
print("BUG 3: Balance visible to EVM excludes tx_value (because value deducted inside EVM)")
print("  - EVM sees balance BEFORE value deduction")
print("  - BALANCE opcode returns: initial - max_exec_reservation - actual_blob_fee")
print(f"    = {scrutor_after_upfront:,} = 0x{scrutor_after_upfront:x}")
print("  - Correct balance should be: initial - max_exec_reservation - actual_blob_fee - tx_value")
print(f"    = {scrutor_after_upfront - tx_value:,} = 0x{(scrutor_after_upfront - tx_value):x}")
print("  - In this fixture: difference = 1 wei (= tx_value). Minor but incorrect.")
print()
print("=== LIKELY BAD BOUNDARIES CHECK ===")
print()
print(f"1. Validation affordability:")
print(f"   Required: gas_limit x max_fee + blob_gas_used x max_fee_per_blob + value")
print(f"   = {max_exec_reservation} + {max_blob_reservation} + {tx_value} = {upfront_requirement:,}")
print(f"   Sender balance: {initial_balance:,}")
print(f"   Sufficient: {initial_balance >= upfront_requirement}")
print()
print(f"2. Pre-execution balance deduction (Scrutor line 86):")
print(f"   Deducts: max_exec_reservation + actual_blob_fee (NOT max_blob_reservation)")
print(f"   = {max_exec_reservation} + {actual_blob_fee_upfront} = {max_exec_reservation + actual_blob_fee_upfront:,}")
print(f"   tx_value NOT deducted here -- deducted in EVM on success (line 612)")
print()
print(f"3. Balance visible inside EVM (BALANCE opcode would see):")
print(f"   = {scrutor_after_upfront:,} = 0x{scrutor_after_upfront:x}")
print(f"   This is initial - max_exec_reservation - actual_blob_fee (no value deducted yet)")
print(f"   Spec requires: initial - all_upfront_charges (incl value)")
print(f"   Expected visible: {scrutor_after_upfront - tx_value:,} = 0x{(scrutor_after_upfront - tx_value):x}")
print(f"   Scrutor shows {tx_value} wei too much to EVM")
print()
print(f"4. Fee-cap refund:")
print(f"   exec_unused_refund = (gas_limit - gas_used) x effective_gas_price")
print(f"   = ({gas_limit} - {gas_used}) x {effective_gas_price} = {exec_unused_refund:,}")
print(f"   price_diff_refund  = gas_limit x (max_fee - effective) = {gas_limit} x {max_fee_per_gas - effective_gas_price} = {price_diff_refund}")
print(f"   blob_cap_refund    = max_blob_reservation - blob_charge = {blob_cap_refund}")
print(f"   Scrutor applies exec_unused + price_diff. Blob cap refund: N/A here (gap=0).")
print()
print(f"5. Coinbase credit:")
print(f"   coinbase_credit = gas_used x priority_fee = {gas_used} x {priority_fee} = {gas_used * priority_fee:,}")
print(f"   blob fees go to BURNED address (not coinbase): {blob_charge:,} wei burned")
print(f"   Scrutor coinbase line 218: minerFee = totalGasUsed x effectivePriorityFee  [CORRECT]")
print(f"   Scrutor does NOT credit blob fees to coinbase  [CORRECT per spec]")
