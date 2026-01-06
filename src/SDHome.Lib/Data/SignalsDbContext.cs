using Microsoft.EntityFrameworkCore;
using SDHome.Lib.Data.Entities;

namespace SDHome.Lib.Data;

public class SignalsDbContext(DbContextOptions<SignalsDbContext> options) : DbContext(options)
{
    public DbSet<SignalEventEntity> SignalEvents => Set<SignalEventEntity>();
    public DbSet<SensorReadingEntity> SensorReadings => Set<SensorReadingEntity>();
    public DbSet<TriggerEventEntity> TriggerEvents => Set<TriggerEventEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<ZoneEntity> Zones => Set<ZoneEntity>();
    public DbSet<ZoneCapabilityAssignmentEntity> ZoneCapabilityAssignments => Set<ZoneCapabilityAssignmentEntity>();

    // Automation entities
    public DbSet<AutomationRuleEntity> AutomationRules => Set<AutomationRuleEntity>();
    public DbSet<AutomationTriggerEntity> AutomationTriggers => Set<AutomationTriggerEntity>();
    public DbSet<AutomationConditionEntity> AutomationConditions => Set<AutomationConditionEntity>();
    public DbSet<AutomationActionEntity> AutomationActions => Set<AutomationActionEntity>();
    public DbSet<AutomationExecutionLogEntity> AutomationExecutionLogs => Set<AutomationExecutionLogEntity>();
    public DbSet<SceneEntity> Scenes => Set<SceneEntity>();

    // Settings entities
    public DbSet<CapabilityMappingEntity> CapabilityMappings => Set<CapabilityMappingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SignalEvent configuration
        modelBuilder.Entity<SignalEventEntity>(entity =>
        {
            entity.ToTable("signal_events");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TimestampUtc).HasColumnName("timestamp_utc").HasColumnType("datetime2(7)");
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(200);
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(200);
            entity.Property(e => e.Location).HasColumnName("location").HasMaxLength(200);
            entity.Property(e => e.Capability).HasColumnName("capability").HasMaxLength(200);
            entity.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(200);
            entity.Property(e => e.EventSubType).HasColumnName("event_sub_type").HasMaxLength(200);
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.RawTopic).HasColumnName("raw_topic").HasMaxLength(4000);
            entity.Property(e => e.RawPayload).HasColumnName("raw_payload").HasColumnType("nvarchar(max)");
            entity.Property(e => e.DeviceKind).HasColumnName("device_kind").HasMaxLength(50);
            entity.Property(e => e.EventCategory).HasColumnName("event_category").HasMaxLength(50);

            entity.HasIndex(e => new { e.DeviceId, e.TimestampUtc })
                .HasDatabaseName("ix_signal_events_device_timestamp")
                .IsDescending(false, true);

