using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Scrutor.BurstTest;

/// <summary>
/// Burst testing tool for Lane 2 RPC Gateway verification
/// Target: 10,000 concurrent requests without dropped connections
/// </summary>
class Program
{
    private record JsonRpcRequest(string jsonrpc, int id, string method, object[]? @params);
    private static readonly HttpClient _httpClient = new();
    private static int _successCount = 0;
    private static int _failureCount = 0;
    private static readonly object _lock = new();

    static async Task<int> Main(string[] args)
    {
        var endpoint = args.Length > 0 ? args[0] : "http://localhost:8545";
        var requestCount = args.Length > 1 ? int.Parse(args[1]) : 10_000;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        Scrutor RPC Gateway - Burst Test (Lane 2)             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Target Endpoint: {endpoint}");
        Console.WriteLine($"Total Requests:  {requestCount:N0}");
        Console.WriteLine($"Concurrency:     {Environment.ProcessorCount * 100}");
        Console.WriteLine();
        // Console.WriteLine("Press any key to begin...");
        // Console.ReadKey(true);
        Console.WriteLine("Starting burst test automatically...");
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();



        // SECTION: Functional Verification
        Console.WriteLine("--- Performing Functional Verification ---");
        await VerifyTransactionFlow(endpoint);
        Console.WriteLine("--- Verification Passed ---\n");

        // SECTION: Execute burst test
        await RunBurstTest(endpoint, requestCount);

        stopwatch.Stop();

        // SECTION: Display results
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      Test Results                            ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"Total Time:      {stopwatch.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"Successful:      {_successCount:N0}");
        Console.WriteLine($"Failed:          {_failureCount:N0}");
        Console.WriteLine($"Success Rate:    {(_successCount * 100.0 / requestCount):F2}%");
        Console.WriteLine($"Throughput:      {(requestCount * 1000.0 / stopwatch.ElapsedMilliseconds):F2} req/s");
        Console.WriteLine();

        // Determine pass/fail
        bool passed = _successCount == requestCount && _failureCount == 0;
        
        if (passed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ PASS - L2_RPC_ACTIVE flag criteria met!");
            Console.WriteLine("  Server survived 10K-request burst without dropping connections");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("✗ FAIL - Test criteria not met");
            Console.WriteLine($"  {_failureCount} requests failed or were dropped");
            Console.ResetColor();
            return 1;
        }
    }

    static async Task RunBurstTest(string endpoint, int totalRequests)
    {
        var tasks = new List<Task>(totalRequests);

        // Distribute requests across all 4 RPC methods
        var methods = new[] 
        { 
            "eth_chainId", 
            "eth_blockNumber", 
            "eth_accounts",
            "eth_getBalance" 
        };

        var testAddress = "0x1234567890123456789012345678901234567890";

        for (int i = 0; i < totalRequests; i++)
        {
            var methodIndex = i % methods.Length;
            var method = methods[methodIndex];
            
            object[]? parameters = method == "eth_getBalance" 
                ? new object[] { testAddress, "latest" }
                : null;

            tasks.Add(SendRequest(endpoint, i, method, parameters));

            // Report progress
            if ((i + 1) % 1000 == 0)
            {
                Console.Write($"\rDispatched: {i + 1:N0} / {totalRequests:N0}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Waiting for all requests to complete...");

        await Task.WhenAll(tasks);
    }

    static async Task SendRequest(string endpoint, int id, string method, object[]? parameters)
    {
        try
        {
            var request = new JsonRpcRequest("2.0", id, method, parameters);
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            
            // Basic validation - ensure it's valid JSON and contains "result"
            if (!string.IsNullOrEmpty(responseBody) && responseBody.Contains("\"result\""))
            {
                IncrementSuccess();
            }
            else
            {
                IncrementFailure();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[Error] Request {id} failed: {ex.Message}");
            IncrementFailure();
        }
    }

    static void IncrementSuccess()
    {
        lock (_lock)
        {
            _successCount++;
        }
    }

    static void IncrementFailure()
    {
        lock (_lock)
        {
            _failureCount++;
        }
    }

    static async Task VerifyTransactionFlow(string endpoint)
    {
        // 1. Get Nonce
        var nonceReq = new JsonRpcRequest("2.0", 100, "eth_getTransactionCount", new object[] { "0x1234567890123456789012345678901234567890", "latest" });
        var nonceRes = await SendJson(endpoint, nonceReq);
        Console.WriteLine($"[Verify] GetNonce: {nonceRes}");

        if (!nonceRes.Contains("result")) throw new Exception("GetNonce failed");

        // 2. Send Raw Transaction (Hardcoded Legacy Tx: Nonce 0, Gas 21000, Value 1)
        var rawTx = "0xdf800182520894353535353535353535353535353535353535353501801b8080";
        var sendReq = new JsonRpcRequest("2.0", 101, "eth_sendRawTransaction", new object[] { rawTx });
        var sendRes = await SendJson(endpoint, sendReq);
        Console.WriteLine($"[Verify] SendRawTransaction: {sendRes}");

        if (!sendRes.Contains("result") || sendRes.Contains("error")) throw new Exception($"SendRawTransaction failed: {sendRes}");
        
        Console.WriteLine("[Verify] Transaction submitted successfully (Hash returned)");
    }

    static async Task<string> SendJson(string endpoint, JsonRpcRequest rpcReq)
    {
        var json = JsonSerializer.Serialize(rpcReq);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
