using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

namespace SDHome.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController(IDeviceService deviceService) : ControllerBase
{
    private readonly IDeviceService _deviceService = deviceService;

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
    /// Sync devices from Zigbee2MQTT
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult> SyncDevices()
    {
        await _deviceService.SyncDevicesFromZigbee2MqttAsync();
        return Ok(new { message = "Device sync completed" });
    }
}
