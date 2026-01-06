using SDHome.Lib.Models;

namespace SDHome.Lib.Data.Entities;

/// <summary>
/// Represents the assignment of a primary device for a specific capability within a zone.
/// For example: "Bathroom" zone has "motion_sensor_bathroom" as the primary device for "motion" capability.
/// This allows automations to reference "zone.bathroom.motion" instead of specific device IDs.
/// </summary>
public class ZoneCapabilityAssignmentEntity
{
    public int Id { get; set; }
    
    /// <summary>
    /// The zone this assignment belongs to
    /// </summary>
    public int ZoneId { get; set; }
    
    /// <summary>
    /// The capability type (e.g., "motion", "temperature", "presence", "lights", "contact", "humidity")
    /// </summary>
    public string Capability { get; set; } = string.Empty;
    
    /// <summary>
    /// The primary device ID that provides this capability for the zone
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// The device property to use for this capability (e.g., "occupancy", "temperature", "state")
    /// If null, uses the default property for the capability type
    /// </summary>
    public string? Property { get; set; }
    
    /// <summary>
    /// Priority for this assignment (lower = higher priority)
    /// Allows multiple devices to be assigned to the same capability with fallback
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Optional friendly name override for this capability in this zone
    /// </summary>
    public string? DisplayName { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ZoneEntity? Zone { get; set; }
    public DeviceEntity? Device { get; set; }

    public ZoneCapabilityAssignment ToModel()
    {
        return new ZoneCapabilityAssignment
        {
            Id = Id,
            ZoneId = ZoneId,
            Capability = Capability,
            DeviceId = DeviceId,
            Property = Property,
            Priority = Priority,
            DisplayName = DisplayName,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ZoneName = Zone?.Name,
            DeviceName = Device?.DisplayName ?? Device?.FriendlyName
        };
    }

    public static ZoneCapabilityAssignmentEntity FromModel(ZoneCapabilityAssignment model)
    {
        return new ZoneCapabilityAssignmentEntity
        {
            Id = model.Id,
            ZoneId = model.ZoneId,
            Capability = model.Capability,
            DeviceId = model.DeviceId,
            Property = model.Property,
            Priority = model.Priority,
            DisplayName = model.DisplayName,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }
}
