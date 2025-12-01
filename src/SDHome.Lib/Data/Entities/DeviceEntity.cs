using System.Text.Json;
using SDHome.Lib.Models;

namespace SDHome.Lib.Data.Entities;

public class DeviceEntity
{
    /// <summary>
    /// The device identifier used in MQTT topics (e.g., "shed_light")
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// The Zigbee2MQTT friendly_name (same as DeviceId, used for MQTT topics)
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;
    
    /// <summary>
    /// User-customizable display name (e.g., "Shed Light"). 
    /// If null, falls back to FriendlyName.
    /// </summary>
    public string? DisplayName { get; set; }
    
    public string? IeeeAddress { get; set; }
    public string? ModelId { get; set; }
    
    /// <summary>
    /// The Zigbee2MQTT definition model (e.g., "WXKG11LM") - used for device images
    /// </summary>
    public string? Model { get; set; }
    
    public string? Manufacturer { get; set; }
    public string? Description { get; set; }
    public bool PowerSource { get; set; }
    public string? DeviceType { get; set; }
    
    /// <summary>
    /// Legacy room field - use ZoneId for new code
    /// </summary>
    [Obsolete("Use ZoneId instead")]
    public string? Room { get; set; }
    
    /// <summary>
    /// Foreign key to Zone for hierarchical grouping
    /// </summary>
    public int? ZoneId { get; set; }
    
    public string Capabilities { get; set; } = "[]";
    public string Attributes { get; set; } = "{}";
    public DateTime? LastSeen { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation property
    public ZoneEntity? Zone { get; set; }

    public Device ToModel()
    {
        return new Device
        {
            DeviceId = DeviceId,
            FriendlyName = FriendlyName,
            DisplayName = DisplayName,
            IeeeAddress = IeeeAddress,
            ModelId = ModelId,
            Model = Model,
            Manufacturer = Manufacturer,
            Description = Description,
            PowerSource = PowerSource,
            DeviceType = !string.IsNullOrEmpty(DeviceType) 
                ? Enum.Parse<Models.DeviceType>(DeviceType) 
                : null,
#pragma warning disable CS0618 // Type or member is obsolete
            Room = Room,
#pragma warning restore CS0618
            ZoneId = ZoneId,
            Zone = Zone?.ToModel(),
            Capabilities = JsonSerializer.Deserialize<List<string>>(Capabilities) ?? [],
            Attributes = JsonSerializer.Deserialize<Dictionary<string, object>>(Attributes) ?? [],
            LastSeen = LastSeen,
            IsAvailable = IsAvailable,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    public static DeviceEntity FromModel(Device model)
    {
        return new DeviceEntity
        {
            DeviceId = model.DeviceId,
            FriendlyName = model.FriendlyName,
            DisplayName = model.DisplayName,
            IeeeAddress = model.IeeeAddress,
            ModelId = model.ModelId,
            Model = model.Model,
            Manufacturer = model.Manufacturer,
            Description = model.Description,
            PowerSource = model.PowerSource,
            DeviceType = model.DeviceType?.ToString(),
#pragma warning disable CS0618 // Type or member is obsolete
            Room = model.Room,
#pragma warning restore CS0618
            ZoneId = model.ZoneId,
            Capabilities = JsonSerializer.Serialize(model.Capabilities),
            Attributes = JsonSerializer.Serialize(model.Attributes),
            LastSeen = model.LastSeen,
            IsAvailable = model.IsAvailable,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }
}
