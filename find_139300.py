import json

fixture_path = r'c:\projects\Scrutor\fixtures\state_tests\cancun\eip4844_blobs\blob_txs\blob_gas_subtraction_tx.json'
with open(fixture_path, 'r') as f:
    data = json.load(f)

for case_name, case_data in data.items():
    if "fork_Cancun" not in case_name: continue
    
    post_states = case_data['post']['Cancun']
    tx_data = case_data['transaction']
    env = case_data['env']
    
    for post in post_states:
        tx_idx = post['indexes']['data']
        
        def get_tx_val(k):
            v = tx_data[k]
            if isinstance(v, list): return int(v[tx_idx] if len(v) > tx_idx else v[0], 16)
            return int(v, 16)
            
        try:
            gas_limit = get_tx_val('gasLimit')
            max_fee = get_tx_val('maxFeePerGas')
            max_blob = get_tx_val('maxFeePerBlobGas')
        except: continue
        
        bvh = tx_data['blobVersionedHashes']
        bvh = bvh[tx_idx] if isinstance(bvh, list) and len(bvh) > tx_idx else bvh[0] if isinstance(bvh, list) else bvh
        blob_count = 1 if isinstance(bvh, str) else len(bvh)
        blob_gas_used = blob_count * 131072
        
        base_fee = int(env['currentBaseFee'], 16)
        
        # Is the delta somehow related to unused_gas * max_fee?
        # Let's just print the differences between various upfront deductions.
        max_exec = gas_limit * max_fee
        max_blob_res = blob_gas_used * max_blob
        
        # If the delta is 139300, print it
        if 139300 in [max_exec, max_blob_res, gas_limit, max_fee, base_fee, blob_gas_used]:
            print("FOUND 139300 literally in:", case_name)
            
        # Check combinations
        for gas_used in range(21000, 30000):
            unused = gas_limit - gas_used
            if unused * max_fee == 139300:
                print(f"unused * max_fee == 139300 at gas_used={gas_used}")
            if unused * base_fee == 139300:
                print(f"unused * base_fee == 139300 at gas_used={gas_used}")
            if gas_used * max_fee == 139300:
                print(f"gas_used * max_fee == 139300 at gas_used={gas_used} in {case_name}")
            if gas_used * base_fee == 139300:
                print(f"gas_used * base_fee == 139300 at gas_used={gas_used} in {case_name}")
                
        # What if it's 19900 * 7? 19900 is gas_used for what?
        if 19900 * 7 == 139300:
            pass # we already know this
            
        # Is 139300 related to price_diff?
        # Let's break
