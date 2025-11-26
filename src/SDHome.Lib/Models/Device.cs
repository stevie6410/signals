namespace SDHome.Lib.Models;

public class Device
{
    public string DeviceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string? IeeeAddress { get; set; }
    public string? ModelId { get; set; }
    public string? Manufacturer { get; set; }
    public string? Description { get; set; }
    public bool PowerSource { get; set; }
    public DeviceType? DeviceType { get; set; }
    public string? Room { get; set; }
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
