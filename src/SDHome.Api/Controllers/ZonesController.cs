using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDHome.Lib.Data;
using SDHome.Lib.Data.Entities;
using SDHome.Lib.Models;

namespace SDHome.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ZonesController(SignalsDbContext db) : ControllerBase
{
    /// <summary>
    /// Get all zones as a flat list
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Zone>>> GetZones()
    {
        var zones = await db.Zones
            .AsNoTracking()
            .Include(z => z.ParentZone)
            .OrderBy(z => z.ParentZoneId)
            .ThenBy(z => z.SortOrder)
            .ThenBy(z => z.Name)
            .ToListAsync();

        return zones.Select(z => z.ToModel(includeParent: true)).ToList();
    }

    /// <summary>
    /// Get zones as a hierarchical tree (only root zones with nested children)
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<List<Zone>>> GetZoneTree()
    {
        // Load ALL zones in one query
        var allZones = await db.Zones
            .AsNoTracking()
            .OrderBy(z => z.SortOrder)
            .ThenBy(z => z.Name)
            .ToListAsync();

        // Build the tree in memory to avoid EF tracking issues
        var zoneLookup = allZones.ToDictionary(z => z.Id);
        
        // Connect parents to children
        foreach (var zone in allZones)
        {
            if (zone.ParentZoneId.HasValue && zoneLookup.TryGetValue(zone.ParentZoneId.Value, out var parent))
            {
                parent.ChildZones.Add(zone);
            }
        }

        // Get only root zones (no parent)
        var rootZones = allZones.Where(z => z.ParentZoneId == null).ToList();

        return rootZones.Select(z => z.ToModel(includeChildren: true)).ToList();
    }

    /// <summary>
    /// Get a specific zone by ID
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Zone>> GetZone(int id)
    {
        var zone = await db.Zones
            .AsNoTracking()
            .Include(z => z.ParentZone)
            .Include(z => z.ChildZones)
            .Include(z => z.Devices)
            .FirstOrDefaultAsync(z => z.Id == id);

        if (zone == null)
            return NotFound();

        return zone.ToModel(includeChildren: true, includeParent: true);
    }

    /// <summary>
    /// Get all devices in a zone (optionally including child zones)
    /// </summary>
    [HttpGet("{id:int}/devices")]
    public async Task<ActionResult<List<Device>>> GetZoneDevices(int id, [FromQuery] bool includeChildren = false)
    {
        var zone = await db.Zones.FindAsync(id);
        if (zone == null)
            return NotFound();

        List<int> zoneIds = [id];

        if (includeChildren)
        {
            zoneIds = await GetAllDescendantZoneIds(id);
        }

        var devices = await db.Devices
            .AsNoTracking()
            .Where(d => d.ZoneId != null && zoneIds.Contains(d.ZoneId.Value))
            .Include(d => d.Zone)
            .OrderBy(d => d.FriendlyName)
            .ToListAsync();

        return devices.Select(d => d.ToModel()).ToList();
    }

