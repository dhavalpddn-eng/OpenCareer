using OpenCareer.Domain.Military;

namespace OpenCareer.Domain.Conflict;

public sealed record ConflictResourceState(
    string CampaignId,
    double FriendlySupply,
    double FriendlyOperationalReadiness,
    double HostileSupply)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ValidateUnitInterval(FriendlySupply, nameof(FriendlySupply));
        ValidateUnitInterval(
            FriendlyOperationalReadiness,
            nameof(FriendlyOperationalReadiness));
        ValidateUnitInterval(HostileSupply, nameof(HostileSupply));
    }

    private static void ValidateUnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class ConflictResourceConsequence
{
    public static ConflictResourceState Apply(
        ConflictResourceState current,
        OperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(outcome);

        current.Validate();
        outcome.Validate();

        (
            double friendlySupplyDelta,
            double friendlyReadinessDelta,
            double hostileSupplyDelta) =
            outcome.Status switch
            {
                OperationOutcomeStatus.Success =>
                    (0.04, 0.03, -0.03),

                OperationOutcomeStatus.PartialSuccess =>
                    (0.02, 0.015, -0.015),

                OperationOutcomeStatus.Failure =>
                    (-0.04, -0.03, 0.02),

                OperationOutcomeStatus.Aborted =>
                    (-0.01, -0.005, 0),

                _ => throw new ArgumentOutOfRangeException(
                    nameof(outcome.Status))
            };

        var updated = current with
        {
            FriendlySupply = Clamp(
                current.FriendlySupply + friendlySupplyDelta),
            FriendlyOperationalReadiness = Clamp(
                current.FriendlyOperationalReadiness
                + friendlyReadinessDelta),
            HostileSupply = Clamp(
                current.HostileSupply + hostileSupplyDelta)
        };

        updated.Validate();
        return updated;
    }

    private static double Clamp(double value) =>
        Math.Clamp(value, 0, 1);
}
