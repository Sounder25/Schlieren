import json

fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

for case_name, case_data in data.items():
    if "tx_value_0-tx_max_priority_fee_per_gas_0-tx_max_fee_per_gas_14-access_list" in case_name:
        for post_state in case_data['post']['Cancun']:
            tx_idx = post_state['indexes']['data']
            tx_data = case_data['transaction']
            
            def get_tx_val(k):
                v = tx_data[k]
                return int(v[tx_idx] if isinstance(v, list) and len(v) > tx_idx else v[0] if isinstance(v, list) else v, 16)
            
            gas_limit = get_tx_val('gasLimit')
            max_fee = get_tx_val('maxFeePerGas')
            max_blob = get_tx_val('maxFeePerBlobGas')
            
            bvh = tx_data['blobVersionedHashes']
            bvh = bvh[tx_idx] if isinstance(bvh, list) and len(bvh) > tx_idx else bvh[0] if isinstance(bvh, list) else bvh
            blob_count = 1 if isinstance(bvh, str) else len(bvh)
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
            eff_gas = min(max_fee, base_fee + 0) # priority fee is 0
            
            sender = tx_data['sender']
            pre_bal = int(case_data['pre'][sender]['balance'], 16)
            
            # Scrutor logic
            max_gas_cost = gas_limit * max_fee
            actual_blob_fee = blob_gas_used * blob_base_fee
            upfront = max_gas_cost + actual_blob_fee
            mid_bal = pre_bal - upfront
            mid_bal += 100 # 100 wei mid execution
            
            gas_used = 27200
            gas_refund_amt = (gas_limit - gas_used) * eff_gas
            price_diff = gas_limit * (max_fee - eff_gas)
            
            scrutor_final = mid_bal + gas_refund_amt + price_diff
            
            # We know the expected final balance from the delta: scrutor_final - 139300 = expected
            # But let's check what EIP-4844 wants
            eip_upfront = gas_limit * max_fee + blob_gas_used * max_blob
            eip_mid = pre_bal - eip_upfront + 100
            blob_refund = blob_gas_used * (max_blob - blob_base_fee)
            eip_final = eip_mid + gas_refund_amt + price_diff + blob_refund
            
            print(f"maxGasCost = {max_gas_cost}")
            print(f"actualBlobFee = {actual_blob_fee}")
            print(f"maxBlobCost = {blob_gas_used * max_blob}")
            print(f"upfront diff = {upfront - eip_upfront}")
            print(f"final diff (EIP - Scrutor) = {eip_final - scrutor_final}")
            break
        break
