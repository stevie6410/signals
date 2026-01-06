using System.Text.Json;
using SDHome.Lib.Models;

namespace SDHome.Lib.Data.Entities;

/// <summary>
/// Defines how raw device capability states are mapped to friendly, typed states.
/// e.g., occupancy: true -> "Occupied", occupancy: false -> "Vacant"
/// </summary>
public class CapabilityMappingEntity
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
    /// JSON array of state mappings: [{ "raw": true, "friendly": "Occupied", "icon": "👤" }, ...]
    /// </summary>
    public string StateMappings { get; set; } = "[]";
    
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
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public CapabilityMapping ToModel()
    {
        return new CapabilityMapping
        {
            Id = Id,
            Capability = Capability,
            DeviceType = DeviceType,
            Property = Property,
            DisplayName = DisplayName,
            Icon = Icon,
            StateMappings = JsonSerializer.Deserialize<List<StateMapping>>(StateMappings) ?? [],
            Unit = Unit,
            IsSystemDefault = IsSystemDefault,
            DisplayOrder = DisplayOrder
        };
    }

    public static CapabilityMappingEntity FromModel(CapabilityMapping model)
    {
        return new CapabilityMappingEntity
        {
            Id = model.Id,
            Capability = model.Capability,
            DeviceType = model.DeviceType,
            Property = model.Property,
            DisplayName = model.DisplayName,
            Icon = model.Icon,
            StateMappings = JsonSerializer.Serialize(model.StateMappings),
            Unit = model.Unit,
            IsSystemDefault = model.IsSystemDefault,
            DisplayOrder = model.DisplayOrder,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
