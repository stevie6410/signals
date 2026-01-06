namespace SDHome.Lib.Models;

/// <summary>
/// Defines how raw device capability states are mapped to friendly, typed states.
/// </summary>
public class CapabilityMapping
{
    public int Id { get; set; }
    
    /// <summary>
    /// The capability name this mapping applies to (e.g., "occupancy", "motion", "contact")
    /// </summary>
    public string Capability { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional: restrict this mapping to a specific device type
    /// </summary>
    public string? DeviceType { get; set; }
    
    /// <summary>
    /// The property in the MQTT payload (e.g., "occupancy", "contact", "state")
    /// </summary>
    public string Property { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for this capability (e.g., "Presence", "Door State")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Icon for this capability (emoji or icon class)
    /// </summary>
    public string? Icon { get; set; }
    
    /// <summary>
    /// State mappings from raw values to friendly names
    /// </summary>
    public List<StateMapping> StateMappings { get; set; } = [];
    
    /// <summary>
    /// Unit of measurement for numeric values (e.g., "°C", "%", "lx")
    /// </summary>
    public string? Unit { get; set; }
    
    /// <summary>
    /// Whether this is a system default mapping (cannot be deleted, only overridden)
    /// </summary>
    public bool IsSystemDefault { get; set; }
    
    /// <summary>
    /// Order for display purposes
    /// </summary>
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// Get the friendly state name for a raw value
    /// </summary>
    public string? GetFriendlyState(object? rawValue)
    {
        if (rawValue == null) return null;
        
        var rawString = rawValue.ToString()?.ToLowerInvariant();
        var mapping = StateMappings.FirstOrDefault(m => 
            m.RawValue?.ToString()?.ToLowerInvariant() == rawString);
        
        return mapping?.FriendlyName;
    }
    
    /// <summary>
    /// Get the raw value for a friendly state name
    /// </summary>
    public object? GetRawValue(string friendlyName)
    {
        var mapping = StateMappings.FirstOrDefault(m => 
            string.Equals(m.FriendlyName, friendlyName, StringComparison.OrdinalIgnoreCase));
        
        return mapping?.RawValue;
    }
}

/// <summary>
/// Maps a raw device state value to a friendly name
/// </summary>
public class StateMapping
{
    /// <summary>
    /// The raw value from the device (e.g., true, false, "ON", "OFF", 1, 0)
    /// </summary>
    public object? RawValue { get; set; }
    
    /// <summary>
    /// The friendly name to display (e.g., "Occupied", "Vacant", "Open", "Closed")
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional icon for this state
    /// </summary>
    public string? Icon { get; set; }
    
    /// <summary>
    /// Optional color for this state (CSS color or class)
    /// </summary>
    public string? Color { get; set; }
    
    /// <summary>
    /// Whether this state represents an "active" or "alert" condition
    /// </summary>
    public bool IsActive { get; set; }
}
