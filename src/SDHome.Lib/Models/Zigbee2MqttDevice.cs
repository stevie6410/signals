using System.Text.Json;

namespace SDHome.Lib.Models;

public class Zigbee2MqttDevice
{
    public string ieee_address { get; set; } = string.Empty;
    public string friendly_name { get; set; } = string.Empty;
    public string? model_id { get; set; }
    public string? manufacturer { get; set; }
    public string? description { get; set; }
    public string? power_source { get; set; }
    public Definition? definition { get; set; }
    public bool disabled { get; set; }
    public JsonElement? endpoints { get; set; }
}

public class Definition
{
    public string? model { get; set; }
    public string? vendor { get; set; }
    public string? description { get; set; }
    public List<Expose>? exposes { get; set; }
}

public class Expose
{
    public string? type { get; set; }
    public string? name { get; set; }
    public string? property { get; set; }
    public List<string>? features { get; set; }
    public string? access { get; set; }
}
