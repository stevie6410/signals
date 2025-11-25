using SDHome.Lib.Models;

namespace SDHome.Lib.Data
{
    public interface ITriggerEventsRepository
    {
        Task InsertAsync(TriggerEvent ev, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TriggerEvent>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TriggerEvent>> GetByDeviceAsync(
            string deviceId,
            int take = 100,
            CancellationToken cancellationToken = default);
    }
}
