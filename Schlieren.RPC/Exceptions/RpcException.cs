namespace Schlieren.RPC;

/// <summary>
/// Custom exception for RPC-specific errors
/// </summary>
public sealed class RpcException : Exception
{
    public int ErrorCode { get; }

    public RpcException(int errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
