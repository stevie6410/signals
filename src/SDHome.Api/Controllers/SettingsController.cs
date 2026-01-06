using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDHome.Lib.Data;
using SDHome.Lib.Data.Entities;
using SDHome.Lib.Models;

namespace SDHome.Api.Controllers;

[ApiController]
[Route("/api/settings")]
public class SettingsController(SignalsDbContext db) : ControllerBase
{
    #region Capability Mappings

    /// <summary>
    /// Get all capability mappings
    /// </summary>
    [HttpGet("capability-mappings")]
    public async Task<List<CapabilityMapping>> GetCapabilityMappings()
    {
        var mappings = await db.CapabilityMappings
            .AsNoTracking()
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Capability)
            .ToListAsync();

        return mappings.Select(m => m.ToModel()).ToList();
    }

    /// <summary>
    /// Get a specific capability mapping by ID
    /// </summary>
    [HttpGet("capability-mappings/{id:int}")]
    public async Task<ActionResult<CapabilityMapping>> GetCapabilityMapping(int id)
    {
        var mapping = await db.CapabilityMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);

        if (mapping == null)
            return NotFound();

        return mapping.ToModel();
    }

    /// <summary>
    /// Get mappings for a specific capability (e.g., "occupancy", "motion")
    /// </summary>
    [HttpGet("capability-mappings/by-capability/{capability}")]
    public async Task<List<CapabilityMapping>> GetMappingsByCapability(string capability)
    {
        var mappings = await db.CapabilityMappings
            .AsNoTracking()
            .Where(m => m.Capability == capability)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();

        return mappings.Select(m => m.ToModel()).ToList();
    }

    /// <summary>
    /// Create a new capability mapping
    /// </summary>
    [HttpPost("capability-mappings")]
    public async Task<ActionResult<CapabilityMapping>> CreateCapabilityMapping([FromBody] CapabilityMapping mapping)
    {
        var entity = CapabilityMappingEntity.FromModel(mapping);
        entity.Id = 0; // Ensure it's a new record
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.IsSystemDefault = false; // User-created mappings are never system defaults

        db.CapabilityMappings.Add(entity);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCapabilityMapping), new { id = entity.Id }, entity.ToModel());
    }

    /// <summary>
    /// Update an existing capability mapping
    /// </summary>
    [HttpPut("capability-mappings/{id:int}")]
    public async Task<ActionResult<CapabilityMapping>> UpdateCapabilityMapping(int id, [FromBody] CapabilityMapping mapping)
    {
        var existing = await db.CapabilityMappings.FindAsync(id);
        if (existing == null)
            return NotFound();

        existing.Capability = mapping.Capability;
        existing.DeviceType = mapping.DeviceType;
        existing.Property = mapping.Property;
        existing.DisplayName = mapping.DisplayName;
        existing.Icon = mapping.Icon;
        existing.StateMappings = System.Text.Json.JsonSerializer.Serialize(mapping.StateMappings);
        existing.Unit = mapping.Unit;
        existing.DisplayOrder = mapping.DisplayOrder;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return existing.ToModel();
    }

    /// <summary>
    /// Delete a capability mapping (cannot delete system defaults)
    /// </summary>
    [HttpDelete("capability-mappings/{id:int}")]
    public async Task<IActionResult> DeleteCapabilityMapping(int id)
    {
        var existing = await db.CapabilityMappings.FindAsync(id);
        if (existing == null)
            return NotFound();

        if (existing.IsSystemDefault)
            return BadRequest("Cannot delete system default mappings. You can only override them.");

        db.CapabilityMappings.Remove(existing);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Seed default capability mappings (creates system defaults if they don't exist)
    /// </summary>
    [HttpPost("capability-mappings/seed-defaults")]
    public async Task<ActionResult<int>> SeedDefaultMappings()
    {
        var defaults = GetDefaultCapabilityMappings();
        var seeded = 0;

        foreach (var mapping in defaults)
        {
            var exists = await db.CapabilityMappings.AnyAsync(m => 
                m.Capability == mapping.Capability && 
                m.IsSystemDefault);

            if (!exists)
            {
                db.CapabilityMappings.Add(mapping);
                seeded++;
            }
        }

        await db.SaveChangesAsync();
        return Ok(seeded);
    }

    /// <summary>
    /// Get the friendly state for a raw value
    /// </summary>
    [HttpGet("capability-mappings/translate")]
    public async Task<ActionResult<TranslatedState>> TranslateState(
        [FromQuery] string capability, 
        [FromQuery] string rawValue,
        [FromQuery] string? deviceType = null)
    {
        var mapping = await db.CapabilityMappings
            .AsNoTracking()
            .Where(m => m.Capability == capability)
            .Where(m => deviceType == null || m.DeviceType == null || m.DeviceType == deviceType)
            .OrderByDescending(m => m.DeviceType != null) // Prefer device-specific mappings
            .FirstOrDefaultAsync();

        if (mapping == null)
            return new TranslatedState { RawValue = rawValue, FriendlyName = rawValue };

        var model = mapping.ToModel();
        var friendly = model.GetFriendlyState(rawValue);

        return new TranslatedState
        {
            RawValue = rawValue,
            FriendlyName = friendly ?? rawValue,
            Icon = model.StateMappings.FirstOrDefault(s => s.FriendlyName == friendly)?.Icon,
            Color = model.StateMappings.FirstOrDefault(s => s.FriendlyName == friendly)?.Color,
            IsActive = model.StateMappings.FirstOrDefault(s => s.FriendlyName == friendly)?.IsActive ?? false
        };
    }

    #endregion

    #region Default Mappings

    private static List<CapabilityMappingEntity> GetDefaultCapabilityMappings()
    {
        return
        [
            // Occupancy (presence detection)
            new CapabilityMappingEntity
            {
                Capability = "occupancy",
                Property = "occupancy",
                DisplayName = "Presence",
                Icon = "👤",
                StateMappings = System.Text.Json.JsonSerializer.Serialize(new List<StateMapping>
                {
                    new() { RawValue = true, FriendlyName = "Occupied", Icon = "👤", Color = "green", IsActive = true },
                    new() { RawValue = false, FriendlyName = "Vacant", Icon = "⚪", Color = "gray", IsActive = false }
                }),
                IsSystemDefault = true,
                DisplayOrder = 10
            },
            
            // Motion (PIR detection)
            new CapabilityMappingEntity
            {
                Capability = "motion",
                Property = "motion",
                DisplayName = "Motion",
                Icon = "🏃",
                StateMappings = System.Text.Json.JsonSerializer.Serialize(new List<StateMapping>
                {
                    new() { RawValue = true, FriendlyName = "Motion Detected", Icon = "🏃", Color = "orange", IsActive = true },
                    new() { RawValue = false, FriendlyName = "No Motion", Icon = "⚪", Color = "gray", IsActive = false }
                }),
                IsSystemDefault = true,
                DisplayOrder = 20
            },
            
            // Contact sensors
            new CapabilityMappingEntity
            {
                Capability = "contact",
                Property = "contact",
                DisplayName = "Contact",
                Icon = "🚪",
                StateMappings = System.Text.Json.JsonSerializer.Serialize(new List<StateMapping>
                {
                    new() { RawValue = true, FriendlyName = "Closed", Icon = "🚪", Color = "green", IsActive = false },
                    new() { RawValue = false, FriendlyName = "Open", Icon = "🚪", Color = "red", IsActive = true }
                }),
                IsSystemDefault = true,
                DisplayOrder = 30
            },
            
            // Switch state
            new CapabilityMappingEntity
            {
                Capability = "state",
                Property = "state",
                DisplayName = "Power",
                Icon = "⚡",
                StateMappings = System.Text.Json.JsonSerializer.Serialize(new List<StateMapping>
                {
                    new() { RawValue = "ON", FriendlyName = "On", Icon = "💡", Color = "yellow", IsActive = true },
                    new() { RawValue = "OFF", FriendlyName = "Off", Icon = "⚫", Color = "gray", IsActive = false }
                }),
                IsSystemDefault = true,
                DisplayOrder = 40
            },
            
            // Water leak
            new CapabilityMappingEntity
            {
                Capability = "water_leak",
                Property = "water_leak",
                DisplayName = "Water Leak",
                Icon = "💧",
                StateMappings = System.Text.Json.JsonSerializer.Serialize(new List<StateMapping>
                {
                    new() { RawValue = true, FriendlyName = "Leak Detected", Icon = "💧", Color = "red", IsActive = true },
                    new() { RawValue = false, FriendlyName = "Dry", Icon = "✓", Color = "green", IsActive = false }
                }),
                IsSystemDefault = true,
                DisplayOrder = 50
            },
            
            // Battery low
            new CapabilityMappingEntity
            {
                Capability = "battery_low",
                Property = "battery_low",
                DisplayName = "Battery Status",
                Icon = "🔋",
                StateMappings = System.Text.Json.JsonSerializer.Serialize(new List<StateMapping>
                {
                    new() { RawValue = true, FriendlyName = "Low Battery", Icon = "🪫", Color = "red", IsActive = true },
                    new() { RawValue = false, FriendlyName = "OK", Icon = "🔋", Color = "green", IsActive = false }
                }),
                IsSystemDefault = true,
                DisplayOrder = 60
            },
            
            // Temperature (numeric, but with unit)
            new CapabilityMappingEntity
            {
                Capability = "temperature",
                Property = "temperature",
                DisplayName = "Temperature",
                Icon = "🌡️",
                Unit = "°C",
                StateMappings = "[]", // No state mappings for numeric values
                IsSystemDefault = true,
                DisplayOrder = 100
            },
            
            // Humidity
            new CapabilityMappingEntity
            {
                Capability = "humidity",
                Property = "humidity",
                DisplayName = "Humidity",
                Icon = "💧",
                Unit = "%",
                StateMappings = "[]",
                IsSystemDefault = true,
                DisplayOrder = 110
            },
            
            // Illuminance
            new CapabilityMappingEntity
            {
                Capability = "illuminance",
                Property = "illuminance",
                DisplayName = "Light Level",
                Icon = "☀️",
                Unit = "lx",
                StateMappings = "[]",
                IsSystemDefault = true,
                DisplayOrder = 120
            },
            
            // Battery percentage
            new CapabilityMappingEntity
            {
                Capability = "battery",
                Property = "battery",
                DisplayName = "Battery",
                Icon = "🔋",
                Unit = "%",
                StateMappings = "[]",
                IsSystemDefault = true,
                DisplayOrder = 130
            },
            
            // Link quality
            new CapabilityMappingEntity
            {
                Capability = "linkquality",
                Property = "linkquality",
                DisplayName = "Signal Strength",
                Icon = "📶",
                Unit = null,
                StateMappings = "[]",
                IsSystemDefault = true,
                DisplayOrder = 140
            }
        ];
    }

    #endregion
}

public class TranslatedState
{
    public string RawValue { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; }
}
