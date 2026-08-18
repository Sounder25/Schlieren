using System.Net.Http.Json;
using System.Numerics;
using Polly;
using Polly.Retry;
using Schlieren.Core.Models;
using Schlieren.Core.Primitives;

namespace Schlieren.Core.Forking;

public class ForkProvider : IForkProvider
{
    private readonly HttpClient _client;
    private readonly IBlockCache _cache;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public ForkProvider(HttpClient client, IBlockCache cache)
    {
        _client = client;
        _cache = cache;
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<ulong> GetLatestBlockNumberAsync(CancellationToken ct = default)
    {
        var request = CreateRequest("eth_blockNumber", Array.Empty<object>());
        var response = await ExecuteRequestAsync<string>(request, ct);
        return ParseUlong(response);
    }

    public async Task<Block?> GetBlockByNumberAsync(ulong number, CancellationToken ct = default)
    {
        if (_cache.TryGetBlock(number, out var cached)) 
            return cached;

        var request = CreateRequest("eth_getBlockByNumber", new object[] { $"0x{number:x}", false });
        var response = await ExecuteRequestAsync<ForkBlockDto>(request, ct);
        var block = response?.ToCanonical();
        
        if (block != null) 
            _cache.CacheBlock(block);
            
        return block;
    }

    public async Task<Block?> GetBlockByHashAsync(string hash, CancellationToken ct = default)
    {
        var request = CreateRequest("eth_getBlockByHash", new object[] { hash, false });
        var response = await ExecuteRequestAsync<ForkBlockDto>(request, ct);
        var block = response?.ToCanonical();
        
        if (block != null) 
            _cache.CacheBlock(block);
            
        return block;
    }

    public async Task<BigInteger> GetBalanceAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default)
    {
        var request = CreateRequest("eth_getBalance", new object[] { address.ToString(), ToBlockTag(blockNumber) });
        var response = await ExecuteRequestAsync<string>(request, ct);
        return ParseBigInt(response);
    }

    public async Task<ulong> GetTransactionCountAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default)
    {
        var request = CreateRequest("eth_getTransactionCount", new object[] { address.ToString(), ToBlockTag(blockNumber) });
        var response = await ExecuteRequestAsync<string>(request, ct);
        return ParseUlong(response);
    }

    public async Task<byte[]> GetCodeAsync(Address address, ulong? blockNumber = null, CancellationToken ct = default)
    {
        var request = CreateRequest("eth_getCode", new object[] { address.ToString(), ToBlockTag(blockNumber) });
        var response = await ExecuteRequestAsync<string>(request, ct);
        return string.IsNullOrEmpty(response) ? Array.Empty<byte>() : Convert.FromHexString(response.StartsWith("0x") ? response[2..] : response);
    }

    public async Task<BigInteger> GetStorageAtAsync(Address address, BigInteger key, ulong? blockNumber = null, CancellationToken ct = default)
    {
        var request = CreateRequest("eth_getStorageAt", new object[] { address.ToString(), $"0x{key:x}", ToBlockTag(blockNumber) });
        var response = await ExecuteRequestAsync<string>(request, ct);
        return ParseBigInt(response);
    }

    private static string ToBlockTag(ulong? blockNumber) => blockNumber.HasValue ? $"0x{blockNumber.Value:x}" : "latest";

    private static object CreateRequest(string method, object[] parameters)
    {
        return new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
            id = 1
        };
    }

    private async Task<T?> ExecuteRequestAsync<T>(object payload, CancellationToken ct)
    {
        var response = await _retryPolicy.ExecuteAsync(async (token) =>
        {
            var res = await _client.PostAsJsonAsync("", payload, token);
            res.EnsureSuccessStatusCode();
            return res;
        }, ct);

        var content = await response.Content.ReadFromJsonAsync<RpcResponse<T>>(cancellationToken: ct);
        return content != null ? content.Result : default;
    }

    private static ulong ParseUlong(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return 0;
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return ulong.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out var val) ? val : 0;
    }

    private static BigInteger ParseBigInt(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "0x") return BigInteger.Zero;
        var clean = hex.StartsWith("0x") ? hex[2..] : hex;
        return BigInteger.Parse("00" + clean, System.Globalization.NumberStyles.HexNumber);
    }
}
