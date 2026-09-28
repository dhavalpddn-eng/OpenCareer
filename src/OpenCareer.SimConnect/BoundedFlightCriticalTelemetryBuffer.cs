using OpenCareer.Domain.Telemetry;

namespace OpenCareer.SimConnect;

internal sealed class BoundedFlightCriticalTelemetryBuffer
{
    private readonly object _gate = new();
    private readonly Queue<AircraftTelemetrySnapshot> _samples = new();
    private readonly int _capacity;
    private DateTimeOffset? _lastAcceptedTimestamp;

    public BoundedFlightCriticalTelemetryBuffer(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public void Add(AircraftTelemetrySnapshot sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_gate)
        {
            if (_lastAcceptedTimestamp is { } last
                && sample.Timestamp <= last)
            {
                return;
            }

            _samples.Enqueue(sample);
            _lastAcceptedTimestamp = sample.Timestamp;

            while (_samples.Count > _capacity)
                _samples.Dequeue();
        }
    }

    public IReadOnlyList<AircraftTelemetrySnapshot> ReadAfter(
        DateTimeOffset? exclusiveTimestamp)
    {
        lock (_gate)
        {
            return _samples
                .Where(sample =>
                    exclusiveTimestamp is null
                    || sample.Timestamp > exclusiveTimestamp.Value)
                .ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _samples.Clear();
            _lastAcceptedTimestamp = null;
        }
    }
}
