using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SDHome.Lib.Models;

namespace SDHome.Lib.Data.Entities;

/// <summary>
/// Custom trigger rule entity - evaluates sensor readings and fires TriggerEvents
/// </summary>
[Table("custom_trigger_rules")]
public class CustomTriggerRuleEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Required]
    [MaxLength(50)]
    [Column("trigger_type")]
    public string TriggerType { get; set; } = "";

    [Required]
    [MaxLength(200)]
    [Column("device_id")]
    public string DeviceId { get; set; } = "";

    [Required]
    [MaxLength(100)]
    [Column("metric")]
    public string Metric { get; set; } = "";

    [Required]
    [MaxLength(50)]
    [Column("operator")]
    public string Operator { get; set; } = "";

    [Column("threshold")]
    public double Threshold { get; set; }

    [Column("threshold2")]
    public double? Threshold2 { get; set; }

    [Column("cooldown_seconds")]
    public int? CooldownSeconds { get; set; }

    [Column("last_fired_utc")]
    public DateTime? LastFiredUtc { get; set; }

    [Column("created_utc")]
    public DateTime CreatedUtc { get; set; }

    [Column("updated_utc")]
    public DateTime UpdatedUtc { get; set; }

    // Navigation properties
    public ICollection<CustomTriggerLogEntity> Logs { get; set; } = [];

    // Conversion methods
    public static CustomTriggerRuleEntity FromModel(CustomTriggerRule model)
    {
        return new CustomTriggerRuleEntity
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            Enabled = model.Enabled,
            TriggerType = model.TriggerType.ToString(),
            DeviceId = model.DeviceId,
            Metric = model.Metric,
            Operator = model.Operator.ToString(),
            Threshold = model.Threshold,
            Threshold2 = model.Threshold2,
            CooldownSeconds = model.CooldownSeconds,
            LastFiredUtc = model.LastFiredUtc,
            CreatedUtc = model.CreatedUtc,
            UpdatedUtc = model.UpdatedUtc
        };
    }

    public CustomTriggerRule ToModel()
    {
        return new CustomTriggerRule(
            Id: Id,
            Name: Name,
            Description: Description,
            Enabled: Enabled,
            TriggerType: Enum.Parse<CustomTriggerType>(TriggerType),
            DeviceId: DeviceId,
            Metric: Metric,
            Operator: Enum.Parse<ThresholdOperator>(Operator),
            Threshold: Threshold,
            Threshold2: Threshold2,
            CooldownSeconds: CooldownSeconds,
            LastFiredUtc: LastFiredUtc,
            CreatedUtc: CreatedUtc,
            UpdatedUtc: UpdatedUtc
        );
    }
}

/// <summary>
/// Custom trigger execution log entity
/// </summary>
[Table("custom_trigger_logs")]
public class CustomTriggerLogEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("custom_trigger_rule_id")]
    public Guid CustomTriggerRuleId { get; set; }

    [Column("fired_utc")]
    public DateTime FiredUtc { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("device_id")]
    public string DeviceId { get; set; } = "";

    [Required]
    [MaxLength(100)]
    [Column("metric")]
    public string Metric { get; set; } = "";

    [Column("value")]
    public double Value { get; set; }

    [Required]
    [MaxLength(500)]
    [Column("condition")]
    public string Condition { get; set; } = "";

    [Column("generated_trigger_event_id")]
    public Guid? GeneratedTriggerEventId { get; set; }

    // Navigation properties
    public CustomTriggerRuleEntity CustomTriggerRule { get; set; } = null!;

    // Conversion methods
    public static CustomTriggerLogEntity FromModel(CustomTriggerLog model)
    {
        return new CustomTriggerLogEntity
        {
            Id = model.Id,
            CustomTriggerRuleId = model.CustomTriggerRuleId,
            FiredUtc = model.FiredUtc,
            DeviceId = model.DeviceId,
            Metric = model.Metric,
            Value = model.Value,
            Condition = model.Condition,
            GeneratedTriggerEventId = model.GeneratedTriggerEventId
        };
    }

    public CustomTriggerLog ToModel()
    {
        return new CustomTriggerLog(
            Id: Id,
            CustomTriggerRuleId: CustomTriggerRuleId,
            FiredUtc: FiredUtc,
            DeviceId: DeviceId,
            Metric: Metric,
            Value: Value,
            Condition: Condition,
            GeneratedTriggerEventId: GeneratedTriggerEventId
        );
    }
}
