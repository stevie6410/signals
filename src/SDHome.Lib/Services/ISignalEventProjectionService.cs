using SDHome.Lib.Models;

namespace SDHome.Lib.Services
{
    public interface ISignalEventProjectionService
    {
        Task ProjectAsync(SignalEvent ev, CancellationToken cancellationToken = default);
    }
}
