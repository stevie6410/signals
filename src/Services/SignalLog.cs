using Signals.Models;

namespace Signals.Services;

public class SignalLog
{
    private readonly List<SignalEvent> _events = new();
    private readonly object _lock = new();

    public IReadOnlyList<SignalEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return _events
                    .OrderByDescending(e => e.TimestampUtc)
                    .ToList();
            }
        }
    }

    public void Add(SignalEvent signalEvent)
    {
        lock (_lock)
        {
            _events.Add(signalEvent);
        }
    }
}
