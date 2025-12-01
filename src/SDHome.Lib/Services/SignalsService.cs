using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Prometheus;
using SDHome.Lib.Data;
using SDHome.Lib.Data.Entities;
using SDHome.Lib.Mappers;
using SDHome.Lib.Models;
using Serilog;

namespace SDHome.Lib.Services;

public class SignalsService(
    ISignalEventMapper mapper,
    SignalsDbContext db,
    HttpClient httpClient,
    IOptions<WebhookOptions> webhookOptions,
    ISignalEventProjectionService projectionService,
    IRealtimeEventBroadcaster realtimeBroadcaster) : ISignalsService
{
    private static readonly Counter ReceivedEventsTotal =
        Metrics.CreateCounter("signals_events_total", "Total number of SignalEvents received.");

    private static readonly Counter ReceivedEventsByDeviceTotal =
        Metrics.CreateCounter(
            "signals_events_by_device_total",
            "Total SignalEvents received per device.",
            new[] { "device" });

    private readonly string? _n8nWebhookUrl = webhookOptions.Value.Main;

    public async Task HandleMqttMessageAsync(
        string topic,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            Log.Warning("Received empty payload on topic {Topic}. Skipping.", topic);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement.Clone();

            SignalEvent ev = root.ValueKind == JsonValueKind.Array
                ? mapper.MapArrayPayload(topic, root)
                : mapper.Map(topic, root);

            ReceivedEventsTotal.Inc();
            ReceivedEventsByDeviceTotal.WithLabels(ev.DeviceId).Inc();

            // 1. Persist raw event to signal_events
            db.SignalEvents.Add(SignalEventEntity.FromModel(ev));
            await db.SaveChangesAsync(cancellationToken);

            // 2. Broadcast to real-time clients (SignalR)
            await realtimeBroadcaster.BroadcastSignalEventAsync(ev);

            // 2b. Broadcast device state update for instant UI updates
            if (root.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(ev.DeviceId))
            {
                var stateUpdate = ExtractDeviceStateUpdate(ev.DeviceId, root);
                if (stateUpdate.State.Count > 0)
                {
                    await realtimeBroadcaster.BroadcastDeviceStateUpdateAsync(stateUpdate);
                }
            }

            // 3. Project into specialized tables (triggers, readings, etc.)
            var projectedData = await projectionService.ProjectAsync(ev, cancellationToken);

            // 4. Forward to webhook (n8n) if configured
            if (!string.IsNullOrWhiteSpace(_n8nWebhookUrl))
            {
                await SendToWebhookAsync(_n8nWebhookUrl, ev, cancellationToken);
            }

            // 5. Pretty-print to console for local debugging
            PrintPrettyEvent(ev);
        }
        catch (JsonException)
        {
            Log.Warning("Received non-JSON payload on {Topic}: {Payload}", topic, payload);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling MQTT message on {Topic} with payload {Payload}", topic, payload);
        }
    }

    /// <summary>
    /// Extract device state from MQTT payload for real-time UI updates
    /// </summary>
    private static DeviceStateUpdate ExtractDeviceStateUpdate(string deviceId, JsonElement payload)
    {
        var state = new Dictionary<string, object?>();

        foreach (var prop in payload.EnumerateObject())
        {
            // Convert JsonElement to appropriate .NET type
            state[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.ToString()
            };
        }

        return new DeviceStateUpdate
        {
            DeviceId = deviceId,
            State = state,
            TimestampUtc = DateTime.UtcNow
        };
    }

    private static void PrintPrettyEvent(SignalEvent ev)
    {
        string emoji = ev.Capability switch
        {
            "button"      => "🔵",
            "temperature" => "🌡️",
            "motion"      => "🏃",
            _             => "📡"
        };

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{emoji} {ev.Capability}/{ev.EventType}/{ev.EventSubType} from {ev.DeviceId}");
        Console.ResetColor();

        string prettyJson = JsonSerializer.Serialize(ev, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(prettyJson);
        Console.ResetColor();
        Console.WriteLine();
    }

    private async Task SendToWebhookAsync(
        string webhookUrl,
        SignalEvent ev,
        CancellationToken ct)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(webhookUrl, ev, ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning(
                    "n8n webhook returned {StatusCode} for SignalEvent {Id}",
                    response.StatusCode,
                    ev.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending SignalEvent {Id} to n8n webhook", ev.Id);
        }
    }
}
