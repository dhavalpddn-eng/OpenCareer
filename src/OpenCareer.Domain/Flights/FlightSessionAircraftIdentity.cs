using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Flights;

/// <summary>
/// The dispatched canonical model and, only when explicitly assigned, its permanent physical identity.
/// A null physical identity is model-only operation, not an unknown canonical model.
/// </summary>
public sealed record FlightSessionAircraftIdentity
{
    public FlightSessionAircraftIdentity(string canonicalAircraftId, AirframeId? physicalAirframeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        if (canonicalAircraftId != canonicalAircraftId.Trim() || canonicalAircraftId.Any(char.IsControl))
            throw new ArgumentException("Canonical aircraft identity must be normalized and contain no control characters.", nameof(canonicalAircraftId));

        physicalAirframeId?.Validate();
        CanonicalAircraftId = canonicalAircraftId;
        PhysicalAirframeId = physicalAirframeId;
    }

    public string CanonicalAircraftId { get; }
    public AirframeId? PhysicalAirframeId { get; }
}
