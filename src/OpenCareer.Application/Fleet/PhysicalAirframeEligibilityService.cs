using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public enum PhysicalAirframeEligibilityStatus
{
    Eligible = 0,
    AuthorityUnavailable = 1,
    Missing = 2,
    IdentityMismatch = 3,
    ModelMismatch = 4,
    Grounded = 5
}

public sealed record PhysicalAirframeEligibility(
    PhysicalAirframeEligibilityStatus Status,
    string Detail)
{
    public bool IsEligible => Status == PhysicalAirframeEligibilityStatus.Eligible;
}

/// <summary>
/// Read-only eligibility for an explicit physical assignment. Does not select/create airframes,
/// infer grounding from wear, or replace the existing model-level dispatch/reservation authority.
/// </summary>
public sealed class PhysicalAirframeEligibilityService(IAirframeStore? airframes = null)
{
    public async Task<PhysicalAirframeEligibility> EvaluateAsync(
        string canonicalAircraftId,
        AirframeId? physicalAirframeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Model-only operation requires no physical store or inferred assignment.
        if (physicalAirframeId is not { } requestedId)
            return new(PhysicalAirframeEligibilityStatus.Eligible, "No physical airframe assignment; model-level dispatch applies.");

        requestedId.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        if (airframes is null)
            return new(PhysicalAirframeEligibilityStatus.AuthorityUnavailable, "Physical aircraft eligibility requires the authoritative airframe store.");

        AirframeStoreRecord? retained = await airframes.FindAsync(requestedId, cancellationToken).ConfigureAwait(false);
        if (retained is null)
            return new(PhysicalAirframeEligibilityStatus.Missing, $"The explicitly selected physical airframe {requestedId} does not exist.");
        retained.Validate();
        if (retained.Airframe.AirframeId != requestedId)
            return new(PhysicalAirframeEligibilityStatus.IdentityMismatch, $"The airframe store returned a different physical identity for {requestedId}.");
        if (!string.Equals(retained.Airframe.CanonicalAircraftId, canonicalAircraftId, StringComparison.Ordinal))
            return new(PhysicalAirframeEligibilityStatus.ModelMismatch, $"Physical airframe {requestedId} does not match canonical aircraft '{canonicalAircraftId}'.");
        if (retained.Condition.RequiresGrounding)
            return new(PhysicalAirframeEligibilityStatus.Grounded, $"Physical airframe {requestedId} is grounded and unavailable for dispatch/start.");

        return new(PhysicalAirframeEligibilityStatus.Eligible, $"Physical airframe {requestedId} matches the model and does not require grounding.");
    }

    public async Task RequireEligibleAsync(string canonicalAircraftId, AirframeId? physicalAirframeId,
        CancellationToken cancellationToken = default)
    {
        PhysicalAirframeEligibility result = await EvaluateAsync(canonicalAircraftId, physicalAirframeId, cancellationToken).ConfigureAwait(false);
        if (!result.IsEligible) throw new InvalidOperationException(result.Detail);
    }
}
