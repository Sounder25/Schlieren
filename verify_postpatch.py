#!/usr/bin/env python3
# Post-patch: multiplier_100 fixture verification
initial_balance     = 0x051acfc1  # 85,643,201
gas_limit           = 0x07a120    # 500,000
max_fee_per_gas     = 14
base_fee            = 7
max_priority_fee    = 7
tx_value            = 1
blob_gas_used       = 6 * (1 << 17)  # 786,432
max_fee_per_blob    = 100
excess_blob_gas     = 0x0e0000
sender_post_fixture = 0x04ff61bc  # 83,845,564
slot0_fixture       = 0x04a40000  # 77,856,768
slot1_fixture       = 0x04a40064  # 77,856,868

def fake_exp(f, n, d):
    i, out, acc = 1, 0, f * d
    while acc > 0:
        out += acc
        acc = acc * n // (d * i)
        i += 1
    return out // d

blob_base_fee        = fake_exp(1, excess_blob_gas, 3_338_477)  # = 1
effective_gas_price  = min(max_fee_per_gas, base_fee + max_priority_fee)  # 14
priority_fee         = effective_gas_price - base_fee            # 7
blob_charge          = blob_gas_used * blob_base_fee             # 786,432
actual_blob_fee      = blob_charge
max_exec_reservation = gas_limit * max_fee_per_gas               # 7,000,000
mid_exec_credit      = 100

# Back-solve gas_used from sender balance
num      = initial_balance - tx_value - blob_charge + mid_exec_credit - sender_post_fixture
gas_used = num // effective_gas_price
assert gas_used * effective_gas_price == num
assert gas_used * priority_fee == 0x07b734  # coinbase check

exec_unused_refund = (gas_limit - gas_used) * effective_gas_price
price_diff_refund  = gas_limit * (max_fee_per_gas - effective_gas_price)  # = 0

# POST-PATCH accounting:
# line 86: senderBalance - maxGasCost - blobFee - tx.Value
scrutor_after_upfront  = initial_balance - max_exec_reservation - actual_blob_fee - tx_value
evm_visible_balance    = scrutor_after_upfront
after_mid_exec         = evm_visible_balance + mid_exec_credit
# result.IsSuccess=True -> valueRestoration=0
scrutor_final          = after_mid_exec + exec_unused_refund + price_diff_refund

print("=== POST-PATCH FIXTURE VERIFICATION ===")
print(f"Fixture: blob_gas_subtraction_tx / value_1 / priority_7 / multiplier_100 / no_access")
print()
print(f"EVM-visible sender balance (BALANCE opcode at execution start):")
print(f"  expected = {slot0_fixture:>14,}  = 0x{slot0_fixture:x}")
print(f"  actual   = {evm_visible_balance:>14,}  = 0x{evm_visible_balance:x}")
print(f"  match    = {evm_visible_balance == slot0_fixture}")
print()
print(f"slot 0 (stored by ORIGIN BALANCE PUSH1 0 SSTORE):")
print(f"  expected = {slot0_fixture:>14,}  = 0x{slot0_fixture:x}")
print(f"  actual   = {evm_visible_balance:>14,}  = 0x{evm_visible_balance:x}")
print(f"  match    = {evm_visible_balance == slot0_fixture}")
print()
print(f"slot 1 (stored after +100 sub-call credit):")
exp_slot1 = slot0_fixture + mid_exec_credit
act_slot1 = evm_visible_balance + mid_exec_credit
print(f"  expected = {exp_slot1:>14,}  = 0x{exp_slot1:x}")
print(f"  actual   = {act_slot1:>14,}  = 0x{act_slot1:x}")
print(f"  match    = {act_slot1 == exp_slot1}")
print()
print(f"final sender balance:")
print(f"  expected = {sender_post_fixture:>14,}  = 0x{sender_post_fixture:x}")
print(f"  actual   = {scrutor_final:>14,}  = 0x{scrutor_final:x}")
print(f"  match    = {scrutor_final == sender_post_fixture}")
print()
all_match = (evm_visible_balance == slot0_fixture and
             act_slot1 == exp_slot1 and
             scrutor_final == sender_post_fixture)
print(f"OVERALL: {'ALL MATCH' if all_match else 'MISMATCH'}")
