using Microsoft.AspNetCore.SignalR;
using SDHome.Api.Hubs;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

namespace SDHome.Api.Services;

/// <summary>
/// SignalR implementation of IRealtimeEventBroadcaster for broadcasting events to connected clients
/// </summary>
public class SignalREventBroadcaster(
    IHubContext<SignalsHub> hubContext,
    ILogger<SignalREventBroadcaster> logger) : IRealtimeEventBroadcaster
{
    public async Task BroadcastSignalEventAsync(SignalEvent signalEvent)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("SignalReceived", signalEvent);
            
            // Also broadcast to device-specific group if deviceId exists
            if (!string.IsNullOrEmpty(signalEvent.DeviceId))
            {
                await hubContext.Clients.Group($"device:{signalEvent.DeviceId}")
                    .SendAsync("DeviceSignalReceived", signalEvent);
            }
            
            logger.LogDebug("Broadcasted signal event: {EventType} from {DeviceId}", 
                signalEvent.EventType, signalEvent.DeviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast signal event");
        }
    }

    public async Task BroadcastSensorReadingAsync(SensorReading reading)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("ReadingReceived", reading);
            
            // Also broadcast to device-specific group if deviceId exists
            if (!string.IsNullOrEmpty(reading.DeviceId))
            {
                await hubContext.Clients.Group($"device:{reading.DeviceId}")
                    .SendAsync("DeviceReadingReceived", reading);
            }
            
            logger.LogDebug("Broadcasted sensor reading: {Metric}={Value} from {DeviceId}", 
                reading.Metric, reading.Value, reading.DeviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast sensor reading");
        }
    }

    public async Task BroadcastTriggerEventAsync(TriggerEvent trigger)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("TriggerReceived", trigger);
            
            // Also broadcast to device-specific group if deviceId exists
            if (!string.IsNullOrEmpty(trigger.DeviceId))
            {
                await hubContext.Clients.Group($"device:{trigger.DeviceId}")
                    .SendAsync("DeviceTriggerReceived", trigger);
            }
            
            logger.LogDebug("Broadcasted trigger event: {TriggerType} from {DeviceId}", 
                trigger.TriggerType, trigger.DeviceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast trigger event");
        }
    }

    public async Task BroadcastDeviceSyncProgressAsync(DeviceSyncProgress progress)
    {
        try
        {
            // Broadcast to all clients (global sync notification)
            await hubContext.Clients.All.SendAsync("DeviceSyncProgress", progress);
            
            // Also broadcast to sync-specific group for targeted updates
            if (!string.IsNullOrEmpty(progress.SyncId))
            {
                await hubContext.Clients.Group($"sync:{progress.SyncId}")
                    .SendAsync("DeviceSyncProgress", progress);
            }
            
            logger.LogDebug("Broadcasted device sync progress: {Status} - {Message}", 
                progress.Status, progress.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast device sync progress");
        }
    }

    public async Task BroadcastDevicePairingProgressAsync(DevicePairingProgress progress)
    {
        try
        {
            // Broadcast to all clients (global pairing notification)
            await hubContext.Clients.All.SendAsync("DevicePairingProgress", progress);
            
            // Also broadcast to pairing-specific group for targeted updates
            if (!string.IsNullOrEmpty(progress.PairingId))
            {
                await hubContext.Clients.Group($"pairing:{progress.PairingId}")
                    .SendAsync("DevicePairingProgress", progress);
            }
            
            logger.LogDebug("Broadcasted device pairing progress: {Status} - {Message}", 
                progress.Status, progress.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast device pairing progress");
        }
    }

    public async Task BroadcastDeviceStateUpdateAsync(DeviceStateUpdate stateUpdate)
    {
        try
        {
            // Broadcast to device-specific group for instant UI updates
            if (!string.IsNullOrEmpty(stateUpdate.DeviceId))
            {
                await hubContext.Clients.Group($"device:{stateUpdate.DeviceId}")
                    .SendAsync("DeviceStateUpdate", stateUpdate);
                
                // Also broadcast to all clients (for dashboard/list views)
                await hubContext.Clients.All.SendAsync("DeviceStateUpdate", stateUpdate);
            }
            
            logger.LogDebug("Broadcasted device state update for {DeviceId}: {PropertyCount} properties", 
                stateUpdate.DeviceId, stateUpdate.State.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to broadcast device state update");
        }
    }
}
