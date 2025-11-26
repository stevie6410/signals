namespace SDHome.Lib.Data;

public interface IDeviceRepository
{
    Task<IEnumerable<Models.Device>> GetAllAsync();
    Task<Models.Device?> GetByIdAsync(string deviceId);
    Task<Models.Device> CreateAsync(Models.Device device);
    Task<Models.Device> UpdateAsync(Models.Device device);
    Task<bool> DeleteAsync(string deviceId);
    Task<IEnumerable<Models.Device>> GetByRoomAsync(string room);
    Task<IEnumerable<Models.Device>> GetByDeviceTypeAsync(Models.DeviceType deviceType);
}
