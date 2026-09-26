using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Fleet;

// No Eligible outcome exists until a supported component failure model is implemented.
public enum AirframeFailureEligibilityStatus
{
    NoPhysicalAirframe,
    ReliabilityUnavailable,
    MaintenanceUnavailable,
    ComponentModelUnavailable
}

public sealed record AirframeFailureEligibility(AirframeId? PhysicalAirframeId, AirframeFailureEligibilityStatus Status,
    AirframeReliabilityReadStatus? ReadStatus, AirframeReliabilityAssessment? Assessment, string Detail)
{
    public bool MaintenanceGatePassed => Status == AirframeFailureEligibilityStatus.ComponentModelUnavailable;
    public bool CanGenerateFailure => false;
}

/// <summary>Future simulation boundary only. It cannot select, schedule or command any failure.</summary>
public sealed class AirframeFailureEligibilityService(AirframeReliabilitySource reliability)
{
    public async Task<AirframeFailureEligibility> EvaluateAsync(FlightSessionAircraftIdentity? identity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identity?.PhysicalAirframeId is not { } id)
            return new(null, AirframeFailureEligibilityStatus.NoPhysicalAirframe, null, null, "No explicit physical airframe assignment.");

        var result = await reliability.ReadAsync(id, cancellationToken).ConfigureAwait(false);
        if (result.Status != AirframeReliabilityReadStatus.Available || result.Assessment is not { } assessment)
            return new(id, AirframeFailureEligibilityStatus.ReliabilityUnavailable, result.Status, null, "Authoritative physical reliability evidence is unavailable.");
        if (assessment.AirframeId != id || !string.Equals(assessment.CanonicalAircraftId, identity.CanonicalAircraftId, StringComparison.Ordinal))
            return new(id, AirframeFailureEligibilityStatus.ReliabilityUnavailable, result.Status, null, "Physical reliability evidence does not match the assigned canonical aircraft.");
        if (assessment.Status == AirframeReliabilityStatus.Unavailable)
            return new(id, AirframeFailureEligibilityStatus.MaintenanceUnavailable, result.Status, assessment,
                "Maintenance gate blocked: " + assessment.Reasons + ".");

        return new(id, AirframeFailureEligibilityStatus.ComponentModelUnavailable, result.Status, assessment,
            "Maintenance gate passed; component reliability and a supported failure model are unavailable. No failure can be generated.");
    }
}
