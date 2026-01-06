using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using SDHome.Lib.Data;
using SDHome.Lib.Data.Entities;
using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public class DeviceService(
    SignalsDbContext db,
    ILogger<DeviceService> logger,
    IOptions<MqttOptions> mqttOptions,
    IMqttPublisher? mqttPublisher = null,
    IMqttClient? mqttClient = null) : IDeviceService
{
    private readonly MqttOptions _mqttOptions = mqttOptions.Value;

    public async Task<IEnumerable<Device>> GetAllDevicesAsync()
    {
        return await db.Devices
            .AsNoTracking()
            .Include(d => d.Zone)
            .OrderBy(d => d.FriendlyName)
            .Select(d => d.ToModel())
            .ToListAsync();
    }

    public async Task<Device?> GetDeviceAsync(string deviceId)
    {
        var entity = await db.Devices
            .AsNoTracking()
            .Include(d => d.Zone)
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        
        return entity?.ToModel();
    }

    public async Task<Device> UpdateDeviceAsync(Device device)
    {
        var entity = await db.Devices.FindAsync(device.DeviceId)
            ?? throw new InvalidOperationException($"Device {device.DeviceId} not found");

        entity.DisplayName = device.DisplayName;
        entity.IeeeAddress = device.IeeeAddress;
        entity.ModelId = device.ModelId;
        entity.Manufacturer = device.Manufacturer;
        entity.Description = device.Description;
        entity.PowerSource = device.PowerSource;
        entity.DeviceType = device.DeviceType?.ToString();
        entity.Room = device.Room;
        entity.ZoneId = device.ZoneId;
        entity.Capabilities = JsonSerializer.Serialize(device.Capabilities);
        entity.Attributes = JsonSerializer.Serialize(device.Attributes);
        entity.LastSeen = device.LastSeen;
        entity.IsAvailable = device.IsAvailable;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return entity.ToModel();
    }

    public async Task<Device> RenameDeviceAsync(string currentName, string newName)
    {
        if (string.IsNullOrWhiteSpace(currentName))
            throw new ArgumentException("Current name cannot be empty", nameof(currentName));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("New name cannot be empty", nameof(newName));
        if (currentName == newName)
            throw new ArgumentException("New name must be different from current name");

        var entity = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == currentName || d.FriendlyName == currentName)
            ?? throw new InvalidOperationException($"Device '{currentName}' not found");

        if (!_mqttOptions.Enabled)
            throw new InvalidOperationException("MQTT is disabled in configuration");

        // Create a dedicated MQTT client for this operation
        var factory = new MqttClientFactory();
        using var renameClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId("SDHomeRename-" + Guid.NewGuid().ToString("N")[..8])
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        logger.LogInformation("Connecting to MQTT broker to rename device '{CurrentName}' to '{NewName}'", currentName, newName);

        var connectResult = await renameClient.ConnectAsync(options);
        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to connect to MQTT broker: {connectResult.ResultCode}");
        }

        try
        {
            // Zigbee2MQTT expects: {"from": "old_name", "to": "new_name"}
            var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
            var renameTopic = $"{baseTopic}/bridge/request/device/rename";
            var payload = JsonSerializer.Serialize(new { from = currentName, to = newName });

            logger.LogInformation("Publishing rename request to {Topic}: {Payload}", renameTopic, payload);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(renameTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await renameClient.PublishAsync(message);

            logger.LogInformation("Rename request sent successfully");

            // Update local database
            // Since DeviceId is the primary key, we need to delete and recreate
            var oldDeviceId = entity.DeviceId;
            
            // Create new entity with all the same properties but new ID
            var newEntity = new Data.Entities.DeviceEntity
            {
                DeviceId = newName,
                FriendlyName = newName,
                DisplayName = entity.DisplayName,
                IeeeAddress = entity.IeeeAddress,
                ModelId = entity.ModelId,
                Manufacturer = entity.Manufacturer,
                Description = entity.Description,
                PowerSource = entity.PowerSource,
                DeviceType = entity.DeviceType,
                Room = entity.Room,
                ZoneId = entity.ZoneId,
                Capabilities = entity.Capabilities,
                Attributes = entity.Attributes,
                LastSeen = entity.LastSeen,
                IsAvailable = entity.IsAvailable,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            };

            // Remove the old entity first (don't modify it!)
            db.Devices.Remove(entity);
            await db.SaveChangesAsync();

            // Now add the new entity with the new DeviceId
            db.Devices.Add(newEntity);
            await db.SaveChangesAsync();

            logger.LogInformation("Device renamed from '{OldName}' to '{NewName}' in database", oldDeviceId, newName);

            return newEntity.ToModel();
        }
        finally
        {
            await renameClient.DisconnectAsync();
        }
    }

    public async Task<IEnumerable<Device>> GetDevicesByRoomAsync(string room)
    {
        return await db.Devices
            .AsNoTracking()
            .Where(d => d.Room == room)
            .OrderBy(d => d.FriendlyName)
            .Select(d => d.ToModel())
            .ToListAsync();
    }

    public async Task<IEnumerable<Device>> GetDevicesByTypeAsync(DeviceType deviceType)
    {
        var deviceTypeString = deviceType.ToString();
        return await db.Devices
            .AsNoTracking()
            .Where(d => d.DeviceType == deviceTypeString)
            .OrderBy(d => d.FriendlyName)
            .Select(d => d.ToModel())
            .ToListAsync();
    }

    public async Task SyncDevicesFromZigbee2MqttAsync()
    {
        if (!_mqttOptions.Enabled)
        {
            logger.LogWarning("MQTT is disabled in configuration. Cannot sync devices from Zigbee2MQTT.");
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        if (mqttClient == null)
        {
            logger.LogError("MQTT client is not available. Cannot sync devices from Zigbee2MQTT.");
            throw new InvalidOperationException("MQTT client is not configured");
        }

        try
        {
            logger.LogInformation("Starting device sync from Zigbee2MQTT at {Host}:{Port}", _mqttOptions.Host, _mqttOptions.Port);

            var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
            var devicesTopic = $"{baseTopic}/bridge/devices";
            var bridgeWildcard = $"{baseTopic}/bridge/#";

            var deviceListReceived = new TaskCompletionSource<List<Zigbee2MqttDevice>>();

            // Set up message handler BEFORE connecting to catch retained messages
            mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                logger.LogDebug("Received message on topic: {Topic}", e.ApplicationMessage.Topic);
                
                if (e.ApplicationMessage.Topic == devicesTopic)
                {
                    try
                    {
                        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                        logger.LogDebug("Device list payload length: {Length}", payload.Length);
                        
                        var devices = JsonSerializer.Deserialize<List<Zigbee2MqttDevice>>(payload);
                        
                        if (devices != null)
                        {
                            logger.LogInformation("Parsed {Count} devices from Zigbee2MQTT", devices.Count);
                            deviceListReceived.TrySetResult(devices);
                        }
                        else
                        {
                            logger.LogWarning("Device list was null after deserialization");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error parsing Zigbee2MQTT device list");
                        deviceListReceived.TrySetException(ex);
                    }
                }
                
                await Task.CompletedTask;
            };

            // Connect if not already connected
            if (!mqttClient.IsConnected)
            {
                var options = new MqttClientOptionsBuilder()
                    .WithClientId("SDHomeDeviceSync-" + Guid.NewGuid())
                    .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                    .WithCleanSession(false)  // Don't use clean session to receive retained messages
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .Build();

                logger.LogInformation("Connecting to MQTT broker at {Host}:{Port}...", _mqttOptions.Host, _mqttOptions.Port);
                
                try
                {
                    var connectResult = await mqttClient.ConnectAsync(options);
                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        logger.LogError("Failed to connect to MQTT broker. Result: {ResultCode}", connectResult.ResultCode);
                        throw new InvalidOperationException($"Failed to connect to MQTT broker: {connectResult.ResultCode}");
                    }
                    logger.LogInformation("Connected to MQTT broker for device sync");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception connecting to MQTT broker at {Host}:{Port}", _mqttOptions.Host, _mqttOptions.Port);
                    throw new InvalidOperationException($"Cannot connect to MQTT broker at {_mqttOptions.Host}:{_mqttOptions.Port}: {ex.Message}", ex);
                }
            }
            else
            {
                logger.LogInformation("MQTT client already connected");
            }

            // Subscribe to devices topic AND bridge wildcard for debugging
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(devicesTopic)
                .WithTopicFilter(bridgeWildcard)  // Also subscribe to all bridge topics for debugging
                .Build();

            logger.LogInformation("Subscribing to {BridgeWildcard} ...", bridgeWildcard);
            var subResult = await mqttClient.SubscribeAsync(subscribeOptions);
            foreach (var item in subResult.Items)
            {
                logger.LogInformation("Subscribed to {Topic} with result {ResultCode}", item.TopicFilter.Topic, item.ResultCode);
            }

            // Wait for the retained message or timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var completedTask = await Task.WhenAny(deviceListReceived.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                // Log that we're disconnecting
                logger.LogWarning("Timeout waiting for Zigbee2MQTT device list after 10 seconds. Check if {DevicesTopic} topic has a retained message.", devicesTopic);
                await mqttClient.DisconnectAsync();
                throw new InvalidOperationException($"Timeout waiting for device list from Zigbee2MQTT. Ensure Zigbee2MQTT is running and the device list is published on {devicesTopic}.");
            }

            var zigbeeDevices = await deviceListReceived.Task;
            
            // Filter out the coordinator (it's not a real device)
            zigbeeDevices = zigbeeDevices.Where(d => d.type != "Coordinator").ToList();
            
            logger.LogInformation("Processing {Count} devices from Zigbee2MQTT (excluding coordinator)", zigbeeDevices.Count);

            foreach (var zigbeeDevice in zigbeeDevices)
            {
                try
                {
                    var device = MapZigbeeDeviceToDevice(zigbeeDevice);
                    
                    var existing = await db.Devices.FindAsync(device.DeviceId);
                    if (existing == null)
                    {
                        var entity = DeviceEntity.FromModel(device);
                        entity.CreatedAt = DateTime.UtcNow;
                        entity.UpdatedAt = DateTime.UtcNow;
                        db.Devices.Add(entity);
                        logger.LogInformation("Created new device: {DeviceId}", device.DeviceId);
                    }
                    else
                    {
                        existing.FriendlyName = device.FriendlyName;
                        existing.IeeeAddress = device.IeeeAddress;
                        existing.ModelId = device.ModelId;
                        existing.Model = device.Model;
                        existing.Manufacturer = device.Manufacturer;
                        existing.Description = device.Description;
                        existing.Capabilities = JsonSerializer.Serialize(device.Capabilities);
                        existing.IsAvailable = device.IsAvailable;
                        existing.LastSeen = DateTime.UtcNow;
                        existing.UpdatedAt = DateTime.UtcNow;
                        logger.LogInformation("Updated device: {DeviceId}", device.DeviceId);
                    }

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing device {FriendlyName}", zigbeeDevice.friendly_name);
                }
            }

            // Fetch state for all devices to populate linkQuality values
            logger.LogInformation("Fetching state for {Count} devices to populate link quality...", zigbeeDevices.Count);
            var stateTasks = zigbeeDevices.Select(async d =>
            {
                try
                {
                    await GetDeviceStateAsync(d.friendly_name);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not fetch state for {DeviceId}", d.friendly_name);
                }
            });
            
            // Run with limited parallelism to avoid overwhelming MQTT broker
            await Task.WhenAll(stateTasks);

            logger.LogInformation("Device sync completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing devices from Zigbee2MQTT");
            throw;
        }
    }

    public async Task<string> SyncDevicesWithProgressAsync(IRealtimeEventBroadcaster broadcaster)
    {
        var syncId = Guid.NewGuid().ToString("N")[..8];
        var discoveredDevices = new List<DeviceSyncDevice>();

        async Task BroadcastProgress(DeviceSyncStatus status, string message, DeviceSyncDevice? currentDevice = null, string? error = null, int processed = 0, int total = 0)
        {
            var progress = new DeviceSyncProgress
            {
                SyncId = syncId,
                Status = status,
                Message = message,
                DevicesFound = discoveredDevices.Count,
                DevicesProcessed = processed,
                DevicesTotal = total,
                CurrentDevice = currentDevice,
                DiscoveredDevices = discoveredDevices.ToList(),
                Error = error
            };
            await broadcaster.BroadcastDeviceSyncProgressAsync(progress);
        }

        if (!_mqttOptions.Enabled)
        {
            await BroadcastProgress(DeviceSyncStatus.Failed, "MQTT is disabled", error: "MQTT is disabled in configuration");
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        if (mqttClient == null)
        {
            await BroadcastProgress(DeviceSyncStatus.Failed, "MQTT client not available", error: "MQTT client is not configured");
            throw new InvalidOperationException("MQTT client is not configured");
        }

        try
        {
            await BroadcastProgress(DeviceSyncStatus.Started, "Starting device sync...");

            var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
            var devicesTopic = $"{baseTopic}/bridge/devices";
            var bridgeWildcard = $"{baseTopic}/bridge/#";

            var deviceListReceived = new TaskCompletionSource<List<Zigbee2MqttDevice>>();

            mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                if (e.ApplicationMessage.Topic == devicesTopic)
                {
                    try
                    {
                        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                        var devices = JsonSerializer.Deserialize<List<Zigbee2MqttDevice>>(payload);
                        if (devices != null)
                        {
                            await BroadcastProgress(DeviceSyncStatus.DeviceReceived, $"Received {devices.Count} devices from Zigbee2MQTT");
                            deviceListReceived.TrySetResult(devices);
                        }
                    }
                    catch (Exception ex)
                    {
                        deviceListReceived.TrySetException(ex);
                    }
                }
                await Task.CompletedTask;
            };

            await BroadcastProgress(DeviceSyncStatus.Connecting, $"Connecting to MQTT broker at {_mqttOptions.Host}:{_mqttOptions.Port}...");

            if (!mqttClient.IsConnected)
            {
                var options = new MqttClientOptionsBuilder()
                    .WithClientId("SDHomeDeviceSync-" + Guid.NewGuid())
                    .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                    .WithCleanSession(false)
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .Build();

                var connectResult = await mqttClient.ConnectAsync(options);
                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    await BroadcastProgress(DeviceSyncStatus.Failed, "Connection failed", error: $"Failed to connect: {connectResult.ResultCode}");
                    throw new InvalidOperationException($"Failed to connect to MQTT broker: {connectResult.ResultCode}");
                }
            }

            await BroadcastProgress(DeviceSyncStatus.Subscribing, $"Subscribing to {devicesTopic}...");

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(devicesTopic)
                .WithTopicFilter(bridgeWildcard)
                .Build();

            await mqttClient.SubscribeAsync(subscribeOptions);

            await BroadcastProgress(DeviceSyncStatus.WaitingForDevices, "Waiting for device list from Zigbee2MQTT...");

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var completedTask = await Task.WhenAny(deviceListReceived.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                await mqttClient.DisconnectAsync();
                await BroadcastProgress(DeviceSyncStatus.Failed, "Timeout waiting for devices", error: $"No device list received on {devicesTopic}. Ensure Zigbee2MQTT is running.");
                throw new InvalidOperationException($"Timeout waiting for device list from Zigbee2MQTT.");
            }

            var zigbeeDevices = await deviceListReceived.Task;
            zigbeeDevices = zigbeeDevices.Where(d => d.type != "Coordinator").ToList();
            var totalDevices = zigbeeDevices.Count;

            await BroadcastProgress(DeviceSyncStatus.Processing, $"Processing {totalDevices} devices...", total: totalDevices);

            int processedCount = 0;
            foreach (var zigbeeDevice in zigbeeDevices)
            {
                try
                {
                    var device = MapZigbeeDeviceToDevice(zigbeeDevice);
                    var existing = await db.Devices.FindAsync(device.DeviceId);
                    var isNew = existing == null;

                    var syncDevice = new DeviceSyncDevice
                    {
                        DeviceId = device.DeviceId,
                        FriendlyName = device.FriendlyName,
                        Manufacturer = device.Manufacturer,
                        Model = device.ModelId,
                        DeviceType = device.DeviceType?.ToString(),
                        IsNew = isNew
                    };
                    discoveredDevices.Add(syncDevice);

                    if (isNew)
                    {
                        var entity = DeviceEntity.FromModel(device);
                        entity.CreatedAt = DateTime.UtcNow;
                        entity.UpdatedAt = DateTime.UtcNow;
                        db.Devices.Add(entity);
                    }
                    else
                    {
                        existing!.FriendlyName = device.FriendlyName;
                        existing.IeeeAddress = device.IeeeAddress;
                        existing.ModelId = device.ModelId;
                        existing.Model = device.Model;
                        existing.Manufacturer = device.Manufacturer;
                        existing.Description = device.Description;
                        existing.Capabilities = JsonSerializer.Serialize(device.Capabilities);
                        existing.IsAvailable = device.IsAvailable;
                        existing.LastSeen = DateTime.UtcNow;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync();
                    processedCount++;

                    await BroadcastProgress(
                        DeviceSyncStatus.DeviceProcessed,
                        $"Processed {device.FriendlyName}",
                        currentDevice: syncDevice,
                        processed: processedCount,
                        total: totalDevices
                    );

                    // Small delay to allow UI to update smoothly
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing device {FriendlyName}", zigbeeDevice.friendly_name);
                }
            }

            // Detect and remove devices that are no longer in Zigbee2MQTT
            var zigbeeDeviceIds = zigbeeDevices.Select(d => d.friendly_name).ToHashSet();
            var existingDevices = await db.Devices.ToListAsync();
            var removedDevices = existingDevices.Where(d => !zigbeeDeviceIds.Contains(d.DeviceId)).ToList();

            foreach (var removedDevice in removedDevices)
            {
                var removedSyncDevice = new DeviceSyncDevice
                {
                    DeviceId = removedDevice.DeviceId,
                    FriendlyName = removedDevice.FriendlyName ?? removedDevice.DeviceId,
                    Manufacturer = removedDevice.Manufacturer,
                    Model = removedDevice.ModelId,
                    DeviceType = removedDevice.DeviceType,
                    IsNew = false,
                    IsRemoved = true
                };
                discoveredDevices.Add(removedSyncDevice);
                
                db.Devices.Remove(removedDevice);
                logger.LogInformation("Removed device no longer in Zigbee2MQTT: {DeviceId}", removedDevice.DeviceId);
            }

            if (removedDevices.Count > 0)
            {
                await db.SaveChangesAsync();
            }

            await mqttClient.DisconnectAsync();

            var newCount = discoveredDevices.Count(d => d.IsNew && !d.IsRemoved);
            var updatedCount = discoveredDevices.Count(d => !d.IsNew && !d.IsRemoved);
            var removedCount = discoveredDevices.Count(d => d.IsRemoved);
            
            var message = $"Sync complete! {newCount} new, {updatedCount} updated";
            if (removedCount > 0)
            {
                message += $", {removedCount} removed";
            }
            
            await BroadcastProgress(
                DeviceSyncStatus.Completed,
                message,
                processed: processedCount,
                total: totalDevices
            );

            return syncId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing devices from Zigbee2MQTT");
            await BroadcastProgress(DeviceSyncStatus.Failed, "Sync failed", error: ex.Message);
            throw;
        }
    }

    private static Device MapZigbeeDeviceToDevice(Zigbee2MqttDevice zigbeeDevice)
    {
        var device = new Device
        {
            DeviceId = zigbeeDevice.friendly_name,
            FriendlyName = zigbeeDevice.friendly_name,
            IeeeAddress = zigbeeDevice.ieee_address,
            ModelId = zigbeeDevice.model_id,
            Model = zigbeeDevice.definition?.model,
            Manufacturer = zigbeeDevice.manufacturer ?? zigbeeDevice.definition?.vendor,
            Description = zigbeeDevice.description ?? zigbeeDevice.definition?.description,
            PowerSource = zigbeeDevice.power_source != "Battery",
            IsAvailable = !zigbeeDevice.disabled,
            LastSeen = DateTime.UtcNow
        };

        if (zigbeeDevice.definition?.exposes != null)
        {
            device.Capabilities = ExtractAllCapabilities(zigbeeDevice.definition.exposes);
            device.DeviceType = InferDeviceType(device.Capabilities);
        }

        return device;
    }
    
    /// <summary>
    /// Recursively extract all capability properties from Zigbee exposes structure.
    /// This handles both flat properties and nested features within composite types (light, switch, etc.)
    /// </summary>
    private static List<string> ExtractAllCapabilities(List<Expose> exposes)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        void ExtractRecursive(List<Expose>? items)
        {
            if (items == null) return;
            
            foreach (var expose in items)
            {
                // Add the property if it exists
                if (!string.IsNullOrEmpty(expose.property))
                {
                    capabilities.Add(expose.property);
                }
                
                // Recursively process nested features (for composite types like light, switch, climate)
                if (expose.features != null && expose.features.Count > 0)
                {
                    ExtractRecursive(expose.features);
                }
            }
        }
        
        ExtractRecursive(exposes);
        return capabilities.ToList();
    }

    private static DeviceType InferDeviceType(List<string> capabilities)
    {
        if (capabilities.Contains("state") && (capabilities.Contains("brightness") || capabilities.Contains("color")))
            return DeviceType.Light;
        
        if (capabilities.Contains("state") && !capabilities.Contains("brightness"))
            return DeviceType.Switch;
        
        if (capabilities.Contains("temperature") || capabilities.Contains("humidity") || capabilities.Contains("occupancy"))
            return DeviceType.Sensor;
        
        if (capabilities.Contains("local_temperature") || capabilities.Contains("system_mode"))
            return DeviceType.Climate;
        
        if (capabilities.Contains("lock_state"))
            return DeviceType.Lock;
        
        if (capabilities.Contains("position"))
            return DeviceType.Cover;

        return DeviceType.Other;
    }

    #region Pairing Mode

    private static string? _activePairingId;
    private static CancellationTokenSource? _pairingCts;
    private static readonly List<DevicePairingDevice> _discoveredDevices = [];
    private static IMqttClient? _pairingMqttClient;

    public async Task<string> StartPairingModeAsync(int durationSeconds, IRealtimeEventBroadcaster broadcaster, CancellationToken cancellationToken = default)
    {
        if (!_mqttOptions.Enabled)
        {
            logger.LogWarning("MQTT is disabled in configuration. Cannot start pairing mode.");
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        // Stop any existing pairing session
        if (_activePairingId != null)
        {
            await StopPairingModeAsync(_activePairingId, broadcaster);
        }

        var pairingId = Guid.NewGuid().ToString("N")[..8];
        _activePairingId = pairingId;
        _pairingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _discoveredDevices.Clear();

        // Clamp duration to valid range (1-254 seconds)
        durationSeconds = Math.Clamp(durationSeconds, 1, 254);

        logger.LogInformation("Starting pairing mode for {Duration} seconds, pairingId: {PairingId}", durationSeconds, pairingId);

        await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
        {
            PairingId = pairingId,
            Status = DevicePairingStatus.Starting,
            Message = "Connecting to MQTT broker...",
            RemainingSeconds = durationSeconds,
            TotalDuration = durationSeconds,
            IsActive = false
        });

        try
        {
            // Create and connect a dedicated MQTT client for pairing
            var factory = new MqttClientFactory();
            _pairingMqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithClientId("SDHomePairing-" + pairingId)
                .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                .WithCleanSession()
                .WithTimeout(TimeSpan.FromSeconds(10))
                .Build();

            logger.LogInformation("Connecting pairing MQTT client to {Host}:{Port}", _mqttOptions.Host, _mqttOptions.Port);
            
            var connectResult = await _pairingMqttClient.ConnectAsync(options, _pairingCts.Token);
            if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
            {
                throw new InvalidOperationException($"Failed to connect to MQTT broker: {connectResult.ResultCode}");
            }

            logger.LogInformation("Pairing MQTT client connected successfully");

            await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
            {
                PairingId = pairingId,
                Status = DevicePairingStatus.Starting,
                Message = "Enabling pairing mode...",
                RemainingSeconds = durationSeconds,
                TotalDuration = durationSeconds,
                IsActive = false
            });

            // Send permit_join command to Zigbee2MQTT
            // Zigbee2MQTT expects: {"value": true, "time": <seconds>}
            // Using explicit JSON to ensure correct format
            var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
            var payload = $"{{\"value\":true,\"time\":{durationSeconds}}}";
            var permitJoinTopic = $"{baseTopic}/bridge/request/permit_join";
            
            logger.LogInformation("Publishing permit_join to {Topic} with payload: {Payload}", permitJoinTopic, payload);
            
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(permitJoinTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _pairingMqttClient.PublishAsync(message, _pairingCts.Token);

            logger.LogInformation("Pairing mode enabled via MQTT - permit_join published successfully");

            await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
            {
                PairingId = pairingId,
                Status = DevicePairingStatus.Active,
                Message = "Pairing mode active. Put your device in pairing mode now!",
                RemainingSeconds = durationSeconds,
                TotalDuration = durationSeconds,
                IsActive = true
            });

            // Start countdown timer in background
            _ = RunPairingCountdownAsync(pairingId, durationSeconds, broadcaster, _pairingCts.Token);

            return pairingId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start pairing mode");
            _activePairingId = null;
            _pairingCts?.Dispose();
            _pairingCts = null;

            await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
            {
                PairingId = pairingId,
                Status = DevicePairingStatus.Failed,
                Message = "Failed to start pairing mode",
                Error = ex.Message,
                IsActive = false
            });

            throw;
        }
    }

    public async Task StopPairingModeAsync(string pairingId, IRealtimeEventBroadcaster broadcaster)
    {
        if (_activePairingId != pairingId)
        {
            logger.LogWarning("Attempted to stop pairing session {PairingId} but active session is {ActiveId}", 
                pairingId, _activePairingId);
            return;
        }

        logger.LogInformation("Stopping pairing mode for session {PairingId}", pairingId);

        await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
        {
            PairingId = pairingId,
            Status = DevicePairingStatus.Stopping,
            Message = "Stopping pairing mode...",
            IsActive = false,
            DiscoveredDevices = [.. _discoveredDevices]
        });

        // Cancel the countdown
        _pairingCts?.Cancel();

        try
        {
            if (_pairingMqttClient?.IsConnected == true)
            {
                // Send permit_join false to Zigbee2MQTT
                var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
                var payload = "{\"value\":false}";
                var permitJoinTopic = $"{baseTopic}/bridge/request/permit_join";
                
                logger.LogInformation("Publishing permit_join disable to {Topic}", permitJoinTopic);
                
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(permitJoinTopic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _pairingMqttClient.PublishAsync(message);
                
                // Disconnect the pairing client
                await _pairingMqttClient.DisconnectAsync();
                logger.LogInformation("Pairing MQTT client disconnected");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send permit_join false command");
        }
        finally
        {
            _pairingMqttClient?.Dispose();
            _pairingMqttClient = null;
        }

        await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
        {
            PairingId = pairingId,
            Status = DevicePairingStatus.Ended,
            Message = $"Pairing ended. {_discoveredDevices.Count} device(s) discovered.",
            IsActive = false,
            DiscoveredDevices = [.. _discoveredDevices]
        });

        _activePairingId = null;
        _pairingCts?.Dispose();
        _pairingCts = null;
    }

    public Task<bool> IsPairingActiveAsync()
    {
        return Task.FromResult(_activePairingId != null);
    }

    private async Task RunPairingCountdownAsync(string pairingId, int durationSeconds, IRealtimeEventBroadcaster broadcaster, CancellationToken cancellationToken)
    {
        var remaining = durationSeconds;

        try
        {
            while (remaining > 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
                remaining--;

                // Send countdown tick every second
                await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
                {
                    PairingId = pairingId,
                    Status = DevicePairingStatus.CountdownTick,
                    Message = $"Pairing mode active. {remaining}s remaining...",
                    RemainingSeconds = remaining,
                    TotalDuration = durationSeconds,
                    IsActive = true,
                    DiscoveredDevices = [.. _discoveredDevices]
                });
            }

            // Pairing time expired
            if (!cancellationToken.IsCancellationRequested)
            {
                await StopPairingModeAsync(pairingId, broadcaster);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Pairing countdown cancelled for session {PairingId}", pairingId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in pairing countdown for session {PairingId}", pairingId);
        }
    }

    /// <summary>
    /// Called by MQTT worker when a device join event is received
    /// </summary>
    public static async Task HandleDeviceJoinEventAsync(
        string ieeeAddress, 
        string? friendlyName, 
        IRealtimeEventBroadcaster broadcaster,
        ILogger logger)
    {
        if (_activePairingId == null) return;

        logger.LogInformation("Device joining: {IeeeAddress} ({FriendlyName})", ieeeAddress, friendlyName);

        var device = new DevicePairingDevice
        {
            IeeeAddress = ieeeAddress,
            FriendlyName = friendlyName,
            Status = DevicePairingDeviceStatus.Discovered
        };

        _discoveredDevices.Add(device);

        await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
        {
            PairingId = _activePairingId,
            Status = DevicePairingStatus.Interviewing,
            Message = $"New device discovered: {friendlyName ?? ieeeAddress}",
            IsActive = true,
            CurrentDevice = device,
            DiscoveredDevices = [.. _discoveredDevices]
        });
    }

    /// <summary>
    /// Called by MQTT worker when a device interview event is received
    /// </summary>
    public static async Task HandleDeviceInterviewEventAsync(
        string ieeeAddress,
        string? friendlyName,
        string status,
        IRealtimeEventBroadcaster broadcaster,
        ILogger logger)
    {
        if (_activePairingId == null) return;

        logger.LogInformation("Device interview {Status}: {IeeeAddress} ({FriendlyName})", status, ieeeAddress, friendlyName);

        var device = _discoveredDevices.FirstOrDefault(d => d.IeeeAddress == ieeeAddress);
        if (device == null)
        {
            device = new DevicePairingDevice
            {
                IeeeAddress = ieeeAddress,
                FriendlyName = friendlyName
            };
            _discoveredDevices.Add(device);
        }

        device.FriendlyName = friendlyName ?? device.FriendlyName;
        device.Status = status.ToLowerInvariant() switch
        {
            "started" => DevicePairingDeviceStatus.Interviewing,
            "successful" => DevicePairingDeviceStatus.Ready,
            "failed" => DevicePairingDeviceStatus.Failed,
            _ => device.Status
        };

        var pairingStatus = status.ToLowerInvariant() switch
        {
            "started" => DevicePairingStatus.Interviewing,
            "successful" => DevicePairingStatus.DevicePaired,
            "failed" => DevicePairingStatus.Interviewing,
            _ => DevicePairingStatus.Active
        };

        var message = status.ToLowerInvariant() switch
        {
            "started" => $"Interviewing {friendlyName ?? ieeeAddress}...",
            "successful" => $"✅ {friendlyName ?? ieeeAddress} paired successfully!",
            "failed" => $"❌ {friendlyName ?? ieeeAddress} interview failed",
            _ => $"Device: {friendlyName ?? ieeeAddress}"
        };

        await broadcaster.BroadcastDevicePairingProgressAsync(new DevicePairingProgress
        {
            PairingId = _activePairingId,
            Status = pairingStatus,
            Message = message,
            IsActive = true,
            CurrentDevice = device,
            DiscoveredDevices = [.. _discoveredDevices]
        });
    }

    #endregion

    #region Device Definition and State

    public async Task<DeviceDefinition?> GetDeviceDefinitionAsync(string deviceId)
    {
        if (!_mqttOptions.Enabled)
        {
            logger.LogWarning("MQTT is disabled. Cannot fetch device definition.");
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        var device = await GetDeviceAsync(deviceId);
        if (device == null)
        {
            return null;
        }

        // Fetch full device info from Zigbee2MQTT
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId("SDHomeDefinition-" + Guid.NewGuid().ToString("N")[..8])
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithCleanSession(false)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
        var devicesTopic = $"{baseTopic}/bridge/devices";
        var stateTopic = $"{baseTopic}/{deviceId}";
        var getTopic = $"{baseTopic}/{deviceId}/get";

        Zigbee2MqttDevice? zigbeeDevice = null;
        Dictionary<string, object?>? currentState = null;

        var deviceListReceived = new TaskCompletionSource<bool>();
        var stateReceived = new TaskCompletionSource<bool>();

        client.ApplicationMessageReceivedAsync += async e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            if (topic == devicesTopic)
            {
                try
                {
                    var devices = JsonSerializer.Deserialize<List<Zigbee2MqttDevice>>(payload);
                    zigbeeDevice = devices?.FirstOrDefault(d => 
                        d.friendly_name == deviceId || d.ieee_address == deviceId);
                    deviceListReceived.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error parsing device list");
                    deviceListReceived.TrySetResult(false);
                }
            }
            else if (topic == stateTopic)
            {
                try
                {
                    // Parse into JsonElement first to handle mixed types properly
                    using var doc = JsonDocument.Parse(payload);
                    currentState = new Dictionary<string, object?>();
                    
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        currentState[prop.Name] = ParseJsonValue(prop.Value);
                    }
                    stateReceived.TrySetResult(true);
                    logger.LogDebug("Received state for {DeviceId} in definition request", deviceId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error parsing device state");
                    stateReceived.TrySetResult(false);
                }
            }

            await Task.CompletedTask;
        };

        await client.ConnectAsync(options);

        try
        {
            // Subscribe to both topics
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(devicesTopic)
                .WithTopicFilter(stateTopic)
                .Build());

            // Wait for both responses (with timeout)
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var deviceTask = deviceListReceived.Task;
            var stateTask = stateReceived.Task;

            // Wait for device list (required)
            var completedTask = await Task.WhenAny(deviceTask, timeout);
            if (completedTask == timeout)
            {
                logger.LogWarning("Timeout waiting for device list from Zigbee2MQTT");
            }

            // Actively request current state instead of relying on retained messages
            // This is more reliable for getting accurate device state
            // Note: Using empty object {} requests ALL properties
            var requestPayload = "{}";
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(getTopic)
                .WithPayload(requestPayload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(message);
            logger.LogDebug("Sent state request to {Topic} for device definition", getTopic);

            // Wait for state response (with separate timeout)
            await Task.WhenAny(stateTask, Task.Delay(3000));
        }
        finally
        {
            await client.DisconnectAsync();
        }

        // Build the definition
        var definition = new DeviceDefinition
        {
            DeviceId = device.DeviceId,
            FriendlyName = device.FriendlyName,
            DisplayName = device.DisplayName,
            IeeeAddress = device.IeeeAddress,
            ModelId = device.ModelId,
            Manufacturer = device.Manufacturer,
            Description = device.Description ?? zigbeeDevice?.definition?.description,
            DeviceType = device.DeviceType?.ToString(),
            PowerSource = device.PowerSource ? "Mains" : "Battery",
            IsAvailable = device.IsAvailable,
            LastSeen = device.LastSeen,
            ImageUrl = device.ImageUrl,
            CurrentState = currentState ?? new Dictionary<string, object?>()
        };

        // Map exposes to capabilities
        if (zigbeeDevice?.definition?.exposes != null)
        {
            definition.Capabilities = MapExposesToCapabilities(zigbeeDevice.definition.exposes);
        }

        return definition;
    }

    private List<DeviceCapability> MapExposesToCapabilities(List<Expose> exposes, string? parentCategory = null)
    {
        var capabilities = new List<DeviceCapability>();

        foreach (var expose in exposes)
        {
            // Determine the category based on type
            var category = parentCategory ?? expose.type;

            if (expose.features != null && expose.features.Count > 0)
            {
                // Composite type with nested features
                var nestedCapabilities = MapExposesToCapabilities(expose.features, category);
                
                // Add nested features directly if this is a known composite type (light, switch, etc.)
                if (expose.type is "light" or "switch" or "lock" or "climate" or "cover" or "fan")
                {
                    capabilities.AddRange(nestedCapabilities);
                }
                else
                {
                    // Create a composite capability
                    capabilities.Add(new DeviceCapability
                    {
                        Type = expose.type ?? "composite",
                        Name = expose.name ?? expose.type ?? "Unknown",
                        Property = expose.property ?? expose.type ?? "",
                        Description = expose.description,
                        Category = category,
                        ControlType = ControlType.Composite,
                        Features = nestedCapabilities,
                        Access = ParseAccess(expose.access)
                    });
                }
            }
            else if (!string.IsNullOrEmpty(expose.property))
            {
                // Simple property
                capabilities.Add(new DeviceCapability
                {
                    Type = expose.type ?? "unknown",
                    Name = FormatPropertyName(expose.name ?? expose.property),
                    Property = expose.property,
                    Description = expose.description,
                    Unit = expose.unit,
                    Category = category,
                    Access = ParseAccess(expose.access),
                    Values = ParseEnumValues(expose.values),
                    ValueMin = expose.value_min,
                    ValueMax = expose.value_max,
                    ValueStep = expose.value_step,
                    ValueOn = expose.ExtensionData?.TryGetValue("value_on", out var von) == true ? ParseJsonElement(von) : null,
                    ValueOff = expose.ExtensionData?.TryGetValue("value_off", out var voff) == true ? ParseJsonElement(voff) : null,
                    ControlType = DetermineControlType(expose)
                });
            }
        }

        return capabilities;
    }

    private static CapabilityAccess ParseAccess(int? access)
    {
        if (access == null) return new CapabilityAccess { CanRead = true };
        
        return new CapabilityAccess
        {
            CanRead = (access.Value & 1) != 0,
            CanWrite = (access.Value & 2) != 0,
            IsPublished = (access.Value & 4) != 0
        };
    }

    private static List<string>? ParseEnumValues(JsonElement? values)
    {
        if (values == null || values.Value.ValueKind != JsonValueKind.Array)
            return null;

        return values.Value.EnumerateArray()
            .Select(v => v.ToString())
            .ToList();
    }

    private static object? ParseJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.ToString()
        };
    }

    private static string FormatPropertyName(string property)
    {
        // Convert snake_case to Title Case
        return string.Join(" ", property.Split('_')
            .Select(word => char.ToUpper(word[0]) + word[1..].ToLower()));
    }

    private static ControlType DetermineControlType(Expose expose)
    {
        // Check if writable
        var canWrite = expose.access.HasValue && (expose.access.Value & 2) != 0;

        return expose.type switch
        {
            "binary" => canWrite ? ControlType.Toggle : ControlType.ReadOnly,
            "numeric" when expose.value_min != null && expose.value_max != null && canWrite => ControlType.Slider,
            "numeric" when canWrite => ControlType.Number,
            "numeric" => ControlType.ReadOnly,
            "enum" when canWrite => ControlType.Select,
            "enum" => ControlType.ReadOnly,
            "text" when canWrite => ControlType.Text,
            "text" => ControlType.ReadOnly,
            "composite" => ControlType.Composite,
            _ => canWrite ? ControlType.Text : ControlType.ReadOnly
        };
    }

    public async Task SetDeviceStateAsync(string deviceId, Dictionary<string, object> state)
    {
        var totalStart = Stopwatch.GetTimestamp();
        
        if (!_mqttOptions.Enabled)
        {
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
        var setTopic = $"{baseTopic}/{deviceId}/set";
        var payload = JsonSerializer.Serialize(state);

        logger.LogInformation("Setting device state for {DeviceId}: {Payload}", deviceId, payload);

        // Use persistent publisher if available (much faster - no connection overhead)
        if (mqttPublisher != null && mqttPublisher.IsConnected)
        {
            await mqttPublisher.PublishAsync(setTopic, payload);
            var totalTime = Stopwatch.GetElapsedTime(totalStart);
            logger.LogInformation("⏱️ SetDeviceState [{DeviceId}] via Publisher: {Total:F1}ms", 
                deviceId, totalTime.TotalMilliseconds);
            return;
        }

        // Fallback: create ephemeral connection (slower)
        logger.LogWarning("Using fallback MQTT connection (publisher not available)");
        var connectStart = Stopwatch.GetTimestamp();
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId("SDHomeSetState-" + Guid.NewGuid().ToString("N")[..8])
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        await client.ConnectAsync(options);
        var connectTime = Stopwatch.GetElapsedTime(connectStart);

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(setTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            var publishStart = Stopwatch.GetTimestamp();
            await client.PublishAsync(message);
            var publishTime = Stopwatch.GetElapsedTime(publishStart);

            var totalTime = Stopwatch.GetElapsedTime(totalStart);
            logger.LogInformation(
                "⏱️ SetDeviceState [{DeviceId}] (fallback) Total: {Total:F1}ms | Connect: {Connect:F1}ms | Publish: {Publish:F1}ms",
                deviceId, totalTime.TotalMilliseconds, connectTime.TotalMilliseconds, publishTime.TotalMilliseconds);
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    /// <summary>
    /// Actively request current state from a device via Zigbee2MQTT.
    /// This sends a GET request and waits for the device to report its state.
    /// More reliable than waiting for retained messages.
    /// </summary>
    public async Task<Dictionary<string, object?>> GetDeviceStateAsync(string deviceId)
    {
        var totalStart = Stopwatch.GetTimestamp();
        
        if (!_mqttOptions.Enabled)
        {
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
        var getTopic = $"{baseTopic}/{deviceId}/get";
        var stateTopic = $"{baseTopic}/{deviceId}";
        
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId("SDHomeGetState-" + Guid.NewGuid().ToString("N")[..8])
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        Dictionary<string, object?>? currentState = null;
        var stateReceived = new TaskCompletionSource<bool>();

        client.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic == stateTopic)
            {
                try
                {
                    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    // Parse into JsonElement first to handle mixed types properly
                    using var doc = JsonDocument.Parse(payload);
                    currentState = new Dictionary<string, object?>();
                    
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        currentState[prop.Name] = ParseJsonValue(prop.Value);
                    }
                    
                    stateReceived.TrySetResult(true);
                    logger.LogDebug("Received state for {DeviceId}: {State}", deviceId, payload);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error parsing device state response for {DeviceId}", deviceId);
                    stateReceived.TrySetResult(false);
                }
            }
            return Task.CompletedTask;
        };

        await client.ConnectAsync(options);

        try
        {
            // Subscribe to the device's state topic
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(stateTopic)
                .Build());

            // Request all state by sending an empty object
            // Zigbee2MQTT will respond with the full device state
            // Note: Using empty object {} requests ALL properties, not just "state"
            var requestPayload = "{}";
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(getTopic)
                .WithPayload(requestPayload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(message);
            logger.LogDebug("Sent state request to {Topic}", getTopic);

            // Wait for response with timeout
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completedTask = await Task.WhenAny(stateReceived.Task, timeout);

            if (completedTask == timeout)
            {
                logger.LogWarning("Timeout waiting for state response from {DeviceId}", deviceId);
            }
        }
        finally
        {
            await client.DisconnectAsync();
        }

        var totalTime = Stopwatch.GetElapsedTime(totalStart);
        logger.LogInformation("⏱️ GetDeviceState [{DeviceId}]: {Total:F1}ms, received: {Received}", 
            deviceId, totalTime.TotalMilliseconds, currentState != null);

        // Persist linkQuality to the database if received
        if (currentState != null && currentState.TryGetValue("linkquality", out var lqValue))
        {
            int? lq = lqValue switch
            {
                long l => (int)l,
                int i => i,
                double d => (int)d,
                _ => null
            };
            if (lq.HasValue)
            {
                try
                {
                    var device = await db.Devices.FindAsync(deviceId);
                    if (device != null)
                    {
                        device.LinkQuality = lq.Value;
                        device.LastSeen = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to persist linkQuality for {DeviceId}", deviceId);
                }
            }
        }

        return currentState ?? new Dictionary<string, object?>();
    }

    private static object? ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ParseJsonObject(element),
            JsonValueKind.Array => ParseJsonArray(element),
            _ => element.ToString()
        };
    }

    private static Dictionary<string, object?> ParseJsonObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ParseJsonValue(prop.Value);
        }
        return dict;
    }

    private static List<object?> ParseJsonArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ParseJsonValue(item));
        }
        return list;
    }

    #endregion

    #region Network Map

    /// <summary>
    /// Get the Zigbee network map from Zigbee2MQTT
    /// </summary>
    public async Task<ZigbeeNetworkMap> GetNetworkMapAsync()
    {
        if (!_mqttOptions.Enabled)
        {
            throw new InvalidOperationException("MQTT is disabled in configuration");
        }

        var baseTopic = _mqttOptions.BaseTopic.TrimEnd('/');
        var requestTopic = $"{baseTopic}/bridge/request/networkmap";
        var responseTopic = $"{baseTopic}/bridge/response/networkmap";

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithClientId("SDHomeNetworkMap-" + Guid.NewGuid().ToString("N")[..8])
            .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        // Network map generation can take 60+ seconds on large networks
        var timeoutSeconds = 120;
        var tcs = new TaskCompletionSource<ZigbeeNetworkMap>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        cts.Token.Register(() => tcs.TrySetException(new TimeoutException($"Network map request timed out after {timeoutSeconds} seconds. The Zigbee network scan may take longer on large networks.")));

        client.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == responseTopic)
            {
                try
                {
                    var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                    logger.LogDebug("Received network map response ({Length} bytes)", payload.Length);
                    
                    // Parse the outer response envelope
                    var response = JsonSerializer.Deserialize<NetworkMapResponse>(payload, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (response?.Status == "error")
                    {
                        var errorMsg = response.Error ?? "Unknown error from Zigbee2MQTT";
                        logger.LogError("Network map request failed: {Error}", errorMsg);
                        tcs.TrySetException(new InvalidOperationException($"Zigbee2MQTT error: {errorMsg}"));
                        return Task.CompletedTask;
                    }

                    // The 'data' field can be either a string (JSON) or an object
                    // Zigbee2MQTT returns it as a JSON string that needs to be parsed again
                    if (response != null && response.Data.ValueKind != JsonValueKind.Undefined && response.Data.ValueKind != JsonValueKind.Null)
                    {
                        NetworkMapDataWrapper? dataWrapper = null;
                        
                        if (response.Data.ValueKind == JsonValueKind.String)
                        {
                            // Data is a JSON string - parse it
                            var dataString = response.Data.GetString();
                            if (!string.IsNullOrEmpty(dataString))
                            {
                                dataWrapper = JsonSerializer.Deserialize<NetworkMapDataWrapper>(dataString, new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                });
                            }
                        }
                        else if (response.Data.ValueKind == JsonValueKind.Object)
                        {
                            // Data is already an object
                            dataWrapper = JsonSerializer.Deserialize<NetworkMapDataWrapper>(response.Data.GetRawText(), new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                        }

                        if (dataWrapper?.Value != null)
                        {
                            var networkMap = ParseNetworkMap(dataWrapper.Value);
                            tcs.TrySetResult(networkMap);
                        }
                        else
                        {
                            logger.LogWarning("Network map data wrapper has no value. Data kind: {Kind}", response.Data.ValueKind);
                            tcs.TrySetException(new InvalidOperationException("Invalid network map response - no data.value returned"));
                        }
                    }
                    else
                    {
                        logger.LogWarning("Network map response has no data. Response: {Response}", 
                            payload.Length > 1000 ? payload[..1000] + "..." : payload);
                        tcs.TrySetException(new InvalidOperationException("Invalid network map response - no data returned"));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to parse network map response");
                    tcs.TrySetException(ex);
                }
            }
            return Task.CompletedTask;
        };

        logger.LogInformation("Connecting to MQTT broker at {Host}:{Port} to get network map...", 
            _mqttOptions.Host, _mqttOptions.Port);
        
        try
        {
            await client.ConnectAsync(options, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to MQTT broker for network map");
            throw new InvalidOperationException($"Failed to connect to MQTT broker: {ex.Message}", ex);
        }

        await client.SubscribeAsync(responseTopic);
        logger.LogInformation("Subscribed to {Topic}, requesting network map (timeout: {Timeout}s)...", 
            responseTopic, timeoutSeconds);

        // Request the network map - use 'raw' type which returns structured JSON
        // routes=true includes routing information
        var requestPayload = JsonSerializer.Serialize(new { type = "raw", routes = true });
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(requestTopic)
            .WithPayload(requestPayload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await client.PublishAsync(message);
        logger.LogInformation("Network map request sent to {Topic}. Waiting for response (this may take up to {Timeout}s on large networks)...", 
            requestTopic, timeoutSeconds);

        try
        {
            return await tcs.Task;
        }
        finally
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disconnecting MQTT client");
            }
        }
    }

    private ZigbeeNetworkMap ParseNetworkMap(NetworkMapData data)
    {
        var map = new ZigbeeNetworkMap();
        
        // Parse nodes
        foreach (var node in data.Nodes ?? [])
        {
            var zigbeeNode = new ZigbeeNode
            {
                IeeeAddress = node.IeeeAddr ?? "",
                FriendlyName = node.FriendlyName ?? node.IeeeAddr ?? "",
                NetworkAddress = node.NwkAddr ?? 0,
                Type = ParseDeviceType(node.Type),
                Manufacturer = node.Manufacturer,
                Model = node.Definition?.Model,
                ModelId = node.ModelId,
                MainsPowered = node.PowerSource == "Mains (single phase)",
                LinkQuality = node.Lqi,
                LastSeen = node.LastSeen.HasValue 
                    ? DateTimeOffset.FromUnixTimeMilliseconds(node.LastSeen.Value).UtcDateTime 
                    : null
            };
            map.Nodes.Add(zigbeeNode);
        }
        
        // Parse links
        foreach (var link in data.Links ?? [])
        {
            var zigbeeLink = new ZigbeeLink
            {
                SourceIeeeAddress = link.Source.IeeeAddr ?? link.SourceIeeeAddr ?? "",
                TargetIeeeAddress = link.Target.IeeeAddr ?? link.TargetIeeeAddr ?? "",
                LinkQuality = link.Lqi ?? link.Linkquality ?? 0,
                Depth = link.Depth,
                Relationship = link.Relationship?.ToString()
            };
            map.Links.Add(zigbeeLink);
        }

        logger.LogInformation("Parsed network map: {NodeCount} nodes, {LinkCount} links", 
            map.Nodes.Count, map.Links.Count);
        
        return map;
    }

    private static ZigbeeDeviceType ParseDeviceType(string? type)
    {
        return type?.ToLower() switch
        {
            "coordinator" => ZigbeeDeviceType.Coordinator,
            "router" => ZigbeeDeviceType.Router,
            "enddevice" => ZigbeeDeviceType.EndDevice,
            _ => ZigbeeDeviceType.EndDevice
        };
    }

    // Response DTOs for network map
    private class NetworkMapResponse
    {
        public JsonElement Data { get; set; }
        public string? Status { get; set; }
        public string? Error { get; set; }
    }

    private class NetworkMapDataWrapper
    {
        public string? Type { get; set; }
        public NetworkMapData? Value { get; set; }
    }

    private class NetworkMapData
    {
        public List<NetworkMapNode>? Nodes { get; set; }
        public List<NetworkMapLink>? Links { get; set; }
    }

    private class NetworkMapNode
    {
        public string? IeeeAddr { get; set; }
        public string? FriendlyName { get; set; }
        public int? NwkAddr { get; set; }
        public string? Type { get; set; }
        public string? Manufacturer { get; set; }
        public string? ModelId { get; set; }
        public string? PowerSource { get; set; }
        public int? Lqi { get; set; }
        public long? LastSeen { get; set; }
        public NetworkMapNodeDefinition? Definition { get; set; }
    }

    private class NetworkMapNodeDefinition
    {
        public string? Model { get; set; }
        public string? Vendor { get; set; }
    }

    private class NetworkMapLink
    {
        public NetworkMapLinkEndpoint Source { get; set; } = new();
        public NetworkMapLinkEndpoint Target { get; set; } = new();
        public int? Lqi { get; set; }
        public int? Linkquality { get; set; }
        public int? Depth { get; set; }
        public int? Relationship { get; set; }
        public string? SourceIeeeAddr { get; set; }
        public string? TargetIeeeAddr { get; set; }
        public int? SourceNwkAddr { get; set; }
        public List<NetworkMapRoute>? Routes { get; set; }
    }

    private class NetworkMapRoute
    {
        public int? DestinationAddress { get; set; }
        public int? NextHop { get; set; }
        public string? Status { get; set; }
    }

    private class NetworkMapLinkEndpoint
    {
        public string? IeeeAddr { get; set; }
        public int? NetworkAddress { get; set; }
        public int? NwkAddr { get; set; }
    }

    #endregion
}
