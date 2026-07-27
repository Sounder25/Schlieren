import json
import os

fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

for case_name, case_data in data.items():
    if "tx_value_0-tx_max_priority_fee_per_gas_0-tx_max_fee_per_gas_14" in case_name:
        for post in case_data['post']['Cancun']:
            tx_idx = post['indexes']['data']
            tx_data = case_data['transaction']
            
            def get_tx_val(k):
                v = tx_data[k]
                return v[tx_idx] if isinstance(v, list) and len(v) > tx_idx else v[0] if isinstance(v, list) else v

            gas_limit = int(get_tx_val('gasLimit'), 16)
            max_fee_per_gas = int(get_tx_val('maxFeePerGas'), 16)
            max_priority_fee = int(get_tx_val('maxPriorityFeePerGas'), 16)
            max_fee_per_blob = int(get_tx_val('maxFeePerBlobGas'), 16)
            tx_value = int(get_tx_val('value'), 16)
            
            bvh = get_tx_val('blobVersionedHashes')
            if isinstance(bvh, str):
                blob_count = 1
            elif isinstance(bvh, list):
                blob_count = len(bvh)
            else:
                blob_count = 0
            
            blob_gas_used = blob_count * 131072
            
            env = case_data['env']
            base_fee = int(env['currentBaseFee'], 16)
            excess_blob_gas = int(env.get('currentExcessBlobGas', '0x0'), 16)
            
            def fake_exp(f, n, d):
                i, out, acc = 1, 0, f * d
                while acc > 0:
                    out += acc
                    acc = (acc * n) // (d * i)
                    i += 1
                return out // d
            
            blob_base_fee = fake_exp(1, excess_blob_gas, 3338477)
            effective_gas_price = min(max_fee_per_gas, base_fee + max_priority_fee)
            
            print(f"--- {case_name} ---")
            print(f"gas_limit = {gas_limit}")
            print(f"max_fee_per_gas = {max_fee_per_gas}")
            print(f"max_priority_fee = {max_priority_fee}")
            print(f"max_fee_per_blob = {max_fee_per_blob}")
            print(f"blob_count = {blob_count}")
            print(f"blob_gas_used = {blob_gas_used}")
            print(f"base_fee = {base_fee}")
            print(f"blob_base_fee = {blob_base_fee}")
            print(f"effective_gas_price = {effective_gas_price}")
            
            print(f"Terms:")
            print(f"  gas_limit * max_fee_per_gas = {gas_limit * max_fee_per_gas}")
            print(f"  blob_gas_used * max_fee_per_blob = {blob_gas_used * max_fee_per_blob}")
            print(f"  blob_gas_used * blob_base_fee = {blob_gas_used * blob_base_fee}")
            print(f"  blob_gas_used * (max_fee_per_blob - blob_base_fee) = {blob_gas_used * (max_fee_per_blob - blob_base_fee)}")
            
            gas_used = 21000 + 4400 # typical for this case (21000 + access list)
            unused_gas = gas_limit - gas_used
            print(f"  If gas_used = {gas_used}:")
            print(f"    gas_used * effective_gas_price = {gas_used * effective_gas_price}")
            print(f"    unused_gas * max_fee_per_gas = {unused_gas * max_fee_per_gas}")
            print(f"    unused_gas * effective_gas_price = {unused_gas * effective_gas_price}")
            print(f"    gas_limit * (max_fee_per_gas - effective_gas_price) = {gas_limit * (max_fee_per_gas - effective_gas_price)}")
            print(f"    gas_used * (max_fee_per_gas - effective_gas_price) = {gas_used * (max_fee_per_gas - effective_gas_price)}")
            print(f"    unused_gas * (max_fee_per_gas - effective_gas_price) = {unused_gas * (max_fee_per_gas - effective_gas_price)}")
            break
        break
