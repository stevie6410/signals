using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public interface IDeviceService
{
    Task<IEnumerable<Device>> GetAllDevicesAsync();
    Task<Device?> GetDeviceAsync(string deviceId);
    Task<Device> UpdateDeviceAsync(Device device);
    Task<Device> RenameDeviceAsync(string currentName, string newName);
    Task SyncDevicesFromZigbee2MqttAsync();
    Task<string> SyncDevicesWithProgressAsync(IRealtimeEventBroadcaster broadcaster);
    Task<IEnumerable<Device>> GetDevicesByRoomAsync(string room);
    Task<IEnumerable<Device>> GetDevicesByTypeAsync(DeviceType deviceType);
    
    // Device definition and state
    Task<DeviceDefinition?> GetDeviceDefinitionAsync(string deviceId);
    Task SetDeviceStateAsync(string deviceId, Dictionary<string, object> state);
    
    // Pairing methods
    Task<string> StartPairingModeAsync(int durationSeconds, IRealtimeEventBroadcaster broadcaster, CancellationToken cancellationToken = default);
    Task StopPairingModeAsync(string pairingId, IRealtimeEventBroadcaster broadcaster);
    Task<bool> IsPairingActiveAsync();
}
