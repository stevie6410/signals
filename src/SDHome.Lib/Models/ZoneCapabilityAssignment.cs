namespace SDHome.Lib.Models;

/// <summary>
/// Represents the assignment of a primary device for a specific capability within a zone.
/// </summary>
public record ZoneCapabilityAssignment
{
    public int Id { get; init; }
    public int ZoneId { get; init; }
    public string Capability { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string? Property { get; init; }
    public int Priority { get; init; }
    public string? DisplayName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    
    // Denormalized for convenience
    public string? ZoneName { get; init; }
    public string? DeviceName { get; init; }
}

/// <summary>
/// Standard capability types that can be assigned to zones
/// </summary>
public static class ZoneCapabilities
{
    // Sensors
    public const string Motion = "motion";
    public const string Presence = "presence";
    public const string Temperature = "temperature";
    public const string Humidity = "humidity";
    public const string Contact = "contact";
    public const string Illuminance = "illuminance";
    public const string Co2 = "co2";
    public const string AirQuality = "air_quality";
    
    // Actuators
    public const string Lights = "lights";
    public const string MainLight = "main_light";
    public const string AccentLight = "accent_light";
    public const string Climate = "climate";
    public const string Heating = "heating";
    public const string Cooling = "cooling";
    public const string Fan = "fan";
    public const string Cover = "cover";
    public const string Lock = "lock";
    public const string Switch = "switch";
    
    // Media
    public const string Speaker = "speaker";
    public const string Display = "display";
    public const string Tv = "tv";
    
    public static readonly string[] All = [
        Motion, Presence, Temperature, Humidity, Contact, Illuminance, Co2, AirQuality,
        Lights, MainLight, AccentLight, Climate, Heating, Cooling, Fan, Cover, Lock, Switch,
        Speaker, Display, Tv
    ];
    
    public static readonly Dictionary<string, string> Labels = new()
    {
        [Motion] = "Motion Sensor",
        [Presence] = "Presence Sensor",
        [Temperature] = "Temperature Sensor",
        [Humidity] = "Humidity Sensor",
        [Contact] = "Door/Window Sensor",
        [Illuminance] = "Light Level Sensor",
        [Co2] = "CO2 Sensor",
        [AirQuality] = "Air Quality Sensor",
        [Lights] = "Lights",
        [MainLight] = "Main Light",
        [AccentLight] = "Accent Light",
        [Climate] = "Climate Control",
        [Heating] = "Heating",
        [Cooling] = "Cooling/AC",
        [Fan] = "Fan",
        [Cover] = "Blinds/Curtains",
        [Lock] = "Lock",
        [Switch] = "Switch",
        [Speaker] = "Speaker",
        [Display] = "Display",
        [Tv] = "TV"
    };
    
    public static readonly Dictionary<string, string> Icons = new()
    {
        [Motion] = "🏃",
        [Presence] = "👤",
        [Temperature] = "🌡️",
        [Humidity] = "💧",
        [Contact] = "🚪",
        [Illuminance] = "☀️",
        [Co2] = "🫁",
        [AirQuality] = "🌬️",
        [Lights] = "💡",
        [MainLight] = "💡",
        [AccentLight] = "✨",
        [Climate] = "🏠",
        [Heating] = "🔥",
        [Cooling] = "❄️",
        [Fan] = "🌀",
        [Cover] = "🪟",
        [Lock] = "🔒",
        [Switch] = "🔘",
        [Speaker] = "🔊",
        [Display] = "🖥️",
        [Tv] = "📺"
    };
    
    /// <summary>
    /// Default property mappings for each capability
    /// </summary>
    public static readonly Dictionary<string, string> DefaultProperties = new()
    {
        [Motion] = "occupancy",
        [Presence] = "presence",
        [Temperature] = "temperature",
        [Humidity] = "humidity",
        [Contact] = "contact",
        [Illuminance] = "illuminance",
        [Co2] = "co2",
        [AirQuality] = "air_quality",
        [Lights] = "state",
        [MainLight] = "state",
        [AccentLight] = "state",
        [Climate] = "state",
        [Heating] = "state",
        [Cooling] = "state",
        [Fan] = "state",
        [Cover] = "position",
        [Lock] = "state",
        [Switch] = "state",
        [Speaker] = "state",
        [Display] = "state",
        [Tv] = "state"
    };
}

public record CreateZoneCapabilityAssignmentRequest
{
    public required string Capability { get; init; }
    public required string DeviceId { get; init; }
    public string? Property { get; init; }
    public int Priority { get; init; } = 0;
    public string? DisplayName { get; init; }
}

public record UpdateZoneCapabilityAssignmentRequest
{
    public string? Property { get; init; }
    public int Priority { get; init; } = 0;
    public string? DisplayName { get; init; }
}

/// <summary>
/// Extended zone model that includes capability assignments
/// </summary>
public record ZoneWithCapabilities
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public string? Color { get; init; }
    public int? ParentZoneId { get; init; }
    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    
    public Zone? ParentZone { get; init; }
    public List<Zone> ChildZones { get; init; } = [];
    public List<ZoneCapabilityAssignment> CapabilityAssignments { get; init; } = [];
    public List<Device> Devices { get; init; } = [];
    
    /// <summary>
    /// Gets the assigned device for a specific capability
    /// </summary>
    public ZoneCapabilityAssignment? GetCapability(string capability)
    {
        return CapabilityAssignments
            .Where(c => c.Capability == capability)
            .OrderBy(c => c.Priority)
            .FirstOrDefault();
    }
}
