import json

fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

def fake_exp(f, n, d):
    i, out, acc = 1, 0, f * d
    while acc > 0:
        out += acc
        acc = (acc * n) // (d * i)
        i += 1
    return out // d

for case_name, case_data in data.items():
    for post_state in case_data['post']['Cancun']:
        tx_idx = post_state['indexes']['data']
        tx_data = case_data['transaction']
        
        def get_tx_val(k):
            v = tx_data[k]
            return int(v[tx_idx] if isinstance(v, list) and len(v) > tx_idx else v[0] if isinstance(v, list) else v, 16)
        
        try:
            gas_limit = get_tx_val('gasLimit')
            max_fee_per_gas = get_tx_val('maxFeePerGas')
            max_priority_fee = get_tx_val('maxPriorityFeePerGas')
            max_fee_per_blob = get_tx_val('maxFeePerBlobGas')
            tx_value = get_tx_val('value')
        except KeyError:
            continue
            
        bvh = tx_data['blobVersionedHashes']
        bvh = bvh[tx_idx] if isinstance(bvh, list) and len(bvh) > tx_idx else bvh[0] if isinstance(bvh, list) else bvh
        blob_count = 1 if isinstance(bvh, str) else len(bvh)
        blob_gas_used = blob_count * 131072
        
        env = case_data['env']
        base_fee = int(env['currentBaseFee'], 16)
        excess_blob_gas = int(env.get('currentExcessBlobGas', '0x0'), 16)
        blob_base_fee = fake_exp(1, excess_blob_gas, 3338477)
        effective_gas_price = min(max_fee_per_gas, base_fee + max_priority_fee)
        
        sender = tx_data['sender']
        pre_balance = int(case_data['pre'][sender]['balance'], 16)
        
        # Calculate scrutor final balance
        # 1. Upfront
        max_gas_cost = gas_limit * max_fee_per_gas
        blob_fee = blob_gas_used * blob_base_fee
        upfront_deduction = max_gas_cost + blob_fee + tx_value
        after_upfront = pre_balance - upfront_deduction
        
        # mid-execution credit (100 wei from test name?)
        mid_credit = 100 if "100_wei_mid_execution" in case_name else 0
        
        # 2. End refund
        # Assume gas used is 27200 for access list, or 21000 without. Let's try 21000, 27200, 25400
        gas_used = 27200 if "access_list" in case_name else 21000
        if tx_data.get('data') and tx_data['data'] != ['0x'] and tx_data['data'] != '0x':
            gas_used += 16 * (len(tx_data['data'][0])-2)//2 # simplified
        gas_refund = gas_limit - gas_used
        gas_refund_amount = gas_refund * effective_gas_price
        price_diff_refund = gas_limit * (max_fee_per_gas - effective_gas_price)
        
        scrutor_final = after_upfront + mid_credit + gas_refund_amount + price_diff_refund
        
        # The expected delta is the difference between correct EIP-4844 math and Scrutor.
        # Correct EIP-4844 upfront deduction: max_gas_cost + max_blob_cost + tx_value
        max_blob_cost = blob_gas_used * max_fee_per_blob
        correct_upfront_deduction = max_gas_cost + max_blob_cost + tx_value
        correct_after_upfront = pre_balance - correct_upfront_deduction
        
        # Correct EIP-4844 end refund: gas_refund + price_diff + BLOB_REFUND
        blob_refund = blob_gas_used * (max_fee_per_blob - blob_base_fee)
        correct_final = correct_after_upfront + mid_credit + gas_refund_amount + price_diff_refund + blob_refund
        
        delta = correct_final - scrutor_final
        if delta == -139300 or delta == 139300 or True:
            # Let's print out the exact expected balance from the fixture for the sender
            # wait, post_state doesn't have the expected balance of sender directly.
            pass
