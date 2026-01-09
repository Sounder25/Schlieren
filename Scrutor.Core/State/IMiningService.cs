using System.Threading;

namespace Scrutor.Core.State;

public interface IMiningService
{
    Task MineAsync(CancellationToken ct = default);
}
