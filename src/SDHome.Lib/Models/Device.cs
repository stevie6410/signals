namespace SDHome.Lib.Models;

public class Device
{
    /// <summary>
    /// The device identifier used in MQTT topics (e.g., "shed_light")
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// The Zigbee2MQTT friendly_name (same as DeviceId, forms MQTT topic)
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;
    
    /// <summary>
    /// User-customizable display name (e.g., "Shed Light").
    /// If null, falls back to FriendlyName.
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Gets the effective display name (DisplayName if set, otherwise FriendlyName)
    /// </summary>
    public string EffectiveDisplayName => DisplayName ?? FriendlyName;
    
    public string? IeeeAddress { get; set; }
    
    /// <summary>
    /// The Zigbee model_id (e.g., "lumi.remote.b1acn01")
    /// </summary>
    public string? ModelId { get; set; }
    
    /// <summary>
    /// The Zigbee2MQTT definition model (e.g., "WXKG11LM") - used for device images
    /// </summary>
    public string? Model { get; set; }
    
    public string? Manufacturer { get; set; }
    
    /// <summary>
    /// Gets the device image URL from Zigbee2MQTT's device image repository.
    /// Uses the definition Model (not ModelId) as that matches the image filenames.
    /// Spaces in model names are replaced with hyphens to match the URL format.
    /// </summary>
    public string? ImageUrl => !string.IsNullOrEmpty(Model) 
        ? $"https://www.zigbee2mqtt.io/images/devices/{Uri.EscapeDataString(Model.Replace(' ', '-'))}.png" 
        : null;
    public string? Description { get; set; }
    public bool PowerSource { get; set; }
    public DeviceType? DeviceType { get; set; }
    
    /// <summary>
    /// Legacy room field - use ZoneId for new code
    /// </summary>
    [Obsolete("Use ZoneId instead")]
    public string? Room { get; set; }
    
    /// <summary>
    /// Zone ID for hierarchical grouping
    /// </summary>
    public int? ZoneId { get; set; }
    
    /// <summary>
    /// Zone details (populated when included)
    /// </summary>
    public Zone? Zone { get; set; }
    
    public List<string> Capabilities { get; set; } = new();
    public Dictionary<string, object> Attributes { get; set; } = new();
    public DateTime? LastSeen { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum DeviceType
{
    Light,
    Switch,
    Sensor,
    Climate,
    Lock,
    Cover,
    Fan,
    Other
}
