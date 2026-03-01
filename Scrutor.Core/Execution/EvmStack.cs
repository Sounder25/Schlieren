using System.Numerics;

namespace Scrutor.Core.Execution;

/// <summary>
/// EVM execution stack (max 1024 items, 256-bit words)
/// </summary>
public sealed class EvmStack
{
    private readonly Stack<BigInteger> _stack = new();
    private const int MaxDepth = 1024;

    public int Count => _stack.Count;

    public bool TryPush(BigInteger value)
    {
        if (_stack.Count >= MaxDepth)
            return false;
        
        var mod = BigInteger.Pow(2, 256);
        var normalized = ((value % mod) + mod) % mod;
        _stack.Push(normalized);
        return true;
    }

    public void Push(BigInteger value)
    {
        if (!TryPush(value))
             throw new EvmStackOverflowException($"Stack overflow: max depth {MaxDepth}");
    }

    public bool TryPop(out BigInteger value)
    {
        if (_stack.Count == 0)
        {
            value = BigInteger.Zero;
            return false;
        }
        value = _stack.Pop();
        return true;
    }

    public BigInteger Pop()
    {
        if (!TryPop(out var val))
            throw new EvmStackUnderflowException("Stack underflow");
        return val;
    }

    public BigInteger Peek() => _stack.Count > 0 ? _stack.Peek() : BigInteger.Zero;

    public void Dup(int depth)
    {
        if (depth < 1 || depth > 16)
            throw new ArgumentOutOfRangeException(nameof(depth), "DUP depth must be 1-16");
        
        if (_stack.Count < depth)
            throw new EvmStackUnderflowException($"Stack underflow: needed {depth} items for DUP{depth}, have {_stack.Count}");

        var array = _stack.ToArray(); 
        var item = array[depth - 1];
        Push(item);
    }

    public void Swap(int depth)
    {
        if (depth < 1 || depth > 16)
            throw new ArgumentOutOfRangeException(nameof(depth), "SWAP depth must be 1-16");
        
        if (_stack.Count < depth + 1)
             throw new EvmStackUnderflowException($"Stack underflow: needed {depth + 1} items for SWAP{depth}, have {_stack.Count}");

        var array = _stack.ToArray();
        
        var top = array[0];
        var other = array[depth];
        
        array[0] = other;
        array[depth] = top;
        
        _stack.Clear();
        for (int i = array.Length - 1; i >= 0; i--)
        {
            _stack.Push(array[i]);
        }
    }

    public IReadOnlyList<BigInteger> SnapshotTopFirst() => _stack.ToArray();
}
