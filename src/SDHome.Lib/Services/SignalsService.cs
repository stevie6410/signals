using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Prometheus;
using SDHome.Lib.Data;
using SDHome.Lib.Mappers;
using SDHome.Lib.Models;
using Serilog;

namespace SDHome.Lib.Services
{
    public class SignalsService : ISignalsService
    {
        private static readonly Counter ReceivedEventsTotal =
            Metrics.CreateCounter("signals_events_total", "Total number of SignalEvents received.");

        private static readonly Counter ReceivedEventsByDeviceTotal =
            Metrics.CreateCounter(
                "signals_events_by_device_total",
                "Total SignalEvents received per device.",
                new[] { "device" });

        private readonly ISignalEventMapper _mapper;
        private readonly ISignalEventsRepository _repository;
        private readonly ISignalEventProjectionService _projectionService;
        private readonly HttpClient _httpClient;
        private readonly IOptions<WebhookOptions> _webhookOptions;
        private readonly string? _n8nWebhookUrl;
        private readonly string? _n8nWebhookTestUrl;

        public SignalsService(
            ISignalEventMapper mapper,
            ISignalEventsRepository repository,
            HttpClient httpClient,
            IOptions<WebhookOptions> webhookOptions,
            ISignalEventProjectionService projectionService)
        {
            _mapper = mapper;
            _repository = repository;
            _httpClient = httpClient;
            _webhookOptions = webhookOptions;
            _n8nWebhookUrl = webhookOptions.Value.Main;
            _n8nWebhookTestUrl = webhookOptions.Value.Test;
            _projectionService = projectionService;
        }

        public async Task HandleMqttMessageAsync(
            string topic,
            string payload,
            CancellationToken cancellationToken = default)
        {
            // If you want to ignore bridge noise, uncomment:
            // if (topic.StartsWith("sdhome/bridge", StringComparison.OrdinalIgnoreCase)) return;

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
                    ? _mapper.MapArrayPayload(topic, root)
                    : _mapper.Map(topic, root);

                ReceivedEventsTotal.Inc();
                ReceivedEventsByDeviceTotal.WithLabels(ev.DeviceId).Inc();

                // 1. Persist raw event to signal_events
                await _repository.InsertAsync(ev, cancellationToken);

                // 2. Project into specialized tables (triggers, readings, etc.)
                await _projectionService.ProjectAsync(ev, cancellationToken);

                // 3. Forward to webhook (n8n) if configured
                if (!string.IsNullOrWhiteSpace(_n8nWebhookUrl))
                {
                    await SendToWebhookAsync(_n8nWebhookUrl!, ev, cancellationToken);
                }

                // 4. Pretty-print to console for local debugging
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

        private void PrintPrettyEvent(SignalEvent ev)
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
                var response = await _httpClient.PostAsJsonAsync(webhookUrl, ev, ct);

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
}
