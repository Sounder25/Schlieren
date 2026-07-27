import sys

def patch_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    # Split into ApplyTransactionAsync and the rest
    parts = content.split("    private async Task<ExecutionResult> ApplyTransactionWithFrameAsync(")
    
    apply_tx = parts[0]
    rest = "    private async Task<ExecutionResult> ApplyTransactionWithFrameAsync(" + parts[1]

    apply_tx = apply_tx.replace(
        "var coinbaseBalance = await state.GetBalanceAsync(block.Coinbase, ct);",
        "var coinbaseBalance = await txOverlay.GetBalanceAsync(block.Coinbase, ct);"
    )
    apply_tx = apply_tx.replace(
        "state.SetBalance(block.Coinbase, coinbaseBalance + minerFee);",
        "txOverlay.SetBalance(block.Coinbase, coinbaseBalance + minerFee);"
    )

    with open(filepath, 'w') as f:
        f.write(apply_tx + rest)

patch_file("Scrutor.Core/Execution/StateTransition.cs")
print("Done")
