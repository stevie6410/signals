using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Generic;
using MQTTnet;
using Npgsql;
using Prometheus;
using Serilog;

public enum DeviceKind
{
    Unknown,
    Button,
    MotionSensor,
    ContactSensor,
    Thermometer,
    Light,
    Switch,
    Outlet,
}

public enum EventCategory
{
    Trigger,    // “do something now”
    Telemetry,  // state/value updates
}

public sealed record SignalEvent(
    Guid Id,
    string Source,
    string DeviceId,
    string? Location,
    string Capability,
    string EventType,
    string? EventSubType,
    double? Value,
    DateTime TimestampUtc,
    string RawTopic,
    JsonElement RawPayload,
    DeviceKind DeviceKind,
    EventCategory EventCategory);

class Program
{
    // Prometheus metrics
    private static readonly Counter ReceivedEventsTotal =
        Metrics.CreateCounter("signals_events_total", "Total number of SignalEvents received.");

    private static readonly Counter ReceivedEventsByDeviceTotal =
        Metrics.CreateCounter("signals_events_by_device_total",
            "Total SignalEvents received per device.",
            new[] { "device" });

    // Declarative map of known devices → kinds
    private static readonly Dictionary<string, DeviceKind> DeviceKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Buttons
        ["Bedroom Switch Steve"] = DeviceKind.Button,

        // Lights / switches
        ["front_room_lamp"] = DeviceKind.Light,

