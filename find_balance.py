import json

fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

for case_name, case_data in data.items():
    if "multiplier_1" in case_name:
        for post_state in case_data['post']['Cancun']:
            tx_idx = post_state['indexes']['data']
            tx_data = case_data['transaction']
            
            def get_tx_val(k):
                v = tx_data[k]
                return int(v[tx_idx] if isinstance(v, list) and len(v) > tx_idx else v[0] if isinstance(v, list) else v, 16)
            
            gas_limit = get_tx_val('gasLimit')
            max_fee = get_tx_val('maxFeePerGas')
            
            sender = tx_data['sender']
            pre_bal = int(case_data['pre'][sender]['balance'], 16)
            
            # get the actual expected final balance
            # the test gives a hash, which is the state root.
            # But the JSON state test structure has a 'post' with 'hash', and 'logs', 'txbytes'.
            # Wait, the expected final balance is NOT in the JSON explicitly! It's in the state root.
            # We don't have the expected balance string.
            pass
