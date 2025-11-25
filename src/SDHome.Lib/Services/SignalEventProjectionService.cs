using System.Text.Json;
using SDHome.Lib.Data;
using SDHome.Lib.Models;

namespace SDHome.Lib.Services
{
    public class SignalEventProjectionService : ISignalEventProjectionService
    {
        private readonly ITriggerEventsRepository _triggerRepo;
        private readonly ISensorReadingsRepository _readingsRepo;

        public SignalEventProjectionService(
            ITriggerEventsRepository triggerRepo,
            ISensorReadingsRepository readingsRepo)
        {
            _triggerRepo = triggerRepo;
            _readingsRepo = readingsRepo;
        }

        public async Task ProjectAsync(SignalEvent ev, CancellationToken cancellationToken = default)
        {
            // Example: handle motion sensor events
            if (ev.Capability == "motion" && ev.EventType == "detection")
            {
                await HandleMotionEventAsync(ev, cancellationToken);
            }

            // Later: handle other capabilities (contact, button, temp-only devices, etc.)
        }

        private async Task HandleMotionEventAsync(SignalEvent ev, CancellationToken ct)
        {
            var payload = ev.RawPayload;

            bool? occupancy = TryGetBool(payload, "occupancy");
            bool isActive = string.Equals(ev.EventSubType, "active", StringComparison.OrdinalIgnoreCase);

            var trigger = new TriggerEvent(
                Id: Guid.NewGuid(),
                SignalEventId: ev.Id,
                TimestampUtc: ev.TimestampUtc,
                DeviceId: ev.DeviceId,
                Capability: ev.Capability,
                TriggerType: "motion",
                TriggerSubType: ev.EventSubType,
                Value: occupancy ?? isActive
            );

            await _triggerRepo.InsertAsync(trigger, ct);

            var readings = new List<SensorReading>();

            if (TryGetDouble(payload, "device_temperature", out var temp))
            {
                readings.Add(new SensorReading(
                    Id: Guid.NewGuid(),
                    SignalEventId: ev.Id,
                    TimestampUtc: ev.TimestampUtc,
                    DeviceId: ev.DeviceId,
                    Metric: "temperature",
                    Value: temp,
                    Unit: "°C"
                ));
            }

            if (TryGetDouble(payload, "illuminance", out var lux))
            {
                readings.Add(new SensorReading(
                    Id: Guid.NewGuid(),
                    SignalEventId: ev.Id,
                    TimestampUtc: ev.TimestampUtc,
                    DeviceId: ev.DeviceId,
                    Metric: "illuminance",
                    Value: lux,
                    Unit: "lx" // or "raw_lux"
                ));
            }

            if (TryGetDouble(payload, "battery", out var battery))
            {
                readings.Add(new SensorReading(
                    Id: Guid.NewGuid(),
                    SignalEventId: ev.Id,
                    TimestampUtc: ev.TimestampUtc,
                    DeviceId: ev.DeviceId,
                    Metric: "battery",
                    Value: battery,
                    Unit: "%"
                ));
            }

            if (TryGetDouble(payload, "linkquality", out var lqi))
            {
                readings.Add(new SensorReading(
                    Id: Guid.NewGuid(),
                    SignalEventId: ev.Id,
                    TimestampUtc: ev.TimestampUtc,
                    DeviceId: ev.DeviceId,
                    Metric: "linkquality",
                    Value: lqi,
                    Unit: null
                ));
            }

            if (TryGetDouble(payload, "voltage", out var voltage))
            {
                readings.Add(new SensorReading(
                    Id: Guid.NewGuid(),
                    SignalEventId: ev.Id,
                    TimestampUtc: ev.TimestampUtc,
                    DeviceId: ev.DeviceId,
                    Metric: "voltage",
                    Value: voltage / 1000.0, // optional conversion
                    Unit: "V"
                ));
            }

            if (readings.Count > 0)
            {
                await _readingsRepo.InsertManyAsync(readings, ct);
            }
        }

        private static bool? TryGetBool(JsonElement payload, string name)
        {
            if (payload.ValueKind != JsonValueKind.Object)
                return null;

            if (payload.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
            if (payload.TryGetProperty(name, out prop) && prop.ValueKind == JsonValueKind.False)
                return false;

            return null;
        }

        private static bool TryGetDouble(JsonElement payload, string name, out double value)
        {
            value = default;

            if (payload.ValueKind != JsonValueKind.Object)
                return false;

            if (!payload.TryGetProperty(name, out var prop))
                return false;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
                return true;

            return false;
        }
    }
}

