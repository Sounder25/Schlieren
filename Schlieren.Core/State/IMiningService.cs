using System.Threading;

namespace Schlieren.Core.State;

public interface IMiningService
{
    Task MineAsync(CancellationToken ct = default);
}
