

namespace SDHome.Lib.Models
{
    public sealed record SensorReading(
        Guid Id,
        Guid SignalEventId,
        DateTime TimestampUtc,
        string DeviceId,
        string Metric,
        double Value,
        string? Unit
    );
}
