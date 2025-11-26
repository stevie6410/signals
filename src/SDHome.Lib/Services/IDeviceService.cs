namespace SDHome.Lib.Services;

public interface IDeviceService
{
    Task<IEnumerable<Models.Device>> GetAllDevicesAsync();
    Task<Models.Device?> GetDeviceAsync(string deviceId);
    Task<Models.Device> UpdateDeviceAsync(Models.Device device);
    Task SyncDevicesFromZigbee2MqttAsync();
    Task<IEnumerable<Models.Device>> GetDevicesByRoomAsync(string room);
    Task<IEnumerable<Models.Device>> GetDevicesByTypeAsync(Models.DeviceType deviceType);
}
