using Microsoft.AspNetCore.Mvc;
using SDHome.Lib.Models;
using SDHome.Lib.Services;

namespace SDHome.Api.Controllers;

[ApiController]
[Route("/api/custom-triggers")]
public class CustomTriggersController(
    ICustomTriggerService customTriggerService) : ControllerBase
{
    // ===== Custom Trigger Rules =====
    
    /// <summary>
    /// Get all custom trigger rules (summary view)
    /// </summary>
    [HttpGet]
    public async Task<List<CustomTriggerSummary>> GetCustomTriggers()
    {
        return await customTriggerService.GetCustomTriggerSummariesAsync();
    }
    
    /// <summary>
    /// Get a single custom trigger rule with full details
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomTriggerRule>> GetCustomTrigger(Guid id)
    {
        var trigger = await customTriggerService.GetCustomTriggerAsync(id);
        if (trigger == null) return NotFound();
        return trigger;
    }
    
    /// <summary>
    /// Create a new custom trigger rule
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - Temperature drops below 20°C: DeviceId="sensor_front_room", Metric="temperature", Operator="LessThan", Threshold=20
    /// - Signal quality below 20: DeviceId="light_bedroom", Metric="linkquality", Operator="LessThan", Threshold=20
    /// - Battery below 10%: DeviceId="sensor_door", Metric="battery", Operator="LessThan", Threshold=10
    /// - Humidity above 70%: DeviceId="sensor_bathroom", Metric="humidity", Operator="GreaterThan", Threshold=70
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CustomTriggerRule), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomTriggerRule>> CreateCustomTrigger([FromBody] CreateCustomTriggerRequest request)
    {
        try
        {
            var trigger = await customTriggerService.CreateCustomTriggerAsync(request);
            return CreatedAtAction(nameof(GetCustomTrigger), new { id = trigger.Id }, trigger);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
    
    /// <summary>
    /// Update an existing custom trigger rule
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomTriggerRule>> UpdateCustomTrigger(Guid id, [FromBody] UpdateCustomTriggerRequest request)
    {
        try
        {
            var trigger = await customTriggerService.UpdateCustomTriggerAsync(id, request);
            return trigger;
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
    
    /// <summary>
    /// Delete a custom trigger rule
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCustomTrigger(Guid id)
    {
        try
        {
            await customTriggerService.DeleteCustomTriggerAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
    
    /// <summary>
    /// Enable or disable a custom trigger rule
    /// </summary>
    [HttpPatch("{id:guid}/toggle")]
    public async Task<ActionResult<CustomTriggerRule>> ToggleCustomTrigger(Guid id, [FromQuery] bool enabled)
    {
        try
        {
            var trigger = await customTriggerService.ToggleCustomTriggerAsync(id, enabled);
            return trigger;
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
    
    // ===== Execution Logs =====
    
    /// <summary>
    /// Get custom trigger execution logs
    /// </summary>
    [HttpGet("logs")]
    public async Task<List<CustomTriggerLog>> GetExecutionLogs(
        [FromQuery] Guid? customTriggerRuleId = null,
        [FromQuery] int take = 100)
    {
        return await customTriggerService.GetExecutionLogsAsync(customTriggerRuleId, take);
    }
}
