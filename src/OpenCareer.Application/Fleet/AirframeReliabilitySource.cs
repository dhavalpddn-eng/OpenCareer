using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum AirframeReliabilityReadStatus { Available, NotFound, ServiceStateUnavailable }
public sealed record AirframeReliabilityReadResult(AirframeId AirframeId, AirframeReliabilityReadStatus Status,
    AirframeReliabilityAssessment? Assessment);

/// <summary>Read-only physical reliability source; no inferred assignment or condition writes.</summary>
public sealed class AirframeReliabilitySource(IAirframeStore airframes, IAirframeServiceStateStore? serviceStates = null,
    TimeProvider? clock = null)
{
    public async Task<AirframeReliabilityReadResult> ReadAsync(AirframeId airframeId, CancellationToken cancellationToken = default)
    {
        airframeId.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var current = await airframes.FindAsync(airframeId, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(airframeId, AirframeReliabilityReadStatus.NotFound, null);
        current.Validate();
        if (current.Airframe.AirframeId != airframeId)
            throw new InvalidDataException("Reliability source received a different physical airframe.");
        var serviceStore = serviceStates ?? airframes as IAirframeServiceStateStore;
        if (serviceStore is null) return new(airframeId, AirframeReliabilityReadStatus.ServiceStateUnavailable, null);
        var service = await serviceStore.ReadServiceStateAsync(airframeId, cancellationToken).ConfigureAwait(false);
        if (service is null) return new(airframeId, AirframeReliabilityReadStatus.ServiceStateUnavailable, null);
        // Validate both inputs before publishing, then reject any concurrent condition/service change.
        var assessment = AirframeReliabilityAssessment.Evaluate(current, service, (clock ?? TimeProvider.System).GetUtcNow());
        if (service != await serviceStore.ReadServiceStateAsync(airframeId, cancellationToken).ConfigureAwait(false)
            || current != await airframes.FindAsync(airframeId, cancellationToken).ConfigureAwait(false))
            throw new AirframeConcurrencyException("Airframe condition/service state changed during reliability read; refresh the assessment.");
        return new(airframeId, AirframeReliabilityReadStatus.Available, assessment);
    }
}
