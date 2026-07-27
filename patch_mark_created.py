import sys

def patch_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    old_target = """            // EIP-161: a newly created account starts at nonce 1 before its
            // initialization code executes. Keep this mutation in the creation
            // frame overlay so a failed creation rolls it back with the frame.
            overlay.SetNonce(contractAddress, 1);
        }"""
        
    new_target = """            // EIP-161: a newly created account starts at nonce 1 before its
            // initialization code executes. Keep this mutation in the creation
            // frame overlay so a failed creation rolls it back with the frame.
            overlay.SetNonce(contractAddress, 1);
            
            // EIP-6780: Mark the account as created in this transaction
            overlay.MarkCreated(contractAddress);
        }"""
        
    content = content.replace(old_target, new_target)

    with open(filepath, 'w') as f:
        f.write(content)

patch_file("Scrutor.Core/Execution/StateTransition.cs")
print("Done")
