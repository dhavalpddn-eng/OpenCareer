using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed class FlightAirframeConsequenceCoordinator(
    IAirframeStore airframes, IFlightAirframeConsequenceStore consequences, TimeProvider clock)
{
    public async Task<FlightAirframeApplyResult?> ApplyAsync(FlightSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.AircraftIdentity?.PhysicalAirframeId is not { } id) return null;
        var current = await airframes.FindAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Assigned physical airframe is missing; terminal evidence must be retained.");
        current.Validate();
        if (current.Airframe.AirframeId != id || current.Airframe.CanonicalAircraftId != session.AircraftIdentity.CanonicalAircraftId)
            throw new InvalidOperationException("Terminal FlightSession and physical airframe identities do not match.");
        var decision = FlightAirframeConsequenceCalculator.Calculate(session);
        DateTimeOffset appliedAt = clock.GetUtcNow();
        if (appliedAt < session.UpdatedAt) appliedAt = session.UpdatedAt;
        if (appliedAt < current.SavedAt) appliedAt = current.SavedAt;
        return await consequences.ApplyAsync(decision, current, appliedAt, cancellationToken).ConfigureAwait(false);
    }
}
