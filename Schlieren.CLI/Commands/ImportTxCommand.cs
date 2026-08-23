using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Schlieren.Core;
using Schlieren.Core.Ethereum;

namespace Schlieren.CLI.Commands;

/// <summary>
/// Import a real Ethereum mainnet transaction and reconstruct it as a Schlieren test case.
/// 
/// Usage:
///   schlieren import-tx 0xTRANSACTION_HASH [--rpc-url URL] [--etherscan-api-key KEY]
/// 
/// Flow:
///   1. Fetch transaction receipt from Ethereum RPC
///   2. Fetch runtime bytecode for target contract
///   3. Fetch pre-state via debug_traceTransaction (prestateTracer)
///   4. Fetch block environment (number, timestamp, baseFee, difficulty, etc.)
///   5. Materialize as Schlieren.Core.ExecutionCase
///   6. Write to fixtures/mainnet/{blockNumber}_{txHash}.json
/// 
/// Requirements:
///   - Ethereum RPC endpoint with debug_* namespace (archive node or Alchemy/Infura with tracing)
///   - Optional: Etherscan API key for verified source code metadata
/// </summary>
public static class ImportTxCommand
{
    public static Command Create()
    {
        var command = new Command("import-tx", "Import a real Ethereum transaction as a test case");

        var txHashArg = new Argument<string>(
            name: "transaction-hash",
            description: "Ethereum transaction hash (0x...)"
        );

        var rpcUrlOption = new Option<string>(
            name: "--rpc-url",
            description: "Ethereum RPC endpoint (must support debug_* namespace)",
            getDefaultValue: () => "https://eth-mainnet.g.alchemy.com/v2/YOUR_KEY"
        );

        var etherscanKeyOption = new Option<string?>(
            name: "--etherscan-api-key",
            description: "Etherscan API key (optional, for verified source metadata)"
        );

        var outputDirOption = new Option<string>(
            name: "--output-dir",
            description: "Where to write the imported fixture",
            getDefaultValue: () => "fixtures/mainnet"
        );

        command.AddArgument(txHashArg);
        command.AddOption(rpcUrlOption);
        command.AddOption(etherscanKeyOption);
        command.AddOption(outputDirOption);

        command.SetHandler(async (context) =>
        {
            var txHash = context.ParseResult.GetValueForArgument(txHashArg);
            var rpcUrl = context.ParseResult.GetValueForOption(rpcUrlOption)!;
            var etherscanKey = context.ParseResult.GetValueForOption(etherscanKeyOption);
            var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;

            await ExecuteAsync(txHash, rpcUrl, etherscanKey, outputDir, context.GetCancellationToken());
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string txHash,
        string rpcUrl,
        string? etherscanApiKey,
        string outputDir,
        CancellationToken ct)
    {
        Console.WriteLine($"[import-tx] Fetching transaction: {txHash}");

        var rpc = new EthereumRpcClient(rpcUrl);

        // Step 1: Get transaction receipt
        Console.WriteLine("[1/5] eth_getTransactionByHash");
        var tx = await rpc.GetTransactionByHashAsync(txHash, ct);
        if (tx == null)
        {
            Console.WriteLine($"❌ Transaction not found: {txHash}");
            return;
        }

        Console.WriteLine($"      Block: {tx.BlockNumber}");
        Console.WriteLine($"      From: {tx.From}");
        Console.WriteLine($"      To: {tx.To ?? "(contract creation)"}");
        Console.WriteLine($"      Value: {tx.Value} wei");
        Console.WriteLine($"      Gas: {tx.Gas}");
        Console.WriteLine($"      Input: {tx.Input.Length} bytes");

        // Step 2: Get receipt for actual gas used and logs
        Console.WriteLine("[2/5] eth_getTransactionReceipt");
        var receipt = await rpc.GetTransactionReceiptAsync(txHash, ct);
        if (receipt == null)
        {
            Console.WriteLine($"❌ Receipt not found (transaction may be pending)");
            return;
        }

        Console.WriteLine($"      Status: {(receipt.Status == 1 ? "SUCCESS" : "REVERT")}");
        Console.WriteLine($"      Gas Used: {receipt.GasUsed}");
        Console.WriteLine($"      Logs: {receipt.Logs.Count}");

        // Step 3: Get runtime bytecode for target contract
        string? targetAddress = tx.To;
        if (targetAddress == null)
        {
            // Contract creation — get created address from receipt
            targetAddress = receipt.ContractAddress;
            Console.WriteLine($"      Created: {targetAddress}");
        }

        Console.WriteLine("[3/5] eth_getCode");
        var runtimeCode = await rpc.GetCodeAsync(targetAddress!, tx.BlockNumber, ct);
        Console.WriteLine($"      Runtime bytecode: {runtimeCode.Length} bytes");

        // Step 4: Get pre-state via debug_traceTransaction
        Console.WriteLine("[4/5] debug_traceTransaction (prestateTracer)");
        var preState = await rpc.GetPreStateAsync(txHash, ct);
        Console.WriteLine($"      Accounts in pre-state: {preState.Count}");

        // Step 5: Get block environment
        Console.WriteLine("[5/5] eth_getBlockByNumber");
        var block = await rpc.GetBlockByNumberAsync(tx.BlockNumber, ct);
        Console.WriteLine($"      Timestamp: {block.Timestamp}");
        Console.WriteLine($"      BaseFee: {block.BaseFeePerGas ?? 0}");
        Console.WriteLine($"      Difficulty: {block.Difficulty}");

        // Optional: fetch verified source from Etherscan
        string? sourceCode = null;
        if (etherscanApiKey != null && targetAddress != null)
        {
            Console.WriteLine("[etherscan] getsourcecode");
            var etherscan = new EtherscanClient(etherscanApiKey);
            sourceCode = await etherscan.GetSourceCodeAsync(targetAddress, ct);
            if (sourceCode != null)
            {
                Console.WriteLine($"      ✓ Verified source available ({sourceCode.Length} chars)");
            }
        }

        // Materialize as ExecutionCase
        var testCase = new MainnetExecutionCase
        {
            Network = "Ethereum Mainnet",
            TransactionHash = txHash,
            BlockNumber = tx.BlockNumber,
            Fork = DetermineFork(tx.BlockNumber), // helper to map block → fork
            
            Transaction = new TransactionInfo
            {
                From = tx.From,
                To = tx.To,
                Value = tx.Value,
                GasLimit = tx.Gas,
                GasPrice = tx.GasPrice,
                MaxFeePerGas = tx.MaxFeePerGas,
                MaxPriorityFeePerGas = tx.MaxPriorityFeePerGas,
                Input = tx.Input,
                Nonce = tx.Nonce,
                ChainId = tx.ChainId
            },

            Block = new BlockEnvironment
            {
                Number = block.Number,
                Timestamp = block.Timestamp,
                BaseFee = block.BaseFeePerGas ?? 0,
                Difficulty = block.Difficulty,
                GasLimit = block.GasLimit,
                Coinbase = block.Miner
            },

            PreState = preState,

            TargetContract = new ContractInfo
            {
                Address = targetAddress!,
                RuntimeBytecode = runtimeCode,
                VerifiedSource = sourceCode
            },

            ExpectedResult = new ExecutionResult
            {
                Success = receipt.Status == 1,
                GasUsed = receipt.GasUsed,
                Logs = receipt.Logs,
                ReturnData = receipt.Status == 1 ? null : receipt.RevertReason
            }
        };

        // Write to disk
        Directory.CreateDirectory(outputDir);
        var fileName = $"{tx.BlockNumber}_{txHash.Substring(0, 10)}.json";
        var outputPath = Path.Combine(outputDir, fileName);

        var json = JsonSerializer.Serialize(testCase, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(outputPath, json, ct);

        Console.WriteLine();
        Console.WriteLine($"✓ Imported transaction");
        Console.WriteLine($"  Written to: {outputPath}");
        Console.WriteLine();
        Console.WriteLine($"  Run with:");
        Console.WriteLine($"    schlieren run-case {outputPath}");
    }

    private static string DetermineFork(long blockNumber)
    {
        // Ethereum mainnet fork activation blocks
        // https://ethereum.org/en/history/
        return blockNumber switch
        {
            >= 21_000_000 => "osaka",      // hypothetical
            >= 19_426_589 => "cancun",     // March 13, 2024
            >= 17_034_870 => "shanghai",   // April 12, 2023
            >= 15_537_394 => "paris",      // September 15, 2022 (The Merge)
            >= 12_965_000 => "london",     // August 5, 2021
            >= 12_244_000 => "berlin",     // April 15, 2021
            >= 9_069_000 => "istanbul",    // December 8, 2019
            >= 7_280_000 => "constantinople", // February 28, 2019
            >= 4_370_000 => "byzantium",   // October 16, 2017
            >= 2_675_000 => "homestead",   // March 14, 2016
            _ => "frontier"
        };
    }
}
