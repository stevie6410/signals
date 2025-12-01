using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

/// <summary>
/// Represents a real-time device state update
/// </summary>
public class DeviceStateUpdate
{
    public string DeviceId { get; set; } = string.Empty;
    public Dictionary<string, object?> State { get; set; } = new();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Interface for broadcasting real-time events to connected clients.
/// Implemented in the API layer (SignalR), but called from the Lib layer.
/// </summary>
public interface IRealtimeEventBroadcaster
{
    Task BroadcastSignalEventAsync(SignalEvent signalEvent);
    Task BroadcastSensorReadingAsync(SensorReading reading);
    Task BroadcastTriggerEventAsync(TriggerEvent trigger);
    Task BroadcastDeviceSyncProgressAsync(DeviceSyncProgress progress);
    Task BroadcastDevicePairingProgressAsync(DevicePairingProgress progress);
    Task BroadcastDeviceStateUpdateAsync(DeviceStateUpdate stateUpdate);
}

/// <summary>
/// No-op implementation when real-time broadcasting is not available
/// </summary>
public class NullRealtimeEventBroadcaster : IRealtimeEventBroadcaster
{
    public Task BroadcastSignalEventAsync(SignalEvent signalEvent) => Task.CompletedTask;
    public Task BroadcastSensorReadingAsync(SensorReading reading) => Task.CompletedTask;
    public Task BroadcastTriggerEventAsync(TriggerEvent trigger) => Task.CompletedTask;
    public Task BroadcastDeviceSyncProgressAsync(DeviceSyncProgress progress) => Task.CompletedTask;
    public Task BroadcastDevicePairingProgressAsync(DevicePairingProgress progress) => Task.CompletedTask;
    public Task BroadcastDeviceStateUpdateAsync(DeviceStateUpdate stateUpdate) => Task.CompletedTask;
}
