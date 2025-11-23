using System.Text.Json;
using Signals.Components;
using Signals.Models;
using Signals.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<SignalLog>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

SeedSampleSignals(app.Services.GetRequiredService<SignalLog>());

app.Run();

static void SeedSampleSignals(SignalLog log)
{
    if (log.Events.Any())
    {
        return;
    }

    log.Add(new(
        Id: Guid.NewGuid(),
        Source: "seed",
        DeviceId: "front_room_lamp",
        Location: "Front room",
        Capability: "light",
        EventType: "state",
        EventSubType: "on",
        Value: null,
        TimestampUtc: DateTime.UtcNow.AddMinutes(-10),
        RawTopic: "sdhome/front_room_lamp",
        RawPayload: JsonDocument.Parse("{\"state\":\"ON\"}").RootElement.Clone(),
        DeviceKind: DeviceKind.Light,
        EventCategory: EventCategory.Telemetry,
        RawPayloadArray: null
    ));

    log.Add(new(
        Id: Guid.NewGuid(),
        Source: "seed",
        DeviceId: "hallway_motion",
        Location: "Hallway",
        Capability: "motion",
        EventType: "detection",
        EventSubType: "active",
        Value: null,
        TimestampUtc: DateTime.UtcNow.AddMinutes(-5),
        RawTopic: "sdhome/hallway_motion",
        RawPayload: JsonDocument.Parse("{\"occupancy\":true}").RootElement.Clone(),
        DeviceKind: DeviceKind.MotionSensor,
        EventCategory: EventCategory.Trigger,
        RawPayloadArray: null
    ));

    log.Add(new(
        Id: Guid.NewGuid(),
        Source: "seed",
        DeviceId: "bedroom_temperature",
        Location: "Bedroom",
        Capability: "temperature",
        EventType: "measurement",
        EventSubType: "current",
        Value: 21.7,
        TimestampUtc: DateTime.UtcNow.AddMinutes(-1),
        RawTopic: "sdhome/bedroom_temperature",
        RawPayload: JsonDocument.Parse("{\"temperature\":21.7}").RootElement.Clone(),
        DeviceKind: DeviceKind.Thermometer,
        EventCategory: EventCategory.Telemetry,
        RawPayloadArray: null
    ));
}
