using System.Text.Json;

namespace SDHome.Lib.Models
{
    public sealed record SignalEvent(
        Guid Id,
        string Source,
        string DeviceId,
        string? Location,
        string Capability,
        string EventType,
        string? EventSubType,
        double? Value,
        DateTime TimestampUtc,
        string RawTopic,
        JsonElement RawPayload,
        DeviceKind DeviceKind,
        EventCategory EventCategory,
        JsonElement? RawPayloadArray
    );
}
