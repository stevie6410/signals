using SDHome.Lib.Models;

namespace SDHome.Lib.Data.Entities;

public class ZoneEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    
    /// <summary>
    /// Optional parent zone for hierarchical nesting
    /// e.g., "Front Room" -> "Downstairs" -> "SD Home"
    /// </summary>
    public int? ParentZoneId { get; set; }
    
    /// <summary>
    /// Sort order within the same parent
    /// </summary>
    public int SortOrder { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ZoneEntity? ParentZone { get; set; }
    public ICollection<ZoneEntity> ChildZones { get; set; } = [];
    public ICollection<DeviceEntity> Devices { get; set; } = [];
    public ICollection<ZoneCapabilityAssignmentEntity> CapabilityAssignments { get; set; } = [];

    public Zone ToModel(bool includeChildren = false, bool includeParent = false)
    {
        return new Zone
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Icon = Icon,
            Color = Color,
            ParentZoneId = ParentZoneId,
            SortOrder = SortOrder,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ParentZone = includeParent ? ParentZone?.ToModel(includeChildren: false, includeParent: true) : null,
            ChildZones = includeChildren 
                ? ChildZones.OrderBy(c => c.SortOrder).Select(c => c.ToModel(includeChildren: true)).ToList() 
                : []
        };
    }

    public static ZoneEntity FromModel(Zone model)
    {
        return new ZoneEntity
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            Icon = model.Icon,
            Color = model.Color,
            ParentZoneId = model.ParentZoneId,
            SortOrder = model.SortOrder,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }
}
