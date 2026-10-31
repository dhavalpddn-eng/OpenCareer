using OpenCareer.Domain.Telemetry;

namespace OpenCareer.SimConnect;

internal sealed class BoundedFlightCriticalTelemetryBuffer
{
    private readonly object _gate = new();
    private readonly Queue<AircraftTelemetrySnapshot> _samples = new();
    private readonly int _capacity;
    private readonly TimeSpan _minimumSampleSpacing;
    private AircraftTelemetrySnapshot? _lastObserved;
    private DateTimeOffset? _lastAcceptedTimestamp;

    public BoundedFlightCriticalTelemetryBuffer(
        int capacity,
        TimeSpan minimumSampleSpacing = default)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        if (minimumSampleSpacing < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSampleSpacing));
        }

        _capacity = capacity;
        _minimumSampleSpacing = minimumSampleSpacing;
    }

    public bool Add(AircraftTelemetrySnapshot sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_gate)
        {
            AircraftTelemetrySnapshot? lastObserved = _lastObserved;

            if (lastObserved is not null
                && sample.Timestamp <= lastObserved.Timestamp)
            {
                return false;
            }

            bool priorityTransition =
                lastObserved is not null
                && (sample.OnGround != lastObserved.OnGround
                    || sample.Paused != lastObserved.Paused
                    || sample.SlewActive != lastObserved.SlewActive);

            _lastObserved = sample;

            if (!priorityTransition
                && _lastAcceptedTimestamp is { } lastAccepted
                && sample.Timestamp - lastAccepted
                    < _minimumSampleSpacing)
            {
                return false;
            }

            _samples.Enqueue(sample);
            _lastAcceptedTimestamp = sample.Timestamp;

            while (_samples.Count > _capacity)
                _samples.Dequeue();

            return true;
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
            _lastObserved = null;
            _lastAcceptedTimestamp = null;
        }
    }
}
