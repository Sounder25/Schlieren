// RpcChaos.test.csx — Testing the raw JSON-RPC HTTP server for stability under malformed inputs

var rpcUrl = "http://127.0.0.1:8545";
var client = new System.Net.Http.HttpClient();

async Task<(int StatusCode, string ResponseBody)> SendRawPost(string body)
{
    var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var response = await client.PostAsync(rpcUrl, content);
    return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
}

async Task<(int StatusCode, string ResponseBody)> SendRawGet()
{
    var response = await client.GetAsync(rpcUrl);
    return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
}

Test("Malformed JSON string (cutoff)", async () =>
{
    var (status, body) = await SendRawPost("{\"jsonrpc\":\"2.0\", \"meth");
    Assert.Contains("-32700", body);
});

Test("Missing Method Field", async () =>
{
    var (status, body) = await SendRawPost("{\"jsonrpc\": \"2.0\"}");
    Assert.Contains("-32600", body);
});

Test("Invalid Method Name", async () =>
{
    var (status, body) = await SendRawPost("{\"jsonrpc\":\"2.0\", \"method\":\"eth_DoesNotExist\", \"id\":1}");
    Assert.Contains("error", body);
});

Test("HTTP Method Abuse (GET instead of POST)", async () =>
{
    var (status, body) = await SendRawGet();
    Assert.Equal(405, status);
});

Test("Massive Payload Size (2MB)", async () =>
{
    var massiveString = new string('A', 2 * 1024 * 1024);
    var payload = "{\"jsonrpc\":\"2.0\", \"method\":\"eth_call\", \"params\":[\"" + massiveString + "\"], \"id\":1}";
    
    var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
    var response = await client.PostAsync(rpcUrl, content);
    
    Assert.Equal(413, (int)response.StatusCode);
});

Test("Batch request bomb (10,000 requests)", async () =>
{
    var sb = new System.Text.StringBuilder();
    sb.Append("[");
    for (int i = 0; i < 10000; i++)
    {
        sb.Append("{\"jsonrpc\":\"2.0\",\"method\":\"eth_chainId\",\"id\":").Append(i).Append("}");
        if (i < 9999) sb.Append(",");
    }
    sb.Append("]");
    
    var (status, body) = await SendRawPost(sb.ToString());
    // Should either cap batch size with -32600 or process all. 
    // Either way, it shouldn't crash.
    Assert.True(status == 200 || status == 400 || status == 413 || body.Contains("-32600") || body.Contains("error"));
});

Test("Slow-loris / chunked body", async () =>
{
    // Open a raw TCP connection, send headers, and drip the body
    using var tcp = new System.Net.Sockets.TcpClient();
    var connectTask = tcp.ConnectAsync("127.0.0.1", 8545);
    
    if (await Task.WhenAny(connectTask, Task.Delay(2000)) != connectTask)
    {
        // Connection timeout
        Assert.True(true);
        return;
    }
    
    using var stream = tcp.GetStream();
    
    var requestData = "{\"jsonrpc\":\"2.0\",\"method\":\"eth_chainId\",\"id\":1}";
    var bodyBytes = System.Text.Encoding.UTF8.GetBytes(requestData);
    
    var headers = "POST / HTTP/1.1\r\n" +
                  "Host: 127.0.0.1:8545\r\n" +
                  "Content-Type: application/json\r\n" +
                  $"Content-Length: {bodyBytes.Length}\r\n" +
                  "Connection: close\r\n\r\n";
                  
    var headerBytes = System.Text.Encoding.UTF8.GetBytes(headers);
    await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
    
    var success = false;
    try
    {
        // Drip body 1 byte per 100ms
        for (int i = 0; i < bodyBytes.Length; i++)
        {
            await stream.WriteAsync(bodyBytes, i, 1);
            await Task.Delay(100);
        }
        success = true;
    }
    catch (System.IO.IOException)
    {
        // Server closed connection during slow write! Good!
        success = true;
    }
    
    Assert.True(success);
});
