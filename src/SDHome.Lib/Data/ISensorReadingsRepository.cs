using SDHome.Lib.Models;


namespace SDHome.Lib.Data
{
    public interface ISensorReadingsRepository
    {
        Task InsertManyAsync(IEnumerable<SensorReading> readings, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SensorReading>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SensorReading>> GetByDeviceAndMetricAsync(
            string deviceId,
            string metric,
            int take = 100,
            CancellationToken cancellationToken = default);
    }
}
