using System.Collections.Concurrent;
using System.Text.Json;
using SDHome.Lib.Data;
using SDHome.Lib.Data.Entities;
using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public class SignalEventProjectionService(
    SignalsDbContext db,
    IRealtimeEventBroadcaster broadcaster,
    IAutomationEngine automationEngine) : ISignalEventProjectionService
{
    /// <summary>
    /// Known sensor metrics and their units. Each metric in a payload becomes a separate SensorReading.
    /// </summary>
    private static readonly Dictionary<string, string?> KnownSensorMetrics = new(StringComparer.OrdinalIgnoreCase)
    {
        // Environmental sensors
        ["temperature"] = "°C",
        ["device_temperature"] = "°C",
        ["humidity"] = "%",
        ["pressure"] = "hPa",
        ["co2"] = "ppm",
        ["voc"] = "ppb",
        ["pm25"] = "µg/m³",
        ["pm10"] = "µg/m³",
        ["formaldehyd"] = "mg/m³",
        
        // Light sensors
        ["illuminance"] = "lx",
        ["illuminance_lux"] = "lx",
        
        // Presence/Motion sensors (boolean values stored as 0/1)
        ["occupancy"] = null,           // Presence detection (mmWave/radar)
        ["motion"] = null,              // PIR motion detection
        ["presence"] = null,            // Alternative presence property
        
        // Power/Energy sensors
        ["power"] = "W",
        ["energy"] = "kWh",
        ["voltage"] = "V",
        ["current"] = "A",
        
        // Device health
        ["battery"] = "%",
        ["battery_low"] = null,
        ["linkquality"] = null,
        
        // Position/Level sensors
        ["position"] = "%",
        ["brightness"] = null,
        ["color_temp"] = "K",
        
        // Water sensors
        ["water_leak"] = null,
        ["soil_moisture"] = "%",
    };

    /// <summary>
    /// Track the last known state for each device+capability combination.
    /// Key format: "{deviceId}:{capability}" -> last known value
    /// </summary>
    private static readonly ConcurrentDictionary<string, object?> DeviceStateCache = new();

    public async Task<ProjectedEventData> ProjectAsync(
        SignalEvent ev, 
        CancellationToken cancellationToken = default,
        PipelineContext? pipelineContext = null)
    {
        // Extract ALL sensor readings from the payload first - each measurement is a separate reading
        var allReadings = ExtractAllSensorReadings(ev);
        
        TriggerEvent? trigger = null;
        
        // Handle trigger events (occupancy, motion, button, contact, state changes)
        // IMPORTANT: Only create triggers when state actually changes
        if (ev.Capability == "occupancy" && ev.EventType == "detection")
        {
            trigger = TryCreateOccupancyTrigger(ev);
        }
        else if (ev.Capability == "motion" && ev.EventType == "detection")
        {
            trigger = TryCreateMotionTrigger(ev);
        }
        else if (ev.Capability == "button" && ev.EventType == "press")
        {
            // Buttons always trigger (they're discrete events, not state)
            trigger = CreateButtonTrigger(ev);
        }
        else if (ev.Capability == "contact")
        {
            trigger = TryCreateContactTrigger(ev);
        }
        else
        {
            // Check for state-based triggers (switches/lights)
            trigger = TryCreateStateTrigger(ev);
        }
        
        // Persist trigger if we have one
        if (trigger != null)
        {
            db.TriggerEvents.Add(TriggerEventEntity.FromModel(trigger));
        }
        
        // Persist all sensor readings
        if (allReadings.Count > 0)
        {
            db.SensorReadings.AddRange(allReadings.Select(SensorReadingEntity.FromModel));
        }
        
        // Save to database
        if (trigger != null || allReadings.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        
        // Broadcast trigger event
        if (trigger != null)
        {
            await broadcaster.BroadcastTriggerEventAsync(trigger);
            await automationEngine.ProcessTriggerEventAsync(trigger, pipelineContext);
            
            // Handle device state changes for automation engine
            await HandleTriggerDeviceStateChange(ev, trigger, pipelineContext);
        }
        
        // Broadcast all sensor readings
        foreach (var reading in allReadings)
        {
            await broadcaster.BroadcastSensorReadingAsync(reading);
            await automationEngine.ProcessSensorReadingAsync(reading, pipelineContext);
        }
        
        return new ProjectedEventData(trigger, allReadings);
    }

    /// <summary>
    /// Extracts ALL sensor readings from the payload. Each numeric/boolean metric becomes a separate SensorReading.
    /// </summary>
    private List<SensorReading> ExtractAllSensorReadings(SignalEvent ev)
    {
        var payload = ev.RawPayload;
        var readings = new List<SensorReading>();

        if (payload.ValueKind != JsonValueKind.Object)
            return readings;

        foreach (var prop in payload.EnumerateObject())
        {
            var metricName = prop.Name.ToLowerInvariant();
            
            // Check if this is a known sensor metric
            if (KnownSensorMetrics.TryGetValue(metricName, out var unit))
            {
                double? value = null;
                
                // Handle numeric values
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var numValue))
                {
                    value = numValue;
                    
                    // Special handling for voltage (often in mV)
                    if (metricName == "voltage" && value > 100)
                    {
                        value /= 1000.0; // Convert mV to V
                    }
                }
                // Handle boolean values (convert to 1/0)
                else if (prop.Value.ValueKind == JsonValueKind.True)
                {
                    value = 1;
                }
                else if (prop.Value.ValueKind == JsonValueKind.False)
                {
                    value = 0;
                }
                
                if (value.HasValue)
                {
                    // Normalize metric names (e.g., device_temperature -> temperature)
                    var normalizedMetric = NormalizeMetricName(metricName);
                    
                    readings.Add(new SensorReading(
                        Id: Guid.NewGuid(),
                        SignalEventId: ev.Id,
                        TimestampUtc: ev.TimestampUtc,
                        DeviceId: ev.DeviceId,
                        Metric: normalizedMetric,
                        Value: value.Value,
                        Unit: unit
                    ));
                }
            }
        }

        return readings;
    }
    
    /// <summary>
    /// Normalize metric names to canonical forms
    /// </summary>
    private static string NormalizeMetricName(string metric) => metric switch
    {
        "device_temperature" => "temperature",
        "illuminance_lux" => "illuminance",
        _ => metric
    };

    private static TriggerEvent CreateButtonTrigger(SignalEvent ev)
    {
        // For buttons, the capability may be the button source (e.g., "button_1", "button_2")
        // or just "button" for single-button devices.
        // The eventSubType contains the action type (single, double, hold, etc.)
        return new TriggerEvent(
            Id: Guid.NewGuid(),
            SignalEventId: ev.Id,
            TimestampUtc: ev.TimestampUtc,
            DeviceId: ev.DeviceId,
            Capability: ev.Capability, // "button", "button_1", "button_2", etc.
            TriggerType: "button",
            TriggerSubType: ev.EventSubType, // "single", "double", "hold", etc.
            Value: true // Buttons are always "active" when pressed
        );
    }

    /// <summary>
    /// Create occupancy trigger only if the occupancy state actually changed
    /// </summary>
    private TriggerEvent? TryCreateOccupancyTrigger(SignalEvent ev)
    {
        var payload = ev.RawPayload;
        bool? occupancy = TryGetBool(payload, "occupancy");
        bool isPresent = string.Equals(ev.EventSubType, "present", StringComparison.OrdinalIgnoreCase);
        bool currentValue = occupancy ?? isPresent;
        
        // Check if state changed
        var cacheKey = $"{ev.DeviceId}:occupancy";
        var previousValue = DeviceStateCache.GetValueOrDefault(cacheKey);
        
        // Update cache
        DeviceStateCache[cacheKey] = currentValue;
        
        // Only trigger if state changed (or first time seeing this device)
        if (previousValue is bool prevBool && prevBool == currentValue)
        {
            return null; // No change, no trigger
        }

        return new TriggerEvent(
            Id: Guid.NewGuid(),
            SignalEventId: ev.Id,
            TimestampUtc: ev.TimestampUtc,
            DeviceId: ev.DeviceId,
            Capability: ev.Capability,
            TriggerType: "occupancy",
            TriggerSubType: currentValue ? "present" : "clear",
            Value: currentValue
        );
    }

    /// <summary>
    /// Create motion trigger only if the motion state actually changed
    /// </summary>
    private TriggerEvent? TryCreateMotionTrigger(SignalEvent ev)
    {
        var payload = ev.RawPayload;
        bool? motion = TryGetBool(payload, "motion");
        bool isActive = string.Equals(ev.EventSubType, "active", StringComparison.OrdinalIgnoreCase);
        bool currentValue = motion ?? isActive;
        
        // Check if state changed
        var cacheKey = $"{ev.DeviceId}:motion";
        var previousValue = DeviceStateCache.GetValueOrDefault(cacheKey);
        
        // Update cache
        DeviceStateCache[cacheKey] = currentValue;
        
        // Only trigger if state changed (or first time seeing this device)
        if (previousValue is bool prevBool && prevBool == currentValue)
        {
            return null; // No change, no trigger
        }

        return new TriggerEvent(
            Id: Guid.NewGuid(),
            SignalEventId: ev.Id,
            TimestampUtc: ev.TimestampUtc,
            DeviceId: ev.DeviceId,
            Capability: ev.Capability,
            TriggerType: "motion",
            TriggerSubType: currentValue ? "active" : "inactive",
            Value: currentValue
        );
    }

    /// <summary>
    /// Create contact trigger only if the contact state actually changed
    /// </summary>
    private TriggerEvent? TryCreateContactTrigger(SignalEvent ev)
    {
        var payload = ev.RawPayload;
        bool? contact = TryGetBool(payload, "contact");
        bool currentValue = contact ?? false;
        
        // Check if state changed
        var cacheKey = $"{ev.DeviceId}:contact";
        var previousValue = DeviceStateCache.GetValueOrDefault(cacheKey);
        
        // Update cache
        DeviceStateCache[cacheKey] = currentValue;
        
        // Only trigger if state changed (or first time seeing this device)
        if (previousValue is bool prevBool && prevBool == currentValue)
        {
            return null; // No change, no trigger
        }

        return new TriggerEvent(
            Id: Guid.NewGuid(),
            SignalEventId: ev.Id,
            TimestampUtc: ev.TimestampUtc,
            DeviceId: ev.DeviceId,
            Capability: ev.Capability,
            TriggerType: "contact",
            TriggerSubType: currentValue ? "closed" : "open",
            Value: currentValue
        );
    }

    /// <summary>
    /// Create state trigger (for switches/lights) only if state actually changed
    /// </summary>
    private TriggerEvent? TryCreateStateTrigger(SignalEvent ev)
    {
        var payload = ev.RawPayload;

        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        // Check for state property (common in switches/lights)
        if (payload.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
        {
            var state = stateProp.GetString();
            if (state == "ON" || state == "OFF")
            {
                // Check if state changed
                var cacheKey = $"{ev.DeviceId}:state";
                var previousValue = DeviceStateCache.GetValueOrDefault(cacheKey);
                
                // Update cache
                DeviceStateCache[cacheKey] = state;
                
                // Only trigger if state changed (or first time seeing this device)
                if (previousValue is string prevState && prevState == state)
                {
                    return null; // No change, no trigger
                }
                
                return new TriggerEvent(
                    Id: Guid.NewGuid(),
                    SignalEventId: ev.Id,
                    TimestampUtc: ev.TimestampUtc,
                    DeviceId: ev.DeviceId,
                    Capability: "switch",
                    TriggerType: "state",
                    TriggerSubType: state?.ToLower(),
                    Value: state == "ON"
                );
            }
        }

        return null;
    }

    private async Task HandleTriggerDeviceStateChange(SignalEvent ev, TriggerEvent trigger, PipelineContext? pipelineContext)
    {
        switch (trigger.TriggerType)
        {
            case "button":
                // Notify as device state change so DeviceState type automations work
                await automationEngine.ProcessDeviceStateChangeAsync(
                    ev.DeviceId,
                    "action",
                    null,
                    ev.EventSubType,
                    pipelineContext);
                break;
                
            case "state":
                // Update cached state for toggle operations
                await automationEngine.ProcessDeviceStateChangeAsync(
                    ev.DeviceId,
                    "state",
                    null,
                    trigger.TriggerSubType?.ToUpper(),
                    pipelineContext);
                break;
        }
    }

    private static bool? TryGetBool(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        if (payload.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }

        return null;
    }
}

