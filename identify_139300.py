import json

fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

target = "fork_Cancun-max_blobs-state_test--100_wei_mid_execution--tx_max_fee_per_blob_gas_multiplier_1-no_calldata-tx_value_0-tx_max_priority_fee_per_gas_0-tx_max_fee_per_gas_14-access_list"

for case_name, case_data in data.items():
    if target in case_name:
        post_state = case_data['post']['Cancun'][0]
        tx_idx = post_state['indexes']['data']
        tx_data = case_data['transaction']
        
        def get_tx_val(k):
            v = tx_data[k]
            return int(v[tx_idx] if isinstance(v, list) and len(v) > tx_idx else v[0] if isinstance(v, list) else v, 16)
        
        gas_limit = get_tx_val('gasLimit')
        max_fee = get_tx_val('maxFeePerGas')
        max_priority = get_tx_val('maxPriorityFeePerGas')
        max_blob = get_tx_val('maxFeePerBlobGas')
        value = get_tx_val('value')
        
        bvh = tx_data['blobVersionedHashes']
        bvh = bvh[tx_idx] if isinstance(bvh, list) and len(bvh) > tx_idx else bvh[0] if isinstance(bvh, list) else bvh
        blob_count = 1 if isinstance(bvh, str) else len(bvh)
        # Wait, if test name says "max_blobs", max blobs in Cancun is 6.
        if "max_blobs" in case_name:
            blob_count = 6
            
        blob_gas_used = blob_count * 131072
        
        env = case_data['env']
        base_fee = int(env['currentBaseFee'], 16)
        excess = int(env.get('currentExcessBlobGas', '0x0'), 16)
        def fake_exp(f, n, d):
            i, out, acc = 1, 0, f * d
            while acc > 0:
                out += acc
                acc = (acc * n) // (d * i)
                i += 1
            return out // d
        blob_base_fee = fake_exp(1, excess, 3338477)
        eff_gas = min(max_fee, base_fee + max_priority)
        
        sender = tx_data['sender']
        initial = int(case_data['pre'][sender]['balance'], 16)
        
        # Expected components
        max_exec_res = gas_limit * max_fee
        max_blob_res = blob_gas_used * max_blob
        
        # Assuming gas_used
        # For an empty tx with access list: 21000 + 2400 + 1900*2 = 27200? Wait, 2400 + 1900 = 4300 if 1 key.
        gas_used = 25400 # 21000 + 2400 + 1900
        # Wait, let's look at the delta: scrutor final - expected = 139300
        # What if Scrutor doesn't refund blob_gas_used * (max_fee_per_blob_gas - blob_base_fee)?
        # For this case, max_blob = 1, blob_base_fee = 1, so refund = 0.
        
        # I'll just print out the terms directly to see if any equals 139300.
        terms = {
            "gas_used * effective_gas_price": gas_used * eff_gas,
            "gas_limit * max_fee_per_gas": gas_limit * max_fee,
            "unused_gas * max_fee_per_gas": (gas_limit - gas_used) * max_fee,
            "unused_gas * effective_gas_price": (gas_limit - gas_used) * eff_gas,
            "gas_used * (max_fee_per_gas - effective_gas_price)": gas_used * (max_fee - eff_gas),
            "blob_gas_used * max_fee_per_blob_gas": blob_gas_used * max_blob,
            "blob_gas_used * blob_base_fee": blob_gas_used * blob_base_fee,
            "blob_gas_used * (max_fee_per_blob_gas - blob_base_fee)": blob_gas_used * (max_blob - blob_base_fee)
        }
        
        for gas_used in [21000, 25400, 27200, 19900]:
            print(f"--- gas_used = {gas_used} ---")
            terms["gas_used * (max_fee_per_gas - effective_gas_price)"] = gas_used * (max_fee - eff_gas)
            for k, v in terms.items():
                if v == 139300:
                    print(f"!!! MATCH !!! {k} = {v}")
                print(f"{k} = {v}")
        break
