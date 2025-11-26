using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SDHome.Lib.Data;

public class SqlServerDeviceRepository(string connectionString) : IDeviceRepository
{
    private readonly string _connectionString = connectionString;

    public async Task EnsureCreatedAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var createTableSql = @"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[devices]') AND type in (N'U'))
            BEGIN
                CREATE TABLE devices (
                    device_id NVARCHAR(255) PRIMARY KEY,
                    friendly_name NVARCHAR(255) NOT NULL,
                    ieee_address NVARCHAR(255),
                    model_id NVARCHAR(255),
                    manufacturer NVARCHAR(255),
                    description NVARCHAR(MAX),
                    power_source BIT NOT NULL DEFAULT 0,
                    device_type NVARCHAR(50),
                    room NVARCHAR(255),
                    capabilities NVARCHAR(MAX),
                    attributes NVARCHAR(MAX),
                    last_seen DATETIME2,
                    is_available BIT NOT NULL DEFAULT 1,
                    created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    updated_at DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                );

                CREATE INDEX idx_devices_room ON devices(room);
                CREATE INDEX idx_devices_device_type ON devices(device_type);
                CREATE INDEX idx_devices_is_available ON devices(is_available);
            END
        ";

        await connection.ExecuteAsync(createTableSql);
    }

    public async Task<IEnumerable<Models.Device>> GetAllAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = "SELECT * FROM devices ORDER BY friendly_name";
        var devices = await connection.QueryAsync<DeviceDto>(sql);
        return devices.Select(MapToDevice);
    }

    public async Task<Models.Device?> GetByIdAsync(string deviceId)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = "SELECT * FROM devices WHERE device_id = @DeviceId";
        var device = await connection.QueryFirstOrDefaultAsync<DeviceDto>(sql, new { DeviceId = deviceId });
        return device != null ? MapToDevice(device) : null;
    }

    public async Task<Models.Device> CreateAsync(Models.Device device)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            INSERT INTO devices (
                device_id, friendly_name, ieee_address, model_id, manufacturer, 
                description, power_source, device_type, room, capabilities, 
                attributes, last_seen, is_available, created_at, updated_at
            ) VALUES (
                @DeviceId, @FriendlyName, @IeeeAddress, @ModelId, @Manufacturer,
                @Description, @PowerSource, @DeviceType, @Room, @Capabilities,
                @Attributes, @LastSeen, @IsAvailable, @CreatedAt, @UpdatedAt
            )";

        await connection.ExecuteAsync(sql, MapToDto(device));
        return device;
    }

    public async Task<Models.Device> UpdateAsync(Models.Device device)
    {
        device.UpdatedAt = DateTime.UtcNow;
        
        using var connection = new SqlConnection(_connectionString);
        var sql = @"
            UPDATE devices SET
                friendly_name = @FriendlyName,
                ieee_address = @IeeeAddress,
                model_id = @ModelId,
                manufacturer = @Manufacturer,
                description = @Description,
                power_source = @PowerSource,
                device_type = @DeviceType,
                room = @Room,
                capabilities = @Capabilities,
                attributes = @Attributes,
                last_seen = @LastSeen,
                is_available = @IsAvailable,
                updated_at = @UpdatedAt
            WHERE device_id = @DeviceId";

        await connection.ExecuteAsync(sql, MapToDto(device));
        return device;
    }

    public async Task<bool> DeleteAsync(string deviceId)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = "DELETE FROM devices WHERE device_id = @DeviceId";
        var affected = await connection.ExecuteAsync(sql, new { DeviceId = deviceId });
        return affected > 0;
    }

    public async Task<IEnumerable<Models.Device>> GetByRoomAsync(string room)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = "SELECT * FROM devices WHERE room = @Room ORDER BY friendly_name";
        var devices = await connection.QueryAsync<DeviceDto>(sql, new { Room = room });
        return devices.Select(MapToDevice);
    }

    public async Task<IEnumerable<Models.Device>> GetByDeviceTypeAsync(Models.DeviceType deviceType)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = "SELECT * FROM devices WHERE device_type = @DeviceType ORDER BY friendly_name";
        var devices = await connection.QueryAsync<DeviceDto>(sql, new { DeviceType = deviceType.ToString() });
        return devices.Select(MapToDevice);
    }

    private static Models.Device MapToDevice(DeviceDto dto)
    {
        return new Models.Device
        {
            DeviceId = dto.device_id,
            FriendlyName = dto.friendly_name,
            IeeeAddress = dto.ieee_address,
            ModelId = dto.model_id,
            Manufacturer = dto.manufacturer,
            Description = dto.description,
            PowerSource = dto.power_source,
            DeviceType = !string.IsNullOrEmpty(dto.device_type) 
                ? Enum.Parse<Models.DeviceType>(dto.device_type) 
                : null,
            Room = dto.room,
            Capabilities = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dto.capabilities ?? "[]") ?? new(),
            Attributes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(dto.attributes ?? "{}") ?? new(),
            LastSeen = dto.last_seen,
            IsAvailable = dto.is_available,
            CreatedAt = dto.created_at,
            UpdatedAt = dto.updated_at
        };
    }

    private static DeviceDto MapToDto(Models.Device device)
    {
        return new DeviceDto
        {
            device_id = device.DeviceId,
            friendly_name = device.FriendlyName,
            ieee_address = device.IeeeAddress,
            model_id = device.ModelId,
            manufacturer = device.Manufacturer,
            description = device.Description,
            power_source = device.PowerSource,
            device_type = device.DeviceType?.ToString(),
            room = device.Room,
            capabilities = System.Text.Json.JsonSerializer.Serialize(device.Capabilities),
            attributes = System.Text.Json.JsonSerializer.Serialize(device.Attributes),
            last_seen = device.LastSeen,
            is_available = device.IsAvailable,
            created_at = device.CreatedAt,
            updated_at = device.UpdatedAt
        };
    }

    private class DeviceDto
    {
        public string device_id { get; set; } = string.Empty;
        public string friendly_name { get; set; } = string.Empty;
        public string? ieee_address { get; set; }
        public string? model_id { get; set; }
        public string? manufacturer { get; set; }
        public string? description { get; set; }
        public bool power_source { get; set; }
        public string? device_type { get; set; }
        public string? room { get; set; }
        public string? capabilities { get; set; }
        public string? attributes { get; set; }
        public DateTime? last_seen { get; set; }
        public bool is_available { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }
}
