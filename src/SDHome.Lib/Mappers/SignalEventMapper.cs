using SDHome.Lib.Models;
using System.Text.Json;

namespace SDHome.Lib.Mappers
{
    public interface ISignalEventMapper
    {
        SignalEvent Map(string topic, JsonElement payload);
        SignalEvent MapArrayPayload(string topic, JsonElement payloadArray);
    }

    public class SignalEventMapper : ISignalEventMapper
    {
        private const string ZigbeePrefix = "sdhome/";

        private static readonly Dictionary<string, DeviceKind> DeviceKinds =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Bedroom Switch Steve"] = DeviceKind.Button,
                ["front_room_lamp"] = DeviceKind.Light,
                // add others here
            };

        public SignalEvent MapArrayPayload(string topic, JsonElement payloadArray)
        {
            var (source, deviceId) = GetSourceAndDeviceId(topic);

            return new SignalEvent(
                Id: Guid.NewGuid(),
                Source: source,
                DeviceId: deviceId,
                Location: null,
                Capability: "bridge_dump",
                EventType: "report",
                EventSubType: null,
                Value: null,
                TimestampUtc: DateTime.UtcNow,
                RawTopic: topic,
                RawPayload: default,
                DeviceKind: DeviceKind.Unknown,
                EventCategory: EventCategory.Telemetry,
                RawPayloadArray: payloadArray
            );
        }

        public SignalEvent Map(string topic, JsonElement payload)
        {
            var (source, deviceId) = GetSourceAndDeviceId(topic);

            DeviceKind deviceKind = DeviceKinds.TryGetValue(deviceId, out var kind)
                ? kind
                : DeviceKind.Unknown;

            string capability = "unknown";
            string eventType = "unknown";
            string? eventSubType = null;
            double? value = null;
            string? location = null;

            // Button actions
            if (payload.TryGetProperty("action", out var actionProp)
                && actionProp.ValueKind == JsonValueKind.String)
            {
                var action = actionProp.GetString() ?? string.Empty;

                capability = "button";
                eventType = "press";

                var parts = action.Split('_', StringSplitOptions.RemoveEmptyEntries);
                eventSubType = parts.Length == 2 ? parts[1] : action;

                if (deviceKind == DeviceKind.Unknown)
                    deviceKind = DeviceKind.Button;
            }

            // Temperature
            if (payload.TryGetProperty("temperature", out var tempProp)
                && tempProp.ValueKind == JsonValueKind.Number)
            {
                capability = "temperature";
                eventType = "measurement";
                eventSubType ??= "current";
                value = tempProp.GetDouble();

                if (deviceKind == DeviceKind.Unknown)
                    deviceKind = DeviceKind.Thermometer;
            }

            // Motion
            if (payload.TryGetProperty("occupancy", out var occProp) &&
                (occProp.ValueKind == JsonValueKind.True || occProp.ValueKind == JsonValueKind.False))
            {
                capability = "motion";
                eventType = "detection";
                eventSubType = occProp.GetBoolean() ? "active" : "inactive";

                if (deviceKind == DeviceKind.Unknown)
                    deviceKind = DeviceKind.MotionSensor;
            }

            // Trigger vs telemetry
            var category = EventCategory.Telemetry;
            if (deviceKind is DeviceKind.Button or DeviceKind.MotionSensor or DeviceKind.ContactSensor
                && capability is "button" or "motion"
                && eventType is "press" or "detection")
            {
                category = EventCategory.Trigger;
            }

            return new SignalEvent(
                Id: Guid.NewGuid(),
                Source: source,
                DeviceId: deviceId,
                Location: location,
                Capability: capability,
                EventType: eventType,
                EventSubType: eventSubType,
                Value: value,
                TimestampUtc: DateTime.UtcNow,
                RawTopic: topic,
                RawPayload: payload,
                DeviceKind: deviceKind,
                EventCategory: category,
                RawPayloadArray: null
            );
        }

        private static (string Source, string DeviceId) GetSourceAndDeviceId(string topic)
        {
            if (topic.StartsWith(ZigbeePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ("sdhome", topic[ZigbeePrefix.Length..]);
            }

            return ("mqtt", topic);
        }
    }

}