            entity.HasIndex(e => e.TimestampUtc)
                .HasDatabaseName("ix_signal_events_timestamp")
                .IsDescending(true);
        });

        // SensorReading configuration
        modelBuilder.Entity<SensorReadingEntity>(entity =>
        {
            entity.ToTable("sensor_readings");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SignalEventId).HasColumnName("signal_event_id");
            entity.Property(e => e.TimestampUtc).HasColumnName("timestamp_utc").HasColumnType("datetime2(7)");
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(200);
            entity.Property(e => e.Metric).HasColumnName("metric").HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(50);

            entity.HasIndex(e => new { e.DeviceId, e.Metric, e.TimestampUtc })
                .HasDatabaseName("ix_sensor_readings_device_metric_ts")
                .IsDescending(false, false, true);

            entity.HasIndex(e => new { e.Metric, e.TimestampUtc })
                .HasDatabaseName("ix_sensor_readings_metric_ts")
                .IsDescending(false, true);
        });

        // TriggerEvent configuration
        modelBuilder.Entity<TriggerEventEntity>(entity =>
        {
            entity.ToTable("trigger_events");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SignalEventId).HasColumnName("signal_event_id");
            entity.Property(e => e.TimestampUtc).HasColumnName("timestamp_utc").HasColumnType("datetime2(7)");
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(200);
            entity.Property(e => e.Capability).HasColumnName("capability").HasMaxLength(200);
            entity.Property(e => e.TriggerType).HasColumnName("trigger_type").HasMaxLength(100);
            entity.Property(e => e.TriggerSubType).HasColumnName("trigger_sub_type").HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnName("value_bit");

            entity.HasIndex(e => new { e.DeviceId, e.TimestampUtc })
                .HasDatabaseName("ix_trigger_events_device_timestamp")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.TriggerType, e.TimestampUtc })
                .HasDatabaseName("ix_trigger_events_type_timestamp")
                .IsDescending(false, true);
        });

        // Device configuration
        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(e => e.DeviceId);

            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
            entity.Property(e => e.FriendlyName).HasColumnName("friendly_name").HasMaxLength(255);
            entity.Property(e => e.IeeeAddress).HasColumnName("ieee_address").HasMaxLength(255);
            entity.Property(e => e.ModelId).HasColumnName("model_id").HasMaxLength(255);
            entity.Property(e => e.Manufacturer).HasColumnName("manufacturer").HasMaxLength(255);
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("nvarchar(max)");
            entity.Property(e => e.PowerSource).HasColumnName("power_source");
            entity.Property(e => e.DeviceType).HasColumnName("device_type").HasMaxLength(50);
            entity.Property(e => e.Room).HasColumnName("room").HasMaxLength(255);
            entity.Property(e => e.Capabilities).HasColumnName("capabilities").HasColumnType("nvarchar(max)");
            entity.Property(e => e.Attributes).HasColumnName("attributes").HasColumnType("nvarchar(max)");
            entity.Property(e => e.LastSeen).HasColumnName("last_seen").HasColumnType("datetime2");
            entity.Property(e => e.IsAvailable).HasColumnName("is_available");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2");

            entity.HasIndex(e => e.Room).HasDatabaseName("idx_devices_room");
            entity.HasIndex(e => e.DeviceType).HasDatabaseName("idx_devices_device_type");
            entity.HasIndex(e => e.IsAvailable).HasDatabaseName("idx_devices_is_available");

            // Zone relationship
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");
            entity.HasIndex(e => e.ZoneId).HasDatabaseName("idx_devices_zone_id");
            entity.HasOne(e => e.Zone)
                .WithMany(z => z.Devices)
                .HasForeignKey(e => e.ZoneId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Zone configuration
        modelBuilder.Entity<ZoneEntity>(entity =>
        {
            entity.ToTable("zones");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(100);
            entity.Property(e => e.Color).HasColumnName("color").HasMaxLength(50);
            entity.Property(e => e.ParentZoneId).HasColumnName("parent_zone_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2");

            // Self-referencing relationship for hierarchy
            entity.HasOne(e => e.ParentZone)
                .WithMany(e => e.ChildZones)
                .HasForeignKey(e => e.ParentZoneId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.ParentZoneId).HasDatabaseName("idx_zones_parent_zone_id");
            entity.HasIndex(e => e.Name).HasDatabaseName("idx_zones_name");
        });

        // ZoneCapabilityAssignment configuration
        modelBuilder.Entity<ZoneCapabilityAssignmentEntity>(entity =>
        {
            entity.ToTable("zone_capability_assignments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");
            entity.Property(e => e.Capability).HasColumnName("capability").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Property).HasColumnName("property").HasMaxLength(255);
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2");

            entity.HasOne(e => e.Zone)
                .WithMany(z => z.CapabilityAssignments)
                .HasForeignKey(e => e.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Device)
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique constraint: one capability per zone per priority
            entity.HasIndex(e => new { e.ZoneId, e.Capability, e.Priority })
                .HasDatabaseName("idx_zone_capability_assignments_unique")
                .IsUnique();

            entity.HasIndex(e => e.ZoneId).HasDatabaseName("idx_zone_capability_assignments_zone_id");
            entity.HasIndex(e => e.DeviceId).HasDatabaseName("idx_zone_capability_assignments_device_id");
            entity.HasIndex(e => e.Capability).HasDatabaseName("idx_zone_capability_assignments_capability");
        });

        // ===== AUTOMATION ENTITIES =====

        // AutomationRule configuration
        modelBuilder.Entity<AutomationRuleEntity>(entity =>
        {
            entity.ToTable("automation_rules");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(100);
            entity.Property(e => e.Color).HasColumnName("color").HasMaxLength(50);
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.TriggerMode).HasColumnName("trigger_mode").HasMaxLength(50);
            entity.Property(e => e.ConditionMode).HasColumnName("condition_mode").HasMaxLength(50);
            entity.Property(e => e.CooldownSeconds).HasColumnName("cooldown_seconds");
            entity.Property(e => e.LastTriggeredAt).HasColumnName("last_triggered_at").HasColumnType("datetime2");
            entity.Property(e => e.ExecutionCount).HasColumnName("execution_count");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2");

            entity.HasIndex(e => e.IsEnabled).HasDatabaseName("idx_automation_rules_enabled");
            entity.HasIndex(e => e.Name).HasDatabaseName("idx_automation_rules_name");
        });

        // AutomationTrigger configuration
        modelBuilder.Entity<AutomationTriggerEntity>(entity =>
        {
            entity.ToTable("automation_triggers");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AutomationRuleId).HasColumnName("automation_rule_id");
            entity.Property(e => e.TriggerType).HasColumnName("trigger_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
            entity.Property(e => e.Property).HasColumnName("property").HasMaxLength(255);
            entity.Property(e => e.Operator).HasColumnName("operator").HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("nvarchar(max)");
            entity.Property(e => e.TimeExpression).HasColumnName("time_expression").HasMaxLength(255);
            entity.Property(e => e.SunEvent).HasColumnName("sun_event").HasMaxLength(50);
            entity.Property(e => e.OffsetMinutes).HasColumnName("offset_minutes");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");

            entity.HasOne(e => e.AutomationRule)
                .WithMany(r => r.Triggers)
                .HasForeignKey(e => e.AutomationRuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.AutomationRuleId).HasDatabaseName("idx_automation_triggers_rule_id");
            entity.HasIndex(e => e.DeviceId).HasDatabaseName("idx_automation_triggers_device_id");
            entity.HasIndex(e => e.TriggerType).HasDatabaseName("idx_automation_triggers_type");
        });

        // AutomationCondition configuration
        modelBuilder.Entity<AutomationConditionEntity>(entity =>
        {
            entity.ToTable("automation_conditions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AutomationRuleId).HasColumnName("automation_rule_id");
            entity.Property(e => e.ConditionType).HasColumnName("condition_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
            entity.Property(e => e.Property).HasColumnName("property").HasMaxLength(255);
            entity.Property(e => e.Operator).HasColumnName("operator").HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("nvarchar(max)");
            entity.Property(e => e.Value2).HasColumnName("value2").HasColumnType("nvarchar(max)");
            entity.Property(e => e.TimeStart).HasColumnName("time_start").HasMaxLength(10);
            entity.Property(e => e.TimeEnd).HasColumnName("time_end").HasMaxLength(10);
            entity.Property(e => e.DaysOfWeek).HasColumnName("days_of_week").HasMaxLength(50);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");

            entity.HasOne(e => e.AutomationRule)
                .WithMany(r => r.Conditions)
                .HasForeignKey(e => e.AutomationRuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.AutomationRuleId).HasDatabaseName("idx_automation_conditions_rule_id");
        });

        // AutomationAction configuration
        modelBuilder.Entity<AutomationActionEntity>(entity =>
        {
            entity.ToTable("automation_actions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AutomationRuleId).HasColumnName("automation_rule_id");
            entity.Property(e => e.ActionType).HasColumnName("action_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasMaxLength(255);
            entity.Property(e => e.Property).HasColumnName("property").HasMaxLength(255);
            entity.Property(e => e.Value).HasColumnName("value").HasColumnType("nvarchar(max)");
            entity.Property(e => e.DelaySeconds).HasColumnName("delay_seconds");
            entity.Property(e => e.WebhookUrl).HasColumnName("webhook_url").HasMaxLength(2000);
            entity.Property(e => e.WebhookMethod).HasColumnName("webhook_method").HasMaxLength(20);
            entity.Property(e => e.WebhookBody).HasColumnName("webhook_body").HasColumnType("nvarchar(max)");
            entity.Property(e => e.NotificationTitle).HasColumnName("notification_title").HasMaxLength(255);
            entity.Property(e => e.NotificationMessage).HasColumnName("notification_message").HasMaxLength(2000);
            entity.Property(e => e.SceneId).HasColumnName("scene_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");

            entity.HasOne(e => e.AutomationRule)
                .WithMany(r => r.Actions)
                .HasForeignKey(e => e.AutomationRuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.AutomationRuleId).HasDatabaseName("idx_automation_actions_rule_id");
            entity.HasIndex(e => e.DeviceId).HasDatabaseName("idx_automation_actions_device_id");
        });

        // AutomationExecutionLog configuration
        modelBuilder.Entity<AutomationExecutionLogEntity>(entity =>
        {
            entity.ToTable("automation_execution_logs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AutomationRuleId).HasColumnName("automation_rule_id");
            entity.Property(e => e.ExecutedAt).HasColumnName("executed_at").HasColumnType("datetime2");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50);
            entity.Property(e => e.TriggerSource).HasColumnName("trigger_source").HasColumnType("nvarchar(max)");
            entity.Property(e => e.ActionResults).HasColumnName("action_results").HasColumnType("nvarchar(max)");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("nvarchar(max)");

            entity.HasOne(e => e.AutomationRule)
                .WithMany(r => r.ExecutionLogs)
                .HasForeignKey(e => e.AutomationRuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.AutomationRuleId, e.ExecutedAt })
                .HasDatabaseName("idx_automation_logs_rule_executed")
                .IsDescending(false, true);
            entity.HasIndex(e => e.ExecutedAt)
                .HasDatabaseName("idx_automation_logs_executed")
                .IsDescending(true);
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_automation_logs_status");
        });

        // Scene configuration
        modelBuilder.Entity<SceneEntity>(entity =>
        {
            entity.ToTable("scenes");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(100);
            entity.Property(e => e.Color).HasColumnName("color").HasMaxLength(50);
            entity.Property(e => e.DeviceStates).HasColumnName("device_states").HasColumnType("nvarchar(max)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2");

            entity.HasIndex(e => e.Name).HasDatabaseName("idx_scenes_name");
        });

        // CapabilityMapping configuration
        modelBuilder.Entity<CapabilityMappingEntity>(entity =>
        {
            entity.ToTable("capability_mappings");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Capability).HasColumnName("capability").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DeviceType).HasColumnName("device_type").HasMaxLength(100);
            entity.Property(e => e.Property).HasColumnName("property").HasMaxLength(100).IsRequired();
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(50);
            entity.Property(e => e.StateMappings).HasColumnName("state_mappings").HasColumnType("nvarchar(max)");
            entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(20);
            entity.Property(e => e.IsSystemDefault).HasColumnName("is_system_default");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2");

            entity.HasIndex(e => e.Capability).HasDatabaseName("idx_capability_mappings_capability");
            entity.HasIndex(e => new { e.Capability, e.DeviceType }).HasDatabaseName("idx_capability_mappings_cap_device");
        });
    }
}
