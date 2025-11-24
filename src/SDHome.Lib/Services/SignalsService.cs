using Microsoft.Extensions.Options;
using Prometheus;
using SDHome.Lib.Data;
using SDHome.Lib.Mappers;
using SDHome.Lib.Models;
using Serilog;
using System.Net.Http.Json;
using System.Text.Json;

namespace SDHome.Lib.Services
{
    public class SignalsService : ISignalsService
    {
        private static readonly Counter ReceivedEventsTotal =
            Metrics.CreateCounter("signals_events_total", "Total number of SignalEvents received.");

        private static readonly Counter ReceivedEventsByDeviceTotal =
            Metrics.CreateCounter("signals_events_by_device_total",
                "Total SignalEvents received per device.",
                new[] { "device" });

        private readonly ISignalEventMapper _mapper;
        private readonly ISignalEventsRepository _repository;
        private readonly HttpClient _httpClient;
        private readonly IOptions<WebhookOptions> _webhookOptions;
        private readonly string? _n8nWebhookUrl;
        private readonly string? _n8nWebhookTestUrl;

        public SignalsService(
            ISignalEventMapper mapper,
            ISignalEventsRepository repository,
            HttpClient httpClient,
            IOptions<WebhookOptions> webhookOptions)
        {
            _mapper = mapper;
            _repository = repository;
            _httpClient = httpClient;
            _webhookOptions = webhookOptions;
            _n8nWebhookUrl = webhookOptions.Value.Main;
            _n8nWebhookTestUrl = webhookOptions.Value.Test;
        }

        public async Task HandleMqttMessageAsync(string topic, string payload, CancellationToken cancellationToken = default)
        {
            // if (topic.StartsWith("sdhome/bridge")) return;


            if (string.IsNullOrWhiteSpace(payload))
            {
                Log.Warning("Received empty payload on topic {Topic}. Skipping.", topic);
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement.Clone();

                SignalEvent ev;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    ev = _mapper.MapArrayPayload(topic, root);
                }
                else
                {
                    ev = _mapper.Map(topic, root);
                }

                ReceivedEventsTotal.Inc();
                ReceivedEventsByDeviceTotal.WithLabels(ev.DeviceId).Inc();

                await _repository.InsertAsync(ev, cancellationToken);

                if (!string.IsNullOrWhiteSpace(_n8nWebhookUrl))
                {
                    await SendToWebhookAsync(_n8nWebhookUrl!, ev, cancellationToken);
                }

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
                "button" => "🔵",
                "temperature" => "🌡️",
                "motion" => "🏃",
                _ => "📡"
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

        private async Task SendToWebhookAsync(string webhookUrl, SignalEvent ev, CancellationToken ct)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(webhookUrl, ev, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("n8n webhook returned {StatusCode} for SignalEvent {Id}",
                        response.StatusCode, ev.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending SignalEvent {Id} to n8n webhook", ev.Id);
            }
        }
    }
}
