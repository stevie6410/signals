using SDHome.Lib.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SDHome.Lib.Models;

// ===== Enums =====

public enum CustomTriggerType
{
    SensorThreshold,      // When sensor value meets threshold (temp < 20, battery < 10, etc.)
    SensorChange,         // When sensor value changes by amount
    MetricUnavailable,    // When expected sensor reading stops arriving
    SignalQuality         // When signal quality (linkquality, RSSI) falls below threshold
}

public enum ThresholdOperator
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equals,
    NotEquals,
    Between
}

// ===== Domain Records =====

/// <summary>
/// Custom trigger rule that evaluates sensor readings and fires TriggerEvents when conditions are met
/// </summary>
public sealed record CustomTriggerRule(
    Guid Id,
    string Name,
    string? Description,
    bool Enabled,
    CustomTriggerType TriggerType,
    string DeviceId,
    string Metric,
    ThresholdOperator Operator,
    double Threshold,
    double? Threshold2,
    int? CooldownSeconds,
    DateTime? LastFiredUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc
);

/// <summary>
/// Log of custom trigger executions
/// </summary>
public sealed record CustomTriggerLog(
    Guid Id,
    Guid CustomTriggerRuleId,
    DateTime FiredUtc,
    string DeviceId,
    string Metric,
    double Value,
    string Condition,
    Guid? GeneratedTriggerEventId
);

// ===== Request/Response DTOs =====

public sealed record CreateCustomTriggerRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;
    
    public string? Description { get; init; }
    
    public bool Enabled { get; init; } = true;
    
    [Required]
    public CustomTriggerType TriggerType { get; init; }
    
    [Required]
    public string DeviceId { get; init; } = string.Empty;
    
    [Required]
    public string Metric { get; init; } = string.Empty;
    
    [Required]
    public ThresholdOperator Operator { get; init; }
    
    [Required]
    public double Threshold { get; init; }
    
    public double? Threshold2 { get; init; }
    
    public int? CooldownSeconds { get; init; }
}

public sealed record UpdateCustomTriggerRequest
{
    [Required]
    public string Name { get; init; } = string.Empty;
    
    public string? Description { get; init; }
    
    public bool Enabled { get; init; } = true;
    
    [Required]
    public CustomTriggerType TriggerType { get; init; }
    
    [Required]
    public string DeviceId { get; init; } = string.Empty;
    
    [Required]
    public string Metric { get; init; } = string.Empty;
    
    [Required]
    public ThresholdOperator Operator { get; init; }
    
    [Required]
    public double Threshold { get; init; }
    
    public double? Threshold2 { get; init; }
    
    public int? CooldownSeconds { get; init; }
}

public sealed record CustomTriggerSummary(
    Guid Id,
    string Name,
    bool Enabled,
    string DeviceId,
    string Metric,
    string Condition,
    DateTime? LastFiredUtc,
    int ExecutionCount
);
