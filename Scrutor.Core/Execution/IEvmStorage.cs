using System.Numerics;

namespace Scrutor.Core.Execution;

/// <summary>
/// EVM storage interface (persistent contract state).
/// LoadAsync is async to support forking IO.
/// </summary>
public interface IEvmStorage
{
    ValueTask<BigInteger> LoadAsync(BigInteger key);
    void Store(BigInteger key, BigInteger value);
}