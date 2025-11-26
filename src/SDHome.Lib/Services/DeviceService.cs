using System.Text;
using System.Text.Json;
using MQTTnet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SDHome.Lib.Services;

public class DeviceService : IDeviceService
{
    private readonly Data.IDeviceRepository _deviceRepository;
    private readonly IMqttClient? _mqttClient;
    private readonly ILogger<DeviceService> _logger;
    private readonly Models.MqttOptions _mqttOptions;

    public DeviceService(
        Data.IDeviceRepository deviceRepository,
        ILogger<DeviceService> logger,
        IOptions<Models.MqttOptions> mqttOptions,
        IMqttClient? mqttClient = null)
    {
        _deviceRepository = deviceRepository;
        _mqttClient = mqttClient;
        _mqttOptions = mqttOptions.Value;
        _logger = logger;
    }

    public async Task<IEnumerable<Models.Device>> GetAllDevicesAsync()
    {
        return await _deviceRepository.GetAllAsync();
    }

    public async Task<Models.Device?> GetDeviceAsync(string deviceId)
    {
        return await _deviceRepository.GetByIdAsync(deviceId);
    }

    public async Task<Models.Device> UpdateDeviceAsync(Models.Device device)
    {
        var existing = await _deviceRepository.GetByIdAsync(device.DeviceId);
        if (existing == null)
        {
            throw new InvalidOperationException($"Device {device.DeviceId} not found");
        }

        return await _deviceRepository.UpdateAsync(device);
    }

    public async Task<IEnumerable<Models.Device>> GetDevicesByRoomAsync(string room)
    {
        return await _deviceRepository.GetByRoomAsync(room);
    }

    public async Task<IEnumerable<Models.Device>> GetDevicesByTypeAsync(Models.DeviceType deviceType)
    {
        return await _deviceRepository.GetByDeviceTypeAsync(deviceType);
    }

