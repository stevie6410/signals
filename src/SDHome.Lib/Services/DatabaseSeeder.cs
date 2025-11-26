using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDHome.Lib.Data;
using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public class DatabaseSeeder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(IServiceProvider serviceProvider, ILogger<DatabaseSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        
        await SeedDevicesAsync(scope.ServiceProvider);
        await SeedSignalsAsync(scope.ServiceProvider);
        await SeedReadingsAsync(scope.ServiceProvider);
        await SeedTriggersAsync(scope.ServiceProvider);
    }

    private async Task SeedDevicesAsync(IServiceProvider serviceProvider)
    {
        var deviceRepo = serviceProvider.GetRequiredService<IDeviceRepository>();
        
        var existingDevices = await deviceRepo.GetAllAsync();
        if (existingDevices.Any())
        {
            _logger.LogInformation("Devices already seeded, skipping...");
            return;
        }

        var devices = new List<Device>
        {
            new Device
            {
                DeviceId = "living_room_light",
                FriendlyName = "Living Room Light",
                IeeeAddress = "0x00124b001f2a3b4c",
                ModelId = "LED1545G12",
                Manufacturer = "IKEA",
                Description = "TRADFRI bulb E27 WS opal 1000lm",
                PowerSource = true,
                DeviceType = DeviceType.Light,
                Room = "Living Room",
                Capabilities = new List<string> { "state", "brightness", "color_temp", "color" },
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            },
            new Device
            {
                DeviceId = "bedroom_switch",
                FriendlyName = "Bedroom Switch",
                IeeeAddress = "0x00124b001f2a3b5d",
                ModelId = "TRADFRI wireless dimmer",
                Manufacturer = "IKEA",
                Description = "TRADFRI wireless dimmer",
                PowerSource = false,
                DeviceType = DeviceType.Switch,
                Room = "Bedroom",
                Capabilities = new List<string> { "battery", "action", "linkquality" },
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            },
            new Device
            {
                DeviceId = "kitchen_sensor",
                FriendlyName = "Kitchen Motion Sensor",
                IeeeAddress = "0x00124b001f2a3b6e",
                ModelId = "SNZB-03",
                Manufacturer = "SONOFF",
                Description = "Motion sensor",
                PowerSource = false,
                DeviceType = DeviceType.Sensor,
                Room = "Kitchen",
                Capabilities = new List<string> { "occupancy", "battery", "voltage", "linkquality" },
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            },
            new Device
            {
                DeviceId = "office_temp_sensor",
                FriendlyName = "Office Temperature Sensor",
                IeeeAddress = "0x00124b001f2a3b7f",
                ModelId = "SNZB-02",
                Manufacturer = "SONOFF",
                Description = "Temperature and humidity sensor",
                PowerSource = false,
                DeviceType = DeviceType.Sensor,
                Room = "Office",
                Capabilities = new List<string> { "temperature", "humidity", "battery", "voltage", "linkquality" },
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            },
            new Device
            {
                DeviceId = "front_door_lock",
                FriendlyName = "Front Door Lock",
                IeeeAddress = "0x00124b001f2a3b8g",
                ModelId = "YRD256",
                Manufacturer = "Yale",
                Description = "Smart door lock",
                PowerSource = false,
                DeviceType = DeviceType.Lock,
                Room = "Entrance",
                Capabilities = new List<string> { "lock_state", "battery", "action" },
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            },
            new Device
            {
                DeviceId = "garage_light",
                FriendlyName = "Garage Light",
                IeeeAddress = "0x00124b001f2a3b9h",
                ModelId = "LED1732G11",
                Manufacturer = "IKEA",
                Description = "TRADFRI LED bulb E27",
                PowerSource = true,
                DeviceType = DeviceType.Light,
                Room = "Garage",
                Capabilities = new List<string> { "state", "brightness", "linkquality" },
                IsAvailable = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow.AddHours(-2)
            }
        };

        foreach (var device in devices)
        {
            await deviceRepo.CreateAsync(device);
            _logger.LogInformation("Seeded device: {DeviceId}", device.DeviceId);
        }
    }

    private async Task SeedSignalsAsync(IServiceProvider serviceProvider)
    {
        var signalRepo = serviceProvider.GetRequiredService<ISignalEventsRepository>();
        
        var existingSignals = await signalRepo.GetRecentAsync(1);
        if (existingSignals.Any())
        {
            _logger.LogInformation("Signals already seeded, skipping...");
            return;
        }

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var signals = new List<SignalEvent>();

        // Generate signals for the past hour
        for (int i = 0; i < 50; i++)
        {
            var timestamp = baseTime.AddMinutes(i);
            
            signals.Add(new SignalEvent(
                Id: Guid.NewGuid(),
                Source: "seed",
                DeviceId: "living_room_light",
                Location: "Living Room",
                Capability: "state",
                EventType: "state_change",
                EventSubType: null,
                Value: i % 3 == 0 ? 1.0 : 0.0,
                TimestampUtc: timestamp,
                RawTopic: "sdhome/living_room_light/state",
                RawPayload: default,
                DeviceKind: DeviceKind.Light,
                EventCategory: EventCategory.Trigger,
                RawPayloadArray: null
            ));

            if (i % 5 == 0)
            {
                signals.Add(new SignalEvent(
                    Id: Guid.NewGuid(),
                    Source: "seed",
                    DeviceId: "office_temp_sensor",
                    Location: "Office",
                    Capability: "temperature",
                    EventType: "sensor_reading",
                    EventSubType: null,
                    Value: 20 + (i % 10) * 0.5,
                    TimestampUtc: timestamp,
                    RawTopic: "sdhome/office_temp_sensor/temperature",
                    RawPayload: default,
                    DeviceKind: DeviceKind.Thermometer,
                    EventCategory: EventCategory.Telemetry,
                    RawPayloadArray: null
                ));

                signals.Add(new SignalEvent(
                    Id: Guid.NewGuid(),
                    Source: "seed",
                    DeviceId: "office_temp_sensor",
                    Location: "Office",
                    Capability: "humidity",
                    EventType: "sensor_reading",
                    EventSubType: null,
                    Value: 45 + (i % 15),
                    TimestampUtc: timestamp,
                    RawTopic: "sdhome/office_temp_sensor/humidity",
                    RawPayload: default,
                    DeviceKind: DeviceKind.Thermometer,
                    EventCategory: EventCategory.Telemetry,
                    RawPayloadArray: null
                ));
            }

            if (i % 7 == 0)
            {
                signals.Add(new SignalEvent(
                    Id: Guid.NewGuid(),
                    Source: "seed",
                    DeviceId: "kitchen_sensor",
                    Location: "Kitchen",
                    Capability: "occupancy",
                    EventType: "trigger",
                    EventSubType: "motion_detected",
                    Value: i % 14 == 0 ? 1.0 : 0.0,
                    TimestampUtc: timestamp,
                    RawTopic: "sdhome/kitchen_sensor/occupancy",
                    RawPayload: default,
                    DeviceKind: DeviceKind.MotionSensor,
                    EventCategory: EventCategory.Trigger,
                    RawPayloadArray: null
                ));
            }

            if (i % 10 == 0)
            {
                signals.Add(new SignalEvent(
                    Id: Guid.NewGuid(),
                    Source: "seed",
                    DeviceId: "bedroom_switch",
                    Location: "Bedroom",
                    Capability: "action",
                    EventType: "trigger",
                    EventSubType: i % 20 == 0 ? "button_on" : "button_brightness_up",
                    Value: null,
                    TimestampUtc: timestamp,
                    RawTopic: "sdhome/bedroom_switch/action",
                    RawPayload: default,
                    DeviceKind: DeviceKind.Switch,
                    EventCategory: EventCategory.Trigger,
                    RawPayloadArray: null
                ));
            }
        }

        foreach (var signal in signals)
        {
            await signalRepo.InsertAsync(signal);
        }

        _logger.LogInformation("Seeded {Count} signal events", signals.Count);
    }

    private async Task SeedReadingsAsync(IServiceProvider serviceProvider)
    {
        var readingsRepo = serviceProvider.GetRequiredService<ISensorReadingsRepository>();
        
        var existingReadings = await readingsRepo.GetRecentAsync(1);
        if (existingReadings.Any())
        {
            _logger.LogInformation("Readings already seeded, skipping...");
            return;
        }

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var readings = new List<SensorReading>();

        for (int i = 0; i < 30; i++)
        {
            var timestamp = baseTime.AddMinutes(i * 2);
            
            readings.Add(new SensorReading(
                Id: Guid.NewGuid(),
                SignalEventId: Guid.NewGuid(),
                TimestampUtc: timestamp,
                DeviceId: "office_temp_sensor",
                Metric: "temperature",
                Value: 20.0 + (i % 10) * 0.5,
                Unit: "°C"
            ));

            readings.Add(new SensorReading(
                Id: Guid.NewGuid(),
                SignalEventId: Guid.NewGuid(),
                TimestampUtc: timestamp,
                DeviceId: "office_temp_sensor",
                Metric: "humidity",
                Value: 45.0 + (i % 15),
                Unit: "%"
            ));

            if (i % 3 == 0)
            {
                readings.Add(new SensorReading(
                    Id: Guid.NewGuid(),
                    SignalEventId: Guid.NewGuid(),
                    TimestampUtc: timestamp,
                    DeviceId: "kitchen_sensor",
                    Metric: "battery",
                    Value: 85.0 - (i * 0.1),
                    Unit: "%"
                ));
            }
        }

        await readingsRepo.InsertManyAsync(readings);

        _logger.LogInformation("Seeded {Count} sensor readings", readings.Count);
    }

    private async Task SeedTriggersAsync(IServiceProvider serviceProvider)
    {
        var triggerRepo = serviceProvider.GetRequiredService<ITriggerEventsRepository>();
        
        var existingTriggers = await triggerRepo.GetRecentAsync(1);
        if (existingTriggers.Any())
        {
            _logger.LogInformation("Triggers already seeded, skipping...");
            return;
        }

        var baseTime = DateTime.UtcNow.AddHours(-1);
        var triggers = new List<TriggerEvent>();

        for (int i = 0; i < 20; i++)
        {
            var timestamp = baseTime.AddMinutes(i * 3);
            
            if (i % 3 == 0)
            {
                triggers.Add(new TriggerEvent(
                    Id: Guid.NewGuid(),
                    SignalEventId: Guid.NewGuid(),
                    TimestampUtc: timestamp,
                    DeviceId: "kitchen_sensor",
                    Capability: "occupancy",
                    TriggerType: "motion_detected",
                    TriggerSubType: null,
                    Value: true
                ));
            }

            if (i % 5 == 0)
            {
                triggers.Add(new TriggerEvent(
                    Id: Guid.NewGuid(),
                    SignalEventId: Guid.NewGuid(),
                    TimestampUtc: timestamp,
                    DeviceId: "bedroom_switch",
                    Capability: "action",
                    TriggerType: "button_pressed",
                    TriggerSubType: "on",
                    Value: null
                ));
            }

            if (i % 7 == 0)
            {
                triggers.Add(new TriggerEvent(
                    Id: Guid.NewGuid(),
                    SignalEventId: Guid.NewGuid(),
                    TimestampUtc: timestamp,
                    DeviceId: "front_door_lock",
                    Capability: "lock_state",
                    TriggerType: "lock_state_changed",
                    TriggerSubType: "locked",
                    Value: true
                ));
            }
        }

        foreach (var trigger in triggers)
        {
            await triggerRepo.InsertAsync(trigger);
        }

        _logger.LogInformation("Seeded {Count} trigger events", triggers.Count);
    }
}