        // Add more as you go:
        // ["hallway_motion"] = DeviceKind.MotionSensor,
        // ["bedroom_temperature"] = DeviceKind.Thermometer,
    };

    // Shared HttpClient for webhook calls 👇
    private static readonly HttpClient HttpClient = new();

    static async Task Main(string[] args)
    {
        // --- Configuration from environment ---
        var brokerAddress = Environment.GetEnvironmentVariable("MQTT__Host") ?? "localhost";
        var portEnv = Environment.GetEnvironmentVariable("MQTT__Port");
        var brokerPort = int.TryParse(portEnv, out var parsedPort) ? parsedPort : 1883;

        var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341";
        var postgresConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

        // n8n webhook URL (e.g. http://n8n:5678/webhook/signals/event)
        var n8nWebhookUrl = Environment.GetEnvironmentVariable("N8N_WEBHOOK_URL");
        var n8nWebhookTestUrl = Environment.GetEnvironmentVariable("N8N_WEBHOOK_TEST_URL");

        // --- Serilog configuration ---
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq(seqUrl)
            .CreateLogger();

        Log.Information(
            "Starting Signals service. MQTT host: {Host}, Port: {Port}, Seq: {SeqUrl}, PostgresConfigured: {PgConfigured}, WebhookConfigured: {WebhookConfigured}",
            brokerAddress, brokerPort, seqUrl,
            !string.IsNullOrWhiteSpace(postgresConnectionString),
            !string.IsNullOrWhiteSpace(n8nWebhookUrl));

        // --- Start Prometheus metrics HTTP server (Kestrel-based) ---
        var metricServer = new KestrelMetricServer(port: 5050);
        metricServer.Start();
        Log.Information("📈 Metrics server running on http://0.0.0.0:5050/metrics");

        if (!string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            await EnsureTableExistsAsync(postgresConnectionString);
        }

        try
        {
            var topicFilter = "sdhome/#";

            var factory = new MqttClientFactory();
            var mqttClient = factory.CreateMqttClient();

            var mqttClientOptions = new MqttClientOptionsBuilder()
                .WithClientId("SignalsClient-" + Guid.NewGuid())
                .WithTcpServer(brokerAddress, brokerPort)
                .WithCleanSession()
                .Build();

            mqttClient.ConnectedAsync += _ =>
            {
                Log.Information("✅ Connected to MQTT broker.");
                return Task.CompletedTask;
            };

            mqttClient.DisconnectedAsync += _ =>
            {
                Log.Warning("⚠️ Disconnected from MQTT broker.");
                return Task.CompletedTask;
            };

            mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var topic = e.ApplicationMessage.Topic;
    
                // 👇 Ignore Zigbee2MQTT bridge logging messages
                if (topic.Equals("sdhome/bridge/logging", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }
    
                var payloadString = e.ApplicationMessage.ConvertPayloadToString();

                try
                {
                    using var doc = JsonDocument.Parse(payloadString);
                    var root = doc.RootElement.Clone();

                    var signalEvent = MapToSignalEvent(topic, root);

                    var isAutomationTrigger = signalEvent.EventCategory == EventCategory.Trigger;

                    // --- Metrics ---
                    ReceivedEventsTotal.Inc();
                    ReceivedEventsByDeviceTotal.WithLabels(signalEvent.DeviceId).Inc();

                    // --- Persist to Postgres (fire-and-forget) ---
                    if (!string.IsNullOrWhiteSpace(postgresConnectionString))
                    {
                        _ = InsertEventAsync(postgresConnectionString, signalEvent);
                    }

                    // --- Structured log to Seq + console ---
                    Log.Information("SignalEvent received {@SignalEvent}", signalEvent);

                    // --- Pretty console output ---
                    PrintPrettyEvent(signalEvent);

                    // --- Send to n8n webhook (fire-and-forget) ---
                    // Only send automation-trigger events to avoid spamming n8n and creating loops
                    if (isAutomationTrigger)
                    {
                        if (!string.IsNullOrWhiteSpace(n8nWebhookUrl))
                        {
                            _ = SendToWebhookAsync(n8nWebhookUrl, signalEvent);
                        }
                        if (!string.IsNullOrWhiteSpace(n8nWebhookTestUrl))
                        {
                            _ = SendToWebhookAsync(n8nWebhookTestUrl, signalEvent);
                        }
                    }
                }
                catch (JsonException)
                {
                    Log.Warning("Received non-JSON payload on {Topic}: {Payload}", topic, payloadString);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error handling MQTT message on {Topic} with payload {Payload}", topic, payloadString);
                }

                return Task.CompletedTask;
            };

            await mqttClient.ConnectAsync(mqttClientOptions);
            Log.Information("Connecting to MQTT broker {Host}:{Port}...", brokerAddress, brokerPort);

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topicFilter))
                .Build();

            await mqttClient.SubscribeAsync(subscribeOptions);
            Log.Information("🔔 Subscribed to MQTT topic filter {TopicFilter}", topicFilter);

            Log.Information("Signals service is running. Press Ctrl+C to exit (in non-container runs).");
            await Task.Delay(-1);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception in Signals service");
        }
        finally
        {
            metricServer.Stop();
            Log.CloseAndFlush();
        }
    }

    private static SignalEvent MapToSignalEvent(string topic, JsonElement payload)
    {
        const string zigbeePrefix = "sdhome/";

        string source;
        string deviceId;

        if (topic.StartsWith(zigbeePrefix, StringComparison.OrdinalIgnoreCase))
        {
            source = "sdhome";
            deviceId = topic[zigbeePrefix.Length..];
        }
        else
        {
            source = "mqtt";
            deviceId = topic;
        }

        // 1. Look up device kind (fallback Unknown)
        DeviceKind deviceKind = DeviceKinds.TryGetValue(deviceId, out var kind)
            ? kind
            : DeviceKind.Unknown;

        string capability = "unknown";
        string eventType = "unknown";
        string? eventSubType = null;
        double? value = null;
        string? location = null;

        // Button actions: "1_single", "2_double", "4_hold"
        if (payload.TryGetProperty("action", out var actionProp)
            && actionProp.ValueKind == JsonValueKind.String)
        {
            var action = actionProp.GetString() ?? string.Empty;

            capability = "button";
            eventType = "press";

            var parts = action.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                // var buttonIndex = parts[0];
                eventSubType = parts[1]; // single / double / hold / etc.
            }
            else
            {
                eventSubType = action;
            }

            if (deviceKind == DeviceKind.Unknown)
            {
                deviceKind = DeviceKind.Button;
            }
        }

        // Example: temperature sensor
        if (payload.TryGetProperty("temperature", out var tempProp)
            && tempProp.ValueKind == JsonValueKind.Number)
        {
            capability = "temperature";
            eventType = "measurement";
            eventSubType ??= "current";
            value = tempProp.GetDouble();

            if (deviceKind == DeviceKind.Unknown)
            {
                deviceKind = DeviceKind.Thermometer;
            }
        }

        // Example: motion sensor (Zigbee occupancy)
        if (payload.TryGetProperty("occupancy", out var occProp) &&
            (occProp.ValueKind == JsonValueKind.True || occProp.ValueKind == JsonValueKind.False))
        {
            capability = "motion";
            eventType = "detection";
            eventSubType = occProp.GetBoolean() ? "active" : "inactive";

            if (deviceKind == DeviceKind.Unknown)
            {
                deviceKind = DeviceKind.MotionSensor;
            }
        }

        // 3. Decide EventCategory (trigger vs telemetry)
        EventCategory eventCategory = EventCategory.Telemetry;

        if (deviceKind is DeviceKind.Button or DeviceKind.MotionSensor or DeviceKind.ContactSensor
            && capability is "button" or "motion"
            && eventType is "press" or "detection")
        {
            eventCategory = EventCategory.Trigger;
        }

        return new SignalEvent(
            Id: Guid.NewGuid(),
            Source: source,
            DeviceId: deviceId,
            Location: location,
            Capability: capability,
            EventType: eventType,
            EventSubType: eventSubType,
            Value: value,
            TimestampUtc: DateTime.UtcNow,
            RawTopic: topic,
            RawPayload: payload,
            DeviceKind: deviceKind,
            EventCategory: eventCategory);
    }

    private static void PrintPrettyEvent(SignalEvent ev)
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

    // 👉 New helper to send events to n8n
    private static async Task SendToWebhookAsync(string webhookUrl, SignalEvent ev)
    {
        try
        {
            var response = await HttpClient.PostAsJsonAsync(webhookUrl, ev);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("n8n webhook returned non-success status code {StatusCode} for SignalEvent {Id}",
                    response.StatusCode, ev.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending SignalEvent {Id} to n8n webhook", ev.Id);
        }
    }

    private static async Task InsertEventAsync(string connectionString, SignalEvent ev)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = @"
INSERT INTO signal_events
    (id, timestamp_utc, source, device_id, location, capability,
     event_type, event_sub_type, value, raw_topic, raw_payload)
VALUES
    (@Id, @TimestampUtc, @Source, @DeviceId, @Location, @Capability,
     @EventType, @EventSubType, @Value, @RawTopic, @RawPayload);";

            await using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Id", ev.Id);
            cmd.Parameters.AddWithValue("@TimestampUtc", ev.TimestampUtc);
            cmd.Parameters.AddWithValue("@Source", ev.Source);
            cmd.Parameters.AddWithValue("@DeviceId", ev.DeviceId);
            cmd.Parameters.AddWithValue("@Location", (object?)ev.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Capability", ev.Capability);
            cmd.Parameters.AddWithValue("@EventType", ev.EventType);
            cmd.Parameters.AddWithValue("@EventSubType", (object?)ev.EventSubType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Value", (object?)ev.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RawTopic", ev.RawTopic);

            var rawPayloadJson = JsonSerializer.Serialize(ev.RawPayload);
            cmd.Parameters.AddWithValue("@RawPayload", NpgsqlTypes.NpgsqlDbType.Jsonb, rawPayloadJson);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to insert SignalEvent {Id} into PostgreSQL", ev.Id);
        }
    }

    private static async Task EnsureTableExistsAsync(string connectionString)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS signal_events (
                id              uuid PRIMARY KEY,
                timestamp_utc   timestamptz      NOT NULL,
                source          text             NOT NULL,
                device_id       text             NOT NULL,
                location        text             NULL,
                capability      text             NOT NULL,
                event_type      text             NOT NULL,
                event_sub_type  text             NULL,
                value           double precision NULL,
                raw_topic       text             NOT NULL,
                raw_payload     jsonb            NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_signal_events_timestamp
                ON signal_events (timestamp_utc);

            CREATE INDEX IF NOT EXISTS ix_signal_events_device_timestamp
                ON signal_events (device_id, timestamp_utc DESC);
            ";

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();

            Log.Information("📦 PostgreSQL table 'signal_events' ensured.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Failed to ensure PostgreSQL table exists.");
            throw;
        }
    }
}
