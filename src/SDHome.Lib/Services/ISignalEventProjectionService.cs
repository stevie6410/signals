using SDHome.Lib.Models;

namespace SDHome.Lib.Services;

public record ProjectedEventData(
    TriggerEvent? Trigger,
    IReadOnlyList<SensorReading> Readings,
    IReadOnlyList<TriggerEvent> CustomTriggers
);

/// <summary>
/// Pipeline timing context passed from SignalsService for E2E tracking
/// </summary>
public record PipelineContext(
    double ParseMs,
    double DatabaseMs,
    double BroadcastMs
);

public interface ISignalEventProjectionService
{
    Task<ProjectedEventData> ProjectAsync(
        SignalEvent ev, 
        CancellationToken cancellationToken = default,
        PipelineContext? pipelineContext = null);
}