    public async Task SyncDevicesFromZigbee2MqttAsync()
    {
        if (!_mqttOptions.Enabled)
        {
            _logger.LogWarning("MQTT is disabled in configuration. Cannot sync devices from Zigbee2MQTT.");
            return;
        }

        if (_mqttClient == null)
        {
            _logger.LogError("MQTT client is not available. Cannot sync devices from Zigbee2MQTT.");
            throw new InvalidOperationException("MQTT client is not configured");
        }

        try
        {
            _logger.LogInformation("Starting device sync from Zigbee2MQTT");

            // Ensure MQTT client is connected
            if (!_mqttClient.IsConnected)
            {
                var options = new MqttClientOptionsBuilder()
                    .WithClientId("SDHomeDeviceSync-" + Guid.NewGuid())
                    .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                    .WithCleanSession()
                    .Build();

                await _mqttClient.ConnectAsync(options);
                _logger.LogInformation("Connected to MQTT broker for device sync");
            }

            // Subscribe to the bridge/devices topic
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter("zigbee2mqtt/bridge/devices")
                .Build();

            var deviceListReceived = new TaskCompletionSource<List<Models.Zigbee2MqttDevice>>();

            // Set up message handler
            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                if (e.ApplicationMessage.Topic == "zigbee2mqtt/bridge/devices")
                {
                    try
                    {
                        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                        var devices = JsonSerializer.Deserialize<List<Models.Zigbee2MqttDevice>>(payload);
                        
                        if (devices != null)
                        {
                            deviceListReceived.TrySetResult(devices);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing Zigbee2MQTT device list");
                        deviceListReceived.TrySetException(ex);
                    }
                }
                
                await Task.CompletedTask;
            };

            await _mqttClient.SubscribeAsync(subscribeOptions);

            // Request device list by publishing to bridge/request/devices
            var requestMessage = new MqttApplicationMessageBuilder()
                .WithTopic("zigbee2mqtt/bridge/request/devices")
                .WithPayload("{}")
                .Build();

            await _mqttClient.PublishAsync(requestMessage);

            // Wait for response with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var completedTask = await Task.WhenAny(deviceListReceived.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Timeout waiting for Zigbee2MQTT device list");
                return;
            }

            var zigbeeDevices = await deviceListReceived.Task;
            _logger.LogInformation("Received {Count} devices from Zigbee2MQTT", zigbeeDevices.Count);

            // Process each device
            foreach (var zigbeeDevice in zigbeeDevices)
            {
                try
                {
                    var device = MapZigbeeDeviceToDevice(zigbeeDevice);
                    
                    var existing = await _deviceRepository.GetByIdAsync(device.DeviceId);
                    if (existing == null)
                    {
                        device.CreatedAt = DateTime.UtcNow;
                        device.UpdatedAt = DateTime.UtcNow;
                        await _deviceRepository.CreateAsync(device);
                        _logger.LogInformation("Created new device: {DeviceId}", device.DeviceId);
                    }
                    else
                    {
                        // Update only Zigbee2MQTT fields, preserve user settings (room, device type)
                        existing.FriendlyName = device.FriendlyName;
                        existing.IeeeAddress = device.IeeeAddress;
                        existing.ModelId = device.ModelId;
                        existing.Manufacturer = device.Manufacturer;
                        existing.Description = device.Description;
                        existing.Capabilities = device.Capabilities;
                        existing.IsAvailable = device.IsAvailable;
                        existing.LastSeen = DateTime.UtcNow;
                        
                        await _deviceRepository.UpdateAsync(existing);
                        _logger.LogInformation("Updated device: {DeviceId}", device.DeviceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing device {FriendlyName}", zigbeeDevice.friendly_name);
                }
            }

            _logger.LogInformation("Device sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing devices from Zigbee2MQTT");
            throw;
        }
    }

    private static Models.Device MapZigbeeDeviceToDevice(Models.Zigbee2MqttDevice zigbeeDevice)
    {
        var device = new Models.Device
        {
            DeviceId = zigbeeDevice.friendly_name,
            FriendlyName = zigbeeDevice.friendly_name,
            IeeeAddress = zigbeeDevice.ieee_address,
            ModelId = zigbeeDevice.model_id,
            Manufacturer = zigbeeDevice.manufacturer ?? zigbeeDevice.definition?.vendor,
            Description = zigbeeDevice.description ?? zigbeeDevice.definition?.description,
            PowerSource = zigbeeDevice.power_source == "Battery" ? false : true,
            IsAvailable = !zigbeeDevice.disabled,
            LastSeen = DateTime.UtcNow
        };

        // Extract capabilities from exposes
        if (zigbeeDevice.definition?.exposes != null)
        {
            device.Capabilities = zigbeeDevice.definition.exposes
                .Where(e => !string.IsNullOrEmpty(e.property))
                .Select(e => e.property!)
                .Distinct()
                .ToList();

            // Try to infer device type from capabilities
            device.DeviceType = InferDeviceType(device.Capabilities);
        }

        return device;
    }

    private static Models.DeviceType InferDeviceType(List<string> capabilities)
    {
        if (capabilities.Contains("state") && (capabilities.Contains("brightness") || capabilities.Contains("color")))
            return Models.DeviceType.Light;
        
        if (capabilities.Contains("state") && !capabilities.Contains("brightness"))
            return Models.DeviceType.Switch;
        
        if (capabilities.Contains("temperature") || capabilities.Contains("humidity") || capabilities.Contains("occupancy"))
            return Models.DeviceType.Sensor;
        
        if (capabilities.Contains("local_temperature") || capabilities.Contains("system_mode"))
            return Models.DeviceType.Climate;
        
        if (capabilities.Contains("lock_state"))
            return Models.DeviceType.Lock;
        
        if (capabilities.Contains("position"))
            return Models.DeviceType.Cover;

        return Models.DeviceType.Other;
    }
}
