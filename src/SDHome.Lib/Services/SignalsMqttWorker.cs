using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public class SignalsMqttWorker : BackgroundService
{
    private readonly ILogger<SignalsMqttWorker> _logger;
    private readonly ISignalsService _signalsService;
    private readonly MqttOptions _mqttOptions;

    public SignalsMqttWorker(
        ISignalsService signalsService,
        IOptions<MqttOptions> mqttOptions,
        ILogger<SignalsMqttWorker> logger)
    {
        _signalsService = signalsService;
        _mqttOptions = mqttOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_mqttOptions.Enabled)
        {
            _logger.LogInformation("MQTT is disabled in configuration. Skipping MQTT connection.");
            return;
        }

        var factory = new MqttClientFactory();
        var client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId("SDHomeSignals-" + Guid.NewGuid())
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithCleanSession()
            .Build();

        client.ConnectedAsync += _ =>
        {
            _logger.LogInformation("✅ Connected to MQTT broker {Host}:{Port}", _mqttOptions.Host, _mqttOptions.Port);
            return Task.CompletedTask;
        };

        client.DisconnectedAsync += _ =>
        {
            _logger.LogWarning("⚠️ Disconnected from MQTT broker.");
            return Task.CompletedTask;
        };

        client.ApplicationMessageReceivedAsync += async e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();

            await _signalsService.HandleMqttMessageAsync(topic, payload, stoppingToken);
        };

        await client.ConnectAsync(options, stoppingToken);

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(_mqttOptions.TopicFilter))
            .Build();

        await client.SubscribeAsync(subscribeOptions, stoppingToken);
        _logger.LogInformation("🔔 Subscribed to MQTT topic filter {TopicFilter}", _mqttOptions.TopicFilter);
    }
}
