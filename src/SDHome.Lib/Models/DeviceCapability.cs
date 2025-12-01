using System.Text.Json.Serialization;

namespace SDHome.Lib.Models;

/// <summary>
/// Represents the full device definition with capabilities from Zigbee2MQTT
/// </summary>
public class DeviceDefinition
{
    public string DeviceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? IeeeAddress { get; set; }
    public string? ModelId { get; set; }
    public string? Manufacturer { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// URL to device image from zigbee2mqtt.io
    /// </summary>
    public string? ImageUrl { get; set; }
    public string? DeviceType { get; set; }
    public string? PowerSource { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime? LastSeen { get; set; }
    
    /// <summary>
    /// All capabilities exposed by this device
    /// </summary>
    public List<DeviceCapability> Capabilities { get; set; } = new();
    
    /// <summary>
    /// Current state values for all properties
    /// </summary>
    public Dictionary<string, object?> CurrentState { get; set; } = new();
}

/// <summary>
/// Represents a single capability/feature exposed by a Zigbee device
/// </summary>
public class DeviceCapability
{
    /// <summary>
    /// Type of capability: binary, numeric, enum, composite, text, list, etc.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for this capability
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Property name used in MQTT payloads
    /// </summary>
    public string Property { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable description
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Unit of measurement (e.g., °C, %, lux)
    /// </summary>
    public string? Unit { get; set; }
    
    /// <summary>
    /// Access flags: read, write, publish
    /// </summary>
    public CapabilityAccess Access { get; set; } = new();
    
    /// <summary>
    /// For enum types: available values
    /// </summary>
    public List<string>? Values { get; set; }
    
    /// <summary>
    /// For numeric types: minimum value
    /// </summary>
    public double? ValueMin { get; set; }
    
    /// <summary>
    /// For numeric types: maximum value
    /// </summary>
    public double? ValueMax { get; set; }
    
    /// <summary>
    /// For numeric types: step increment
    /// </summary>
    public double? ValueStep { get; set; }
    
    /// <summary>
    /// For binary types: value when ON
    /// </summary>
    public object? ValueOn { get; set; }
    
    /// <summary>
    /// For binary types: value when OFF
    /// </summary>
    public object? ValueOff { get; set; }
    
    /// <summary>
    /// For composite types: nested features
    /// </summary>
    public List<DeviceCapability>? Features { get; set; }
    
    /// <summary>
    /// Category for grouping in UI (light, switch, sensor, climate, etc.)
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// Suggested UI control type
    /// </summary>
    public ControlType ControlType { get; set; }
}

/// <summary>
/// Access flags for a capability
/// </summary>
public class CapabilityAccess
{
    /// <summary>
    /// Can read current value
    /// </summary>
    public bool CanRead { get; set; }
    
    /// <summary>
    /// Can write/set value
    /// </summary>
    public bool CanWrite { get; set; }
    
    /// <summary>
    /// Value is published automatically on change
    /// </summary>
    public bool IsPublished { get; set; }
}

/// <summary>
/// Suggested UI control type for rendering
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ControlType
{
    Toggle,      // Binary on/off
    Slider,      // Numeric with range
    Number,      // Numeric input
    Select,      // Dropdown/enum
    ColorPicker, // Color temperature or XY
    Text,        // Text input
    ReadOnly,    // Display only (sensor values)
    Button,      // Action button
    Composite    // Group of controls
}

/// <summary>
/// Request to set device state
/// </summary>
public class SetDeviceStateRequest
{
    /// <summary>
    /// Property-value pairs to set
    /// </summary>
    public Dictionary<string, object> State { get; set; } = new();
}
