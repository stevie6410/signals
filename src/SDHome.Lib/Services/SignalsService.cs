using System.Diagnostics;
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
    
    // Topics to skip for pipeline processing (still logged but not processed through full pipeline)
    private static readonly string[] SkipTopicPatterns = [
        "/set",           // Outgoing commands we send
        "/get",           // Outgoing state requests
        "bridge/logging", // Zigbee2MQTT internal logs
        "bridge/state",   // Bridge online/offline status
        "bridge/info",    // Bridge info updates
    ];
    
    private static bool ShouldSkipTopic(string topic)
    {
        foreach (var pattern in SkipTopicPatterns)
        {
            if (topic.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task HandleMqttMessageAsync(
        string topic,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var pipelineStart = Stopwatch.GetTimestamp();
        
        if (string.IsNullOrWhiteSpace(payload))
        {
            Log.Warning("Received empty payload on topic {Topic}. Skipping.", topic);
            return;
        }
        
        // Skip noisy topics that don't need full pipeline processing
        if (ShouldSkipTopic(topic))
        {
            Log.Debug("Skipping topic {Topic} (matches skip pattern)", topic);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement.Clone();

            SignalEvent ev = root.ValueKind == JsonValueKind.Array
                ? mapper.MapArrayPayload(topic, root)
                : mapper.Map(topic, root);

            var parseTime = Stopwatch.GetElapsedTime(pipelineStart);

            ReceivedEventsTotal.Inc();
            ReceivedEventsByDeviceTotal.WithLabels(ev.DeviceId).Inc();

            // 1. Persist raw event to signal_events
            var dbStart = Stopwatch.GetTimestamp();
            db.SignalEvents.Add(SignalEventEntity.FromModel(ev));
            await db.SaveChangesAsync(cancellationToken);
            var dbTime = Stopwatch.GetElapsedTime(dbStart);

            // 2. Broadcast to real-time clients (SignalR)
            var broadcastStart = Stopwatch.GetTimestamp();
            await realtimeBroadcaster.BroadcastSignalEventAsync(ev);

            // 2b. Broadcast device state update for instant UI updates
            // Note: LinkQuality is updated by DeviceStateSyncWorker to avoid concurrency issues
            if (root.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(ev.DeviceId))
            {
                var stateUpdate = ExtractDeviceStateUpdate(ev.DeviceId, root);
                if (stateUpdate.State.Count > 0)
                {
                    await realtimeBroadcaster.BroadcastDeviceStateUpdateAsync(stateUpdate);
                }
            }
            var broadcastTime = Stopwatch.GetElapsedTime(broadcastStart);

            // 3. Project into specialized tables (triggers, readings, etc.) + automation processing
            // Pass pipeline context so E2E tracker can build complete timeline
            var pipelineContext = new PipelineContext(
                ParseMs: parseTime.TotalMilliseconds,
                DatabaseMs: dbTime.TotalMilliseconds,
                BroadcastMs: broadcastTime.TotalMilliseconds
            );
            
            var projectionStart = Stopwatch.GetTimestamp();
            var projectedData = await projectionService.ProjectAsync(ev, cancellationToken, pipelineContext);
            var projectionTime = Stopwatch.GetElapsedTime(projectionStart);

            // 4. Forward triggers to webhook (n8n) if configured
            var webhookStart = Stopwatch.GetTimestamp();
            if (!string.IsNullOrWhiteSpace(_n8nWebhookUrl))
            {
                // Send primary trigger
                if (projectedData.Trigger != null)
                {
                    await SendTriggerToWebhookAsync(_n8nWebhookUrl, projectedData.Trigger, cancellationToken);
                }
                
                // Send custom triggers
                foreach (var customTrigger in projectedData.CustomTriggers)
                {
                    await SendTriggerToWebhookAsync(_n8nWebhookUrl, customTrigger, cancellationToken);
                }
            }
            var webhookTime = Stopwatch.GetElapsedTime(webhookStart);

            var totalTime = Stopwatch.GetElapsedTime(pipelineStart);
            
            // Build and broadcast pipeline timeline for visualization
            var timeline = new PipelineTimeline
            {
                DeviceId = ev.DeviceId,
                TotalMs = totalTime.TotalMilliseconds,
                Stages = new List<PipelineStage>
                {
                    new() { Name = "Parse", DurationMs = parseTime.TotalMilliseconds, StartOffsetMs = 0, Category = "signal" },
                    new() { Name = "Database", DurationMs = dbTime.TotalMilliseconds, StartOffsetMs = parseTime.TotalMilliseconds, Category = "db" },
                    new() { Name = "Broadcast", DurationMs = broadcastTime.TotalMilliseconds, StartOffsetMs = parseTime.TotalMilliseconds + dbTime.TotalMilliseconds, Category = "broadcast" },
                    new() { Name = "Automation", DurationMs = projectionTime.TotalMilliseconds, StartOffsetMs = parseTime.TotalMilliseconds + dbTime.TotalMilliseconds + broadcastTime.TotalMilliseconds, Category = "automation" },
                }
            };
            
            if (!string.IsNullOrWhiteSpace(_n8nWebhookUrl))
            {
                timeline.Stages.Add(new PipelineStage 
                { 
                    Name = "Webhook", 
                    DurationMs = webhookTime.TotalMilliseconds, 
                    StartOffsetMs = parseTime.TotalMilliseconds + dbTime.TotalMilliseconds + broadcastTime.TotalMilliseconds + projectionTime.TotalMilliseconds, 
                    Category = "webhook" 
                });
            }
            
            // Always broadcast timeline for visualization (even for fast signals)
            await realtimeBroadcaster.BroadcastPipelineTimelineAsync(timeline);
            
            // Log timing breakdown for signals that took more than 50ms
            if (totalTime.TotalMilliseconds > 50)
            {
                Log.Information(
                    "⏱️ Signal Pipeline [{DeviceId}] Total: {Total:F1}ms | Parse: {Parse:F1}ms | DB: {Db:F1}ms | Broadcast: {Broadcast:F1}ms | Projection+Automation: {Projection:F1}ms | Webhook: {Webhook:F1}ms",
                    ev.DeviceId,
                    totalTime.TotalMilliseconds,
                    parseTime.TotalMilliseconds,
                    dbTime.TotalMilliseconds,
                    broadcastTime.TotalMilliseconds,
                    projectionTime.TotalMilliseconds,
                    webhookTime.TotalMilliseconds);
            }

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

    private async Task SendTriggerToWebhookAsync(
        string webhookUrl,
        TriggerEvent trigger,
        CancellationToken ct)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(webhookUrl, trigger, ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning(
                    "n8n webhook returned {StatusCode} for TriggerEvent {Id}",
                    response.StatusCode,
                    trigger.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending TriggerEvent {Id} to n8n webhook", ex, trigger.Id);
        }
    }
}
