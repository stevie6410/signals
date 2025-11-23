using System.Text.Json;

namespace Signals.Models;

public enum DeviceKind
{
    Unknown,
    Button,
    MotionSensor,
    ContactSensor,
    Thermometer,
    Light,
    Switch,
    Outlet,
}

public enum EventCategory
{
    Trigger,
    Telemetry,
}

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
    JsonElement? RawPayloadArray);
