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
                
                // Skip empty actions (some devices send action: "" on state updates)
                if (!string.IsNullOrEmpty(action))
                {
                    capability = "button";
                    eventType = "press";
                    
                    // Parse multi-button actions like "button_1_single", "button_2_double", etc.
                    // Also handles simple actions like "single", "double", "hold", "release"
                    var (buttonSource, actionType) = ParseButtonAction(action);
                    
                    // Store button source in capability if it's a multi-button device
                    if (buttonSource != null)
                    {
                        capability = buttonSource; // e.g., "button_1", "button_2"
                    }
                    
                    eventSubType = actionType; // e.g., "single", "double", "hold", "release"

                    if (deviceKind == DeviceKind.Unknown)
                        deviceKind = DeviceKind.Button;
                }
            }

            // Check for temperature first to determine sensor type priority
            bool hasTemperature = payload.TryGetProperty("temperature", out var tempProp)
                && tempProp.ValueKind == JsonValueKind.Number;
            
            // Occupancy sensors (presence detection - typically mmWave/radar based)
            // These report "occupancy" property and detect presence, not just motion
            if (payload.TryGetProperty("occupancy", out var occProp) &&
                (occProp.ValueKind == JsonValueKind.True || occProp.ValueKind == JsonValueKind.False))
            {
                // Only classify as occupancy if it's actually detecting (true) 
                // OR if there's no temperature reading (dedicated presence sensor)
                if (occProp.GetBoolean() || !hasTemperature)
                {
                    capability = "occupancy";
                    eventType = "detection";
                    eventSubType = occProp.GetBoolean() ? "present" : "clear";

                    if (deviceKind == DeviceKind.Unknown)
                        deviceKind = DeviceKind.MotionSensor; // Will need OccupancySensor type eventually
                }
            }
            
            // Motion sensors (PIR-based motion detection)
            // These report "motion" property for instantaneous motion events
            if (payload.TryGetProperty("motion", out var motionProp) &&
                (motionProp.ValueKind == JsonValueKind.True || motionProp.ValueKind == JsonValueKind.False))
            {
                if (motionProp.GetBoolean() || !hasTemperature)
                {
                    capability = "motion";
                    eventType = "detection";
                    eventSubType = motionProp.GetBoolean() ? "active" : "inactive";

                    if (deviceKind == DeviceKind.Unknown)
                        deviceKind = DeviceKind.MotionSensor;
                }
            }

            // Temperature - takes priority over presence sensors if present
            if (hasTemperature)
            {
                capability = "temperature";
                eventType = "measurement";
                eventSubType ??= "current";
                value = tempProp.GetDouble();

                if (deviceKind == DeviceKind.Unknown)
                    deviceKind = DeviceKind.Thermometer;
            }

            // Contact sensors (door/window sensors)
            if (payload.TryGetProperty("contact", out var contactProp) &&
                (contactProp.ValueKind == JsonValueKind.True || contactProp.ValueKind == JsonValueKind.False))
            {
                capability = "contact";
                eventType = "state_change";
                eventSubType = contactProp.GetBoolean() ? "closed" : "open";

                if (deviceKind == DeviceKind.Unknown)
                    deviceKind = DeviceKind.ContactSensor;
            }

            // Trigger vs telemetry
            var category = EventCategory.Telemetry;
            if (deviceKind is DeviceKind.Button or DeviceKind.MotionSensor or DeviceKind.ContactSensor
                && capability is "button" or "motion" or "occupancy" or "contact"
                && eventType is "press" or "detection" or "state_change")
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

        /// <summary>
        /// Parse button action strings to extract button source and action type.
        /// Handles formats like:
        /// - "single", "double", "hold", "release" (simple single-button devices)
        /// - "button_1_single", "button_2_double" (multi-button devices)
        /// - "1_single", "2_double" (numeric button prefix)
        /// - "left_single", "right_double" (named button sources)
        /// - "up_hold_release", "down_press_release" (dimmer remotes with compound actions)
        /// </summary>
        private static (string? ButtonSource, string ActionType) ParseButtonAction(string action)
        {
            // Single-word action types (no prefix)
            var simpleActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "single", "double", "triple", "quadruple", 
                "hold", "release", "press", "long_press", "click"
            };
            
            // Compound action types (two words joined by underscore)
            var compoundActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "hold_release", "press_release", "long_press",
                "brightness_move_up", "brightness_move_down", "brightness_stop"
            };
            
            var lowerAction = action.ToLowerInvariant();
            
            // Check if this is a simple action (no button prefix)
            if (simpleActions.Contains(lowerAction))
            {
                return (null, lowerAction);
            }
            
            // Check if the whole thing is a compound action
            if (compoundActions.Contains(lowerAction))
            {
                return (null, lowerAction);
            }
            
            // Try to split and find action type at the end
            var parts = action.Split('_');
            
            if (parts.Length >= 2)
            {
                // FIRST: Check if last two parts form a compound action (e.g., "up_hold_release" -> "hold_release")
                if (parts.Length >= 3)
                {
                    var lastTwoParts = $"{parts[^2]}_{parts[^1]}".ToLowerInvariant();
                    if (compoundActions.Contains(lastTwoParts))
                    {
                        var buttonSource = string.Join("_", parts[..^2]).ToLowerInvariant();
                        return (buttonSource, lastTwoParts);
                    }
                }
                
                // THEN: Check if just the last part is a simple action
                var lastPart = parts[^1].ToLowerInvariant();
                if (simpleActions.Contains(lastPart))
                {
                    // Everything before the action is the button source
                    var buttonSource = string.Join("_", parts[..^1]).ToLowerInvariant();
                    return (buttonSource, lastPart);
                }
            }
            
            // Fallback: return the whole action as the action type
            return (null, lowerAction);
        }
    }

}
