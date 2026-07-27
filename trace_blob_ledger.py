import json
fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

for case_name, case_data in data.items():
    if "tx_value_0-tx_max_priority_fee_per_gas_0-tx_max_fee_per_gas_14-access_list" in case_name:
        for post in case_data['post']['Cancun']:
            tx_idx = post['indexes']['data']
            tx_data = case_data['transaction']
            
            def get_tx_val(k):
                v = tx_data[k]
                return int(v[tx_idx] if isinstance(v, list) and len(v) > tx_idx else v[0] if isinstance(v, list) else v, 16)

            gas_limit = get_tx_val('gasLimit')
            max_fee_per_gas = get_tx_val('maxFeePerGas')
            max_priority_fee = get_tx_val('maxPriorityFeePerGas')
            max_fee_per_blob = get_tx_val('maxFeePerBlobGas')
            tx_value = get_tx_val('value')
            sender = tx_data['sender']
            
            bvh = tx_data['blobVersionedHashes']
            bvh = bvh[tx_idx] if isinstance(bvh, list) and len(bvh) > tx_idx else bvh[0] if isinstance(bvh, list) else bvh
            blob_count = 1 if isinstance(bvh, str) else len(bvh)
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
            
            pre = case_data['pre'].get(sender, {})
            initial_balance = int(pre.get('balance', '0x0'), 16)
            
            expected_post_balance = int(post['hash'], 16) # wait, hash is state root.
            break
        break