    /// <summary>
    /// Create a new zone
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Zone>> CreateZone([FromBody] CreateZoneRequest request)
    {
        // Validate parent exists if specified
        if (request.ParentZoneId.HasValue)
        {
            var parentExists = await db.Zones.AnyAsync(z => z.Id == request.ParentZoneId.Value);
            if (!parentExists)
                return BadRequest("Parent zone does not exist");
        }

        var entity = new ZoneEntity
        {
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
            Color = request.Color,
            ParentZoneId = request.ParentZoneId,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Zones.Add(entity);
        await db.SaveChangesAsync();

        // Reload with parent
        await db.Entry(entity).Reference(z => z.ParentZone).LoadAsync();

        return CreatedAtAction(nameof(GetZone), new { id = entity.Id }, entity.ToModel(includeParent: true));
    }

    /// <summary>
    /// Update a zone
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<Zone>> UpdateZone(int id, [FromBody] UpdateZoneRequest request)
    {
        var entity = await db.Zones.FindAsync(id);
        if (entity == null)
            return NotFound();

        // Prevent circular references
        if (request.ParentZoneId.HasValue)
        {
            if (request.ParentZoneId.Value == id)
                return BadRequest("A zone cannot be its own parent");

            // Check if new parent is a descendant of this zone
            var descendants = await GetAllDescendantZoneIds(id);
            if (descendants.Contains(request.ParentZoneId.Value))
                return BadRequest("Cannot set a descendant zone as parent (would create circular reference)");

            var parentExists = await db.Zones.AnyAsync(z => z.Id == request.ParentZoneId.Value);
            if (!parentExists)
                return BadRequest("Parent zone does not exist");
        }

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.Icon = request.Icon;
        entity.Color = request.Color;
        entity.ParentZoneId = request.ParentZoneId;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Reload with parent
        await db.Entry(entity).Reference(z => z.ParentZone).LoadAsync();

        return entity.ToModel(includeParent: true);
    }

    /// <summary>
    /// Delete a zone (devices will have their ZoneId set to null)
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteZone(int id, [FromQuery] bool reassignToParent = false)
    {
        var entity = await db.Zones
            .Include(z => z.ChildZones)
            .Include(z => z.Devices)
            .FirstOrDefaultAsync(z => z.Id == id);

        if (entity == null)
            return NotFound();

        if (reassignToParent && entity.ParentZoneId.HasValue)
        {
            // Move children and devices to parent zone
            foreach (var child in entity.ChildZones)
            {
                child.ParentZoneId = entity.ParentZoneId;
            }
            foreach (var device in entity.Devices)
            {
                device.ZoneId = entity.ParentZoneId;
            }
        }
        else
        {
            // Move children to root level
            foreach (var child in entity.ChildZones)
            {
                child.ParentZoneId = null;
            }
            // Devices will be set to null by cascade
        }

        db.Zones.Remove(entity);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Assign a device to a zone
    /// </summary>
    [HttpPost("{id:int}/devices/{deviceId}")]
    public async Task<ActionResult> AssignDeviceToZone(int id, string deviceId)
    {
        var zone = await db.Zones.FindAsync(id);
        if (zone == null)
            return NotFound("Zone not found");

        var device = await db.Devices.FindAsync(deviceId);
        if (device == null)
            return NotFound("Device not found");

        device.ZoneId = id;
        device.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Remove a device from its zone
    /// </summary>
    [HttpDelete("{id:int}/devices/{deviceId}")]
    public async Task<ActionResult> RemoveDeviceFromZone(int id, string deviceId)
    {
        var device = await db.Devices.FindAsync(deviceId);
        if (device == null)
            return NotFound("Device not found");

        if (device.ZoneId != id)
            return BadRequest("Device is not in this zone");

        device.ZoneId = null;
        device.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }

    // ===== CAPABILITY ASSIGNMENT ENDPOINTS =====

    /// <summary>
    /// Get all zones with their capability assignments (for dashboard)
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<List<ZoneWithCapabilities>>> GetZonesForDashboard()
    {
        var zones = await db.Zones
            .AsNoTracking()
            .Include(z => z.ParentZone)
            .Include(z => z.ChildZones)
            .Include(z => z.Devices)
            .Include(z => z.CapabilityAssignments)
                .ThenInclude(c => c.Device)
            .OrderBy(z => z.ParentZoneId ?? 0)
            .ThenBy(z => z.SortOrder)
            .ThenBy(z => z.Name)
            .ToListAsync();

        return zones.Select(z => new ZoneWithCapabilities
        {
            Id = z.Id,
            Name = z.Name,
            Description = z.Description,
            Icon = z.Icon,
            Color = z.Color,
            ParentZoneId = z.ParentZoneId,
            SortOrder = z.SortOrder,
            CreatedAt = z.CreatedAt,
            UpdatedAt = z.UpdatedAt,
            ParentZone = z.ParentZone?.ToModel(),
            ChildZones = z.ChildZones.OrderBy(c => c.SortOrder).Select(c => c.ToModel()).ToList(),
            CapabilityAssignments = z.CapabilityAssignments.OrderBy(c => c.Capability).ThenBy(c => c.Priority).Select(c => c.ToModel()).ToList(),
            Devices = z.Devices.OrderBy(d => d.FriendlyName).Select(d => d.ToModel()).ToList()
        }).ToList();
    }

    /// <summary>
    /// Get all available capability types
    /// </summary>
    [HttpGet("capabilities/types")]
    public ActionResult<object> GetCapabilityTypes()
    {
        return new
        {
            capabilities = ZoneCapabilities.All,
            labels = ZoneCapabilities.Labels,
            icons = ZoneCapabilities.Icons,
            defaultProperties = ZoneCapabilities.DefaultProperties
        };
    }

    /// <summary>
    /// Get all capability assignments across all zones
    /// </summary>
    [HttpGet("capabilities/all")]
    public async Task<ActionResult<List<ZoneCapabilityAssignment>>> GetAllCapabilities()
    {
        var assignments = await db.ZoneCapabilityAssignments
            .AsNoTracking()
            .Include(a => a.Zone)
            .Include(a => a.Device)
            .OrderBy(a => a.Zone!.Name)
            .ThenBy(a => a.Capability)
            .ThenBy(a => a.Priority)
            .ToListAsync();

        return assignments.Select(a => a.ToModel()).ToList();
    }

    /// <summary>
    /// Get all capability assignments for a zone
    /// </summary>
    [HttpGet("{id:int}/capabilities")]
    public async Task<ActionResult<List<ZoneCapabilityAssignment>>> GetZoneCapabilities(int id)
    {
        var zone = await db.Zones.FindAsync(id);
        if (zone == null)
            return NotFound("Zone not found");

        var assignments = await db.ZoneCapabilityAssignments
            .AsNoTracking()
            .Include(a => a.Zone)
            .Include(a => a.Device)
            .Where(a => a.ZoneId == id)
            .OrderBy(a => a.Capability)
            .ThenBy(a => a.Priority)
            .ToListAsync();

        return assignments.Select(a => a.ToModel()).ToList();
    }

    /// <summary>
    /// Assign a device as the primary for a capability in a zone
    /// </summary>
    [HttpPost("{id:int}/capabilities")]
    public async Task<ActionResult<ZoneCapabilityAssignment>> AssignCapability(
        int id, 
        [FromBody] CreateZoneCapabilityAssignmentRequest request)
    {
        var zone = await db.Zones.FindAsync(id);
        if (zone == null)
            return NotFound("Zone not found");

        var device = await db.Devices.FindAsync(request.DeviceId);
        if (device == null)
            return NotFound("Device not found");

        // Check if this exact assignment already exists
        var existing = await db.ZoneCapabilityAssignments
            .FirstOrDefaultAsync(a => 
                a.ZoneId == id && 
                a.Capability == request.Capability && 
                a.Priority == request.Priority);

        if (existing != null)
        {
            // Update existing assignment
            existing.DeviceId = request.DeviceId;
            existing.Property = request.Property;
            existing.DisplayName = request.DisplayName;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new assignment
            existing = new ZoneCapabilityAssignmentEntity
            {
                ZoneId = id,
                Capability = request.Capability,
                DeviceId = request.DeviceId,
                Property = request.Property,
                Priority = request.Priority,
                DisplayName = request.DisplayName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ZoneCapabilityAssignments.Add(existing);
        }

        await db.SaveChangesAsync();

        // Reload with navigation properties
        await db.Entry(existing).Reference(a => a.Zone).LoadAsync();
        await db.Entry(existing).Reference(a => a.Device).LoadAsync();

        return CreatedAtAction(
            nameof(GetZoneCapabilities), 
            new { id }, 
            existing.ToModel());
    }

    /// <summary>
    /// Remove a capability assignment from a zone
    /// </summary>
    [HttpDelete("{id:int}/capabilities/{capability}")]
    public async Task<ActionResult> RemoveCapability(int id, string capability, [FromQuery] int priority = 0)
    {
        var assignment = await db.ZoneCapabilityAssignments
            .FirstOrDefaultAsync(a => 
                a.ZoneId == id && 
                a.Capability == capability && 
                a.Priority == priority);

        if (assignment == null)
            return NotFound("Capability assignment not found");

        db.ZoneCapabilityAssignments.Remove(assignment);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Get a zone with all its capability assignments
    /// </summary>
    [HttpGet("{id:int}/full")]
    public async Task<ActionResult<ZoneWithCapabilities>> GetZoneWithCapabilities(int id)
    {
        var zone = await db.Zones
            .AsNoTracking()
            .Include(z => z.ParentZone)
            .Include(z => z.ChildZones)
            .Include(z => z.Devices)
            .Include(z => z.CapabilityAssignments)
                .ThenInclude(c => c.Device)
            .FirstOrDefaultAsync(z => z.Id == id);

        if (zone == null)
            return NotFound();

        return new ZoneWithCapabilities
        {
            Id = zone.Id,
            Name = zone.Name,
            Description = zone.Description,
            Icon = zone.Icon,
            Color = zone.Color,
            ParentZoneId = zone.ParentZoneId,
            SortOrder = zone.SortOrder,
            CreatedAt = zone.CreatedAt,
            UpdatedAt = zone.UpdatedAt,
            ParentZone = zone.ParentZone?.ToModel(),
            ChildZones = zone.ChildZones.OrderBy(c => c.SortOrder).Select(c => c.ToModel()).ToList(),
            CapabilityAssignments = zone.CapabilityAssignments.OrderBy(c => c.Capability).ThenBy(c => c.Priority).Select(c => c.ToModel()).ToList(),
            Devices = zone.Devices.OrderBy(d => d.FriendlyName).Select(d => d.ToModel()).ToList()
        };
    }

    private async Task LoadChildrenRecursively(ZoneEntity zone)
    {
        await db.Entry(zone).Collection(z => z.ChildZones).LoadAsync();
        foreach (var child in zone.ChildZones)
        {
            await LoadChildrenRecursively(child);
        }
    }

    private async Task<List<int>> GetAllDescendantZoneIds(int zoneId)
    {
        var result = new List<int> { zoneId };
        var children = await db.Zones
            .Where(z => z.ParentZoneId == zoneId)
            .Select(z => z.Id)
            .ToListAsync();

        foreach (var childId in children)
        {
            result.AddRange(await GetAllDescendantZoneIds(childId));
        }

        return result;
    }
}
