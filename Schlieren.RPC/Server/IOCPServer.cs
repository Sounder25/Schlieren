using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Schlieren.RPC.Handlers;

namespace Schlieren.RPC.Server
{
    /// <summary>
    /// Windows-native HTTP server using I/O Completion Ports (IOCP)
    /// Optimized for high-concurrency workloads on Ryzen 9 7900X (24 threads)
    /// </summary>
    public sealed class IOCPServer : IDisposable
    {
        private readonly int _port;
        private readonly RpcRouter _router;
        private readonly CancellationTokenSource _cts;
        private readonly Socket _listenerSocket;
        private readonly ConcurrentBag<Socket> _activeConnections;
        private readonly SemaphoreSlim _connectionSemaphore;
        private readonly ILogger _logger;
        private bool _disposed;

        // Performance tuning for "The Beast" (Ryzen 9 7900X)
        private const int MaxConcurrentConnections = 10000;
        private const int BufferSize = 8192;
        private const int BacklogSize = 1000;

        public IOCPServer(int port, RpcRouter router, ILogger logger)
        {
            _port = port;
            _router = router;
            _logger = logger;
            _cts = new CancellationTokenSource();
            _activeConnections = new ConcurrentBag<Socket>();
            _connectionSemaphore = new SemaphoreSlim(MaxConcurrentConnections);

            // SECTION: Create and configure listener socket with IOCP
            _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            
            // Windows async sockets use IOCP by default in .NET (no explicit flag needed)
            _listenerSocket.NoDelay = true; // Disable Nagle's algorithm for lower latency
            
            // Socket options for high-throughput
            _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }

