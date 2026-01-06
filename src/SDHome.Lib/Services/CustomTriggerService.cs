using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SDHome.Lib.Data;
using SDHome.Lib.Data.Entities;
using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public interface ICustomTriggerService
{
    // Rule management
    Task<List<CustomTriggerSummary>> GetCustomTriggerSummariesAsync();
    Task<CustomTriggerRule?> GetCustomTriggerAsync(Guid id);
    Task<CustomTriggerRule> CreateCustomTriggerAsync(CreateCustomTriggerRequest request);
    Task<CustomTriggerRule> UpdateCustomTriggerAsync(Guid id, UpdateCustomTriggerRequest request);
    Task DeleteCustomTriggerAsync(Guid id);
    Task<CustomTriggerRule> ToggleCustomTriggerAsync(Guid id, bool enabled);
    
    // Execution logs
    Task<List<CustomTriggerLog>> GetExecutionLogsAsync(Guid? customTriggerRuleId = null, int take = 100);
    
    // Evaluation (called by SignalEventProjectionService)
    Task<List<TriggerEvent>> EvaluateSensorReadingsAsync(List<SensorReading> readings);
}

public class CustomTriggerService(
    SignalsDbContext db,
    ILogger<CustomTriggerService> logger) : ICustomTriggerService
{
    // ===== Rule Management =====
    
    public async Task<List<CustomTriggerSummary>> GetCustomTriggerSummariesAsync()
    {
        var rules = await db.Set<CustomTriggerRuleEntity>()
            .AsNoTracking()
            .Include(r => r.Logs)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return rules.Select(r => new CustomTriggerSummary(
            Id: r.Id,
            Name: r.Name,
            Enabled: r.Enabled,
            DeviceId: r.DeviceId,
            Metric: r.Metric,
            Condition: FormatCondition(r),
            LastFiredUtc: r.LastFiredUtc,
            ExecutionCount: r.Logs.Count
        )).ToList();
    }

    public async Task<CustomTriggerRule?> GetCustomTriggerAsync(Guid id)
    {
        var entity = await db.Set<CustomTriggerRuleEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        return entity?.ToModel();
    }

    public async Task<CustomTriggerRule> CreateCustomTriggerAsync(CreateCustomTriggerRequest request)
    {
        var now = DateTime.UtcNow;
        var rule = new CustomTriggerRule(
            Id: Guid.NewGuid(),
            Name: request.Name,
            Description: request.Description,
            Enabled: request.Enabled,
            TriggerType: request.TriggerType,
            DeviceId: request.DeviceId,
            Metric: request.Metric.ToLowerInvariant(),
            Operator: request.Operator,
            Threshold: request.Threshold,
            Threshold2: request.Threshold2,
            CooldownSeconds: request.CooldownSeconds,
            LastFiredUtc: null,
            CreatedUtc: now,
            UpdatedUtc: now
        );

        var entity = CustomTriggerRuleEntity.FromModel(rule);
        db.Set<CustomTriggerRuleEntity>().Add(entity);
        await db.SaveChangesAsync();

        logger.LogInformation("Created custom trigger rule {Id}: {Name}", rule.Id, rule.Name);
        return rule;
    }

    public async Task<CustomTriggerRule> UpdateCustomTriggerAsync(Guid id, UpdateCustomTriggerRequest request)
    {
        var entity = await db.Set<CustomTriggerRuleEntity>()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (entity == null)
            throw new InvalidOperationException($"Custom trigger rule {id} not found");

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.Enabled = request.Enabled;
        entity.TriggerType = request.TriggerType.ToString();
        entity.DeviceId = request.DeviceId;
        entity.Metric = request.Metric.ToLowerInvariant();
        entity.Operator = request.Operator.ToString();
        entity.Threshold = request.Threshold;
        entity.Threshold2 = request.Threshold2;
        entity.CooldownSeconds = request.CooldownSeconds;
        entity.UpdatedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        logger.LogInformation("Updated custom trigger rule {Id}: {Name}", id, entity.Name);
        return entity.ToModel();
    }

    public async Task DeleteCustomTriggerAsync(Guid id)
    {
        var entity = await db.Set<CustomTriggerRuleEntity>()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (entity == null)
            throw new InvalidOperationException($"Custom trigger rule {id} not found");

        db.Set<CustomTriggerRuleEntity>().Remove(entity);
        await db.SaveChangesAsync();

        logger.LogInformation("Deleted custom trigger rule {Id}: {Name}", id, entity.Name);
    }

    public async Task<CustomTriggerRule> ToggleCustomTriggerAsync(Guid id, bool enabled)
    {
        var entity = await db.Set<CustomTriggerRuleEntity>()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (entity == null)
            throw new InvalidOperationException($"Custom trigger rule {id} not found");

        entity.Enabled = enabled;
        entity.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Toggled custom trigger rule {Id} to {Enabled}", id, enabled);
        return entity.ToModel();
    }

    // ===== Execution Logs =====
    
    public async Task<List<CustomTriggerLog>> GetExecutionLogsAsync(Guid? customTriggerRuleId = null, int take = 100)
    {
        var query = db.Set<CustomTriggerLogEntity>()
            .AsNoTracking()
            .OrderByDescending(l => l.FiredUtc)
            .Take(take);

        if (customTriggerRuleId.HasValue)
        {
            query = (IOrderedQueryable<CustomTriggerLogEntity>)query.Where(l => l.CustomTriggerRuleId == customTriggerRuleId.Value);
        }

        var entities = await query.ToListAsync();
        return entities.Select(e => e.ToModel()).ToList();
    }

    // ===== Evaluation =====
    
    public async Task<List<TriggerEvent>> EvaluateSensorReadingsAsync(List<SensorReading> readings)
    {
        if (!readings.Any())
            return [];

        // Get all enabled custom trigger rules
        var rules = await db.Set<CustomTriggerRuleEntity>()
            .Where(r => r.Enabled)
            .ToListAsync();

        if (!rules.Any())
            return [];

        var generatedTriggers = new List<TriggerEvent>();
        var now = DateTime.UtcNow;

        foreach (var reading in readings)
        {
            // Find matching rules for this device and metric
            var matchingRules = rules.Where(r => 
                r.DeviceId == reading.DeviceId && 
                r.Metric.Equals(reading.Metric, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            foreach (var rule in matchingRules)
            {
                // Check cooldown
                if (rule.CooldownSeconds.HasValue && rule.LastFiredUtc.HasValue)
                {
                    var cooldownExpiry = rule.LastFiredUtc.Value.AddSeconds(rule.CooldownSeconds.Value);
                    if (now < cooldownExpiry)
                    {
                        logger.LogDebug("Custom trigger {RuleId} in cooldown until {Expiry}", rule.Id, cooldownExpiry);
                        continue;
                    }
                }

                // Evaluate condition
                var conditionMet = EvaluateCondition(rule, reading.Value);

                if (conditionMet)
                {
                    // Create trigger event
                    var triggerEvent = new TriggerEvent(
                        Id: Guid.NewGuid(),
                        SignalEventId: reading.SignalEventId,
                        TimestampUtc: reading.TimestampUtc,
                        DeviceId: reading.DeviceId,
                        Capability: reading.Metric,
                        TriggerType: "custom",
                        TriggerSubType: rule.Name.ToLowerInvariant().Replace(" ", "_"),
                        Value: true
                    );

                    generatedTriggers.Add(triggerEvent);

                    // Update last fired time
                    rule.LastFiredUtc = now;

                    // Log execution
                    var log = new CustomTriggerLogEntity
                    {
                        Id = Guid.NewGuid(),
                        CustomTriggerRuleId = rule.Id,
                        FiredUtc = now,
                        DeviceId = reading.DeviceId,
                        Metric = reading.Metric,
                        Value = reading.Value,
                        Condition = FormatCondition(rule),
                        GeneratedTriggerEventId = triggerEvent.Id
                    };

                    db.Set<CustomTriggerLogEntity>().Add(log);

                    logger.LogInformation(
                        "Custom trigger fired: {RuleName} - {DeviceId} {Metric} = {Value} ({Condition})",
                        rule.Name, reading.DeviceId, reading.Metric, reading.Value, FormatCondition(rule));
                }
            }
        }

        if (generatedTriggers.Any())
        {
            await db.SaveChangesAsync();
        }

        return generatedTriggers;
    }

    // ===== Helper Methods =====
    
    private static bool EvaluateCondition(CustomTriggerRuleEntity rule, double value)
    {
        var op = Enum.Parse<ThresholdOperator>(rule.Operator);

        return op switch
        {
            ThresholdOperator.GreaterThan => value > rule.Threshold,
            ThresholdOperator.GreaterThanOrEqual => value >= rule.Threshold,
            ThresholdOperator.LessThan => value < rule.Threshold,
            ThresholdOperator.LessThanOrEqual => value <= rule.Threshold,
            ThresholdOperator.Equals => Math.Abs(value - rule.Threshold) < 0.001,
            ThresholdOperator.NotEquals => Math.Abs(value - rule.Threshold) >= 0.001,
            ThresholdOperator.Between => rule.Threshold2.HasValue && 
                                         value >= rule.Threshold && 
                                         value <= rule.Threshold2.Value,
            _ => false
        };
    }

    private static string FormatCondition(CustomTriggerRuleEntity rule)
    {
        var op = Enum.Parse<ThresholdOperator>(rule.Operator);

        var operatorStr = op switch
        {
            ThresholdOperator.GreaterThan => ">",
            ThresholdOperator.GreaterThanOrEqual => "≥",
            ThresholdOperator.LessThan => "<",
            ThresholdOperator.LessThanOrEqual => "≤",
            ThresholdOperator.Equals => "=",
            ThresholdOperator.NotEquals => "≠",
            ThresholdOperator.Between => "between",
            _ => "?"
        };

        if (op == ThresholdOperator.Between && rule.Threshold2.HasValue)
        {
            return $"{rule.Metric} {operatorStr} {rule.Threshold} and {rule.Threshold2.Value}";
        }

        return $"{rule.Metric} {operatorStr} {rule.Threshold}";
    }
}
