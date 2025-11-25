

namespace SDHome.Lib.Models
{
    public sealed record TriggerEvent(
        Guid Id,
        Guid SignalEventId,
        DateTime TimestampUtc,
        string DeviceId,
        string Capability,
        string TriggerType,
        string? TriggerSubType,
        bool? Value
    );
}
