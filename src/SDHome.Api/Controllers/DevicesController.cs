using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

namespace SDHome.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController(IDeviceService deviceService, IRealtimeEventBroadcaster broadcaster) : ControllerBase
{
    private readonly IDeviceService _deviceService = deviceService;
    private readonly IRealtimeEventBroadcaster _broadcaster = broadcaster;

    /// <summary>
    /// Get all devices
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Device>>> GetDevices()
    {
        var devices = await _deviceService.GetAllDevicesAsync();
        return Ok(devices);
    }

    /// <summary>
    /// Get a specific device by ID
    /// </summary>
    [HttpGet("{deviceId}")]
    public async Task<ActionResult<Device>> GetDevice(string deviceId)
    {
        var device = await _deviceService.GetDeviceAsync(deviceId);
        if (device == null)
        {
            return NotFound();
        }
        return Ok(device);
    }

    /// <summary>
    /// Get full device definition with capabilities from Zigbee2MQTT
    /// </summary>
    [HttpGet("{deviceId}/definition")]
    public async Task<ActionResult<DeviceDefinition>> GetDeviceDefinition(string deviceId)
    {
        try
        {
            var definition = await _deviceService.GetDeviceDefinitionAsync(deviceId);
            if (definition == null)
            {
                return NotFound(new { error = "Device not found" });
            }
            return Ok(definition);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to get device definition", details = ex.Message });
        }
    }

    /// <summary>
    /// Set device state (send command to device)
    /// </summary>
    [HttpPost("{deviceId}/state")]
    public async Task<ActionResult> SetDeviceState(string deviceId, [FromBody] SetDeviceStateRequest request)
    {
        try
        {
            await _deviceService.SetDeviceStateAsync(deviceId, request.State);
            return Ok(new { success = true, deviceId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to set device state", details = ex.Message });
        }
    }

    /// <summary>
    /// Update device attributes (room, device type, etc.)
    /// </summary>
    [HttpPut("{deviceId}")]
    public async Task<ActionResult<Device>> UpdateDevice(string deviceId, [FromBody] Device device)
    {
        if (deviceId != device.DeviceId)
        {
            return BadRequest("Device ID mismatch");
        }

        try
        {
            var updated = await _deviceService.UpdateDeviceAsync(device);
            return Ok(updated);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Rename a device in Zigbee2MQTT and local database
    /// </summary>
    [HttpPost("{deviceId}/rename")]
    public async Task<ActionResult<Device>> RenameDevice(string deviceId, [FromBody] RenameDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return BadRequest(new { error = "New name is required" });
        }

        try
        {
            var device = await _deviceService.RenameDeviceAsync(deviceId, request.NewName);
            return Ok(device);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to rename device", details = ex.Message });
        }
    }

    /// <summary>
    /// Get devices by room
    /// </summary>
    [HttpGet("by-room/{room}")]
    public async Task<ActionResult<IEnumerable<Device>>> GetDevicesByRoom(string room)
    {
        var devices = await _deviceService.GetDevicesByRoomAsync(room);
        return Ok(devices);
    }

    /// <summary>
    /// Get devices by type
    /// </summary>
    [HttpGet("by-type/{deviceType}")]
    public async Task<ActionResult<IEnumerable<Device>>> GetDevicesByType(DeviceType deviceType)
    {
        var devices = await _deviceService.GetDevicesByTypeAsync(deviceType);
        return Ok(devices);
    }

    /// <summary>
    /// Sync devices from Zigbee2MQTT (legacy endpoint)
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult> SyncDevices()
    {
        try
        {
            await _deviceService.SyncDevicesFromZigbee2MqttAsync();
            var devices = await _deviceService.GetAllDevicesAsync();
            return Ok(new { message = "Device sync completed", deviceCount = devices.Count() });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Device sync failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Sync devices from Zigbee2MQTT with real-time progress updates via SignalR
    /// </summary>
    [HttpPost("sync/realtime")]
    public async Task<ActionResult> SyncDevicesRealtime()
    {
        try
        {
            var syncId = await _deviceService.SyncDevicesWithProgressAsync(_broadcaster);
            var devices = await _deviceService.GetAllDevicesAsync();
            return Ok(new { 
                message = "Device sync completed", 
                syncId,
                deviceCount = devices.Count() 
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Device sync failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Start pairing mode to allow new Zigbee devices to join
    /// </summary>
    [HttpPost("pairing/start")]
    public async Task<ActionResult> StartPairing([FromBody] StartPairingRequest? request)
    {
        try
        {
            var duration = request?.Duration ?? 120;
            var pairingId = await _deviceService.StartPairingModeAsync(duration, _broadcaster);
            return Ok(new { 
                message = "Pairing mode started",
                pairingId,
                duration
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to start pairing mode", details = ex.Message });
        }
    }

    /// <summary>
    /// Stop pairing mode
    /// </summary>
    [HttpPost("pairing/stop")]
    public async Task<ActionResult> StopPairing([FromBody] StopPairingRequest request)
    {
        try
        {
            await _deviceService.StopPairingModeAsync(request.PairingId, _broadcaster);
            return Ok(new { message = "Pairing mode stopped" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to stop pairing mode", details = ex.Message });
        }
    }

    /// <summary>
    /// Check if pairing mode is active
    /// </summary>
    [HttpGet("pairing/status")]
    public async Task<ActionResult> GetPairingStatus()
    {
        var isActive = await _deviceService.IsPairingActiveAsync();
        return Ok(new { isActive });
    }
}

/// <summary>
/// Request to stop pairing mode
/// </summary>
public class StopPairingRequest
{
    public string PairingId { get; set; } = string.Empty;
}

/// <summary>
/// Request to rename a device
/// </summary>
public class RenameDeviceRequest
{
    public string NewName { get; set; } = string.Empty;
}