        /// <summary>
        /// Starts the IOCP server and begins accepting connections
        /// </summary>
        public async Task Start()
        {
            var endpoint = new IPEndPoint(IPAddress.Any, _port);
            _listenerSocket.Bind(endpoint);
            _listenerSocket.Listen(BacklogSize);

            _logger.LogInformation("[IOCP Server] Listening on port {Port}", _port);
            _logger.LogInformation("[IOCP Server] Max concurrent connections: {MaxConnections}", MaxConcurrentConnections);
            _logger.LogInformation("[IOCP Server] Optimized for Windows I/O Completion Ports");

            // SECTION: Accept loop using async IOCP operations
            while (!_cts.Token.IsCancellationRequested)
            {
                // Throttle connections to prevent resource exhaustion
                try
                {
                    await _connectionSemaphore.WaitAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // The permit is now held. It must be released either by HandleConnection's
                // finally block (on a successful accept) or right here (on a failed one) —
                // otherwise a transient accept error permanently shrinks the connection pool.
                Socket clientSocket;
                try
                {
                    // AcceptAsync uses IOCP under the hood on Windows
                    clientSocket = await _listenerSocket.AcceptAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _connectionSemaphore.Release();
                    break;
                }
                catch (Exception ex)
                {
                    _connectionSemaphore.Release();
                    _logger.LogError(ex, "[IOCP Server] Accept error");
                    continue;
                }

                _activeConnections.Add(clientSocket);

                // Fire and forget - handle each connection independently
                _ = Task.Run(() => HandleConnection(clientSocket), _cts.Token);
            }
        }

        /// <summary>
        /// Handles individual client connection with IOCP async I/O
        /// </summary>
        private async Task HandleConnection(Socket clientSocket)
        {
            try
            {
                using (clientSocket)
                {
                    var buffer = new byte[BufferSize];
                    using var rawBytes = new MemoryStream();

                    // SECTION: Read HTTP request using IOCP
                    int bytesRead;
                    int headerEndIndex = -1;

                    // Defense against Slowloris: Enforce an absolute 5-second timeout for the entire request read phase
                    using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                    try
                    {
                        while ((bytesRead = await clientSocket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None, readCts.Token)) > 0)
                        {
                            rawBytes.Write(buffer, 0, bytesRead);

                            // Decode the FULL accumulated byte stream every time, not just the new
                            // chunk — a multi-byte UTF-8 sequence split across two TCP reads decodes
                            // correctly once all its bytes have arrived, instead of turning into
                            // U+FFFD replacement characters on each half.
                            var currentData = Encoding.UTF8.GetString(rawBytes.GetBuffer(), 0, (int)rawBytes.Length);
                            headerEndIndex = currentData.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                        if (headerEndIndex != -1)
                        {
                            // Check if we have complete body based on Content-Length.
                            // HTTP headers are ASCII by spec, so the char-length of the header
                            // portion equals its byte length — use that to get the true byte
                            // offset where the body starts, since currentData.Length is a UTF-16
                            // char count and can undercount the real byte size of a non-ASCII body.
                            var headerText = currentData.Substring(0, headerEndIndex);
                            var bodyStartByteIndex = Encoding.UTF8.GetByteCount(headerText) + 4;
                            if (HasCompleteBody(headerText, (int)rawBytes.Length, bodyStartByteIndex))
                            {
                                break;
                            }
                        }

                        // Prevent infinite reading — enforce the 1MB cap on actual bytes received,
                        // not decoded UTF-16 chars (which can undercount a multi-byte body).
                        if (rawBytes.Length > 1024 * 1024) // 1MB limit
                        {
                            await SendErrorResponse(clientSocket, 413, "Payload Too Large");
                            return;
                        } // End of if > 1MB
                    } // End of while loop
                    } // End of try block
                    catch (OperationCanceledException)
                    {
                        // Slowloris timeout reached
                        await SendErrorResponse(clientSocket, 408, "Request Timeout");
                        return;
                    }

                    if (bytesRead == 0 || headerEndIndex == -1)
                    {
                        return; // Client disconnected or invalid request
                    }

                    // SECTION: Parse HTTP request
                    var httpRequest = Encoding.UTF8.GetString(rawBytes.GetBuffer(), 0, (int)rawBytes.Length);
                    var (method, body) = ParseHttpRequest(httpRequest, headerEndIndex);

                    if (method != "POST")
                    {
                        await SendErrorResponse(clientSocket, 405, "Method Not Allowed");
                        return;
                    }

                    // SECTION: Process JSON-RPC request
                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    requestCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second request timeout

                    var jsonResponse = await _router.ProcessRequest(body, requestCts.Token);

                    // SECTION: Send HTTP response using IOCP
                    await SendHttpResponse(clientSocket, 200, jsonResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[IOCP Server] Connection error");
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        // SECTION: HTTP Protocol Utilities

        /// <summary>
        /// Byte-based body-completeness check. <paramref name="headerText"/> is the ASCII
        /// header block (used only to extract Content-Length); <paramref name="totalByteCount"/>
        /// and <paramref name="bodyStartByteIndex"/> are raw byte offsets/counts, not UTF-16
        /// char counts, so this is correct for bodies containing multi-byte content.
        /// </summary>
        private static bool HasCompleteBody(string headerText, int totalByteCount, int bodyStartByteIndex)
        {
            var contentLengthMatch = System.Text.RegularExpressions.Regex.Match(
                headerText,
                @"Content-Length:\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (!contentLengthMatch.Success)
            {
                return true; // No body expected
            }

            // Content-Length is attacker-controlled and the regex allows unbounded digits;
            // int.Parse on something like "99999999999999999999" used to throw an uncaught
            // OverflowException instead of failing cleanly. Treat anything unparsable or over
            // the 1MB cap as "not complete yet" — the caller's byte-count cap rejects it with
            // a clean 413 as soon as the actual body exceeds the limit.
            if (!long.TryParse(contentLengthMatch.Groups[1].Value, out var contentLength) ||
                contentLength < 0 || contentLength > 1024 * 1024)
            {
                return false;
            }

            var currentBodyLength = totalByteCount - bodyStartByteIndex;
            return currentBodyLength >= contentLength;
        }

        private static (string Method, string Body) ParseHttpRequest(string httpRequest, int headerEndIndex)
        {
            var lines = httpRequest.Substring(0, headerEndIndex).Split("\r\n");
            var requestLine = lines[0].Split(' ');
            var method = requestLine.Length > 0 ? requestLine[0] : "UNKNOWN";

            var bodyStartIndex = headerEndIndex + 4;
            var body = bodyStartIndex < httpRequest.Length
                ? httpRequest.Substring(bodyStartIndex)
                : string.Empty;

            return (method, body);
        }

        private async Task SendHttpResponse(Socket socket, int statusCode, string jsonBody)
        {
            var statusText = statusCode switch
            {
                200 => "OK",
                405 => "Method Not Allowed",
                413 => "Payload Too Large",
                500 => "Internal Server Error",
                _ => "Unknown"
            };

            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            var response = new StringBuilder();
            
            response.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            response.AppendLine("Content-Type: application/json");
            response.AppendLine($"Content-Length: {bodyBytes.Length}");
            response.AppendLine("Connection: close");
            response.AppendLine();

            var headerBytes = Encoding.UTF8.GetBytes(response.ToString());
            var fullResponse = new byte[headerBytes.Length + bodyBytes.Length];
            
            Buffer.BlockCopy(headerBytes, 0, fullResponse, 0, headerBytes.Length);
            Buffer.BlockCopy(bodyBytes, 0, fullResponse, headerBytes.Length, bodyBytes.Length);

            await socket.SendAsync(new ArraySegment<byte>(fullResponse), SocketFlags.None);
        }

        private async Task SendErrorResponse(Socket socket, int statusCode, string statusText)
        {
            var errorJson = "{\"error\":\"" + statusText + "\"}";
            await SendHttpResponse(socket, statusCode, errorJson);
        }

        /// <summary>
        /// Gracefully shuts down the server
        /// </summary>
        public async Task Stop()
        {
            _logger.LogInformation("[IOCP Server] Shutting down...");
            _cts.Cancel();

            // Close all active connections
            while (_activeConnections.TryTake(out var socket))
            {
                try
                {
                    socket.Close();
                }
                catch { /* Best effort */ }
            }

            _listenerSocket.Close();

            // Wait briefly for cleanup
            await Task.Delay(100);
            _logger.LogInformation("[IOCP Server] Shutdown complete");
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cts.Cancel();
            _cts.Dispose();
            _listenerSocket.Dispose();
            _connectionSemaphore.Dispose();

            _disposed = true;
        }
    }
}
