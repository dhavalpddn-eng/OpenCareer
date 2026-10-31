using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

public interface IAutomaticDispatchPlanService
{
    Task<PrepareDispatchPlanResult> PrepareAsync(
        Guid contractId,
        string canonicalAircraftId,
        string reservationId,
        string originIcao,
        string destinationIcao,
        double payloadPounds,
        double routeDistanceNauticalMiles,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies the standard civilian planning policy to an already accepted job.
/// Aircraft performance remains authoritative: the policy selects only a
/// registry-proven profile and the normal prepared-plan service performs the
/// complete fuel, weight, range, airport and runway evaluation.
/// </summary>
public sealed class AutomaticDispatchPlanService(
    IDispatchCruiseProfileSource cruiseProfiles,
    IPreparedDispatchPlanService preparedPlans)
    : IAutomaticDispatchPlanService
{
    private const double RangeSafetyMarginPercent = 10;
    private const double RunwaySafetyMarginPercent = 15;
    private const double ContingencyFuelPercent = 10;
    private const double FinalReserveMinutes = 45;

    public async Task<PrepareDispatchPlanResult> PrepareAsync(
        Guid contractId,
        string canonicalAircraftId,
        string reservationId,
        string originIcao,
        string destinationIcao,
        double payloadPounds,
        double routeDistanceNauticalMiles,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(contractId));

        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationId);

        if (!double.IsFinite(payloadPounds) || payloadPounds < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadPounds));
        if (!double.IsFinite(routeDistanceNauticalMiles)
            || routeDistanceNauticalMiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeDistanceNauticalMiles));
        }

        DispatchCruiseProfileCatalog catalog = await cruiseProfiles
            .ReadAsync(canonicalAircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (catalog.Status != DispatchCruiseProfileCatalogStatus.Ready
            || catalog.Profiles.Count == 0)
        {
            return new(
                PrepareDispatchPlanStatus.PlanningInsufficient,
                $"Automatic preflight could not load authoritative aircraft performance. {catalog.Detail}");
        }

        PrepareDispatchPlanResult? lastFailure = null;
        foreach (DispatchCruiseProfileOption profile in catalog.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DispatchCruiseProfileEnvelope envelope = profile.Envelope;
            double isaDeviation = Math.Clamp(
                0,
                envelope.MinimumIsaDeviationCelsius,
                envelope.MaximumIsaDeviationCelsius);

            var request = new PrepareDispatchPlanRequest(
                contractId,
                canonicalAircraftId,
                reservationId,
                originIcao,
                destinationIcao,
                new CruisePerformancePlanningRequest(
                    profile.ProfileIndex,
                    profile.FuelTypeIndex,
                    envelope.MaximumWeightPounds,
                    isaDeviation,
                    envelope.MinimumPressureAltitudeFeet),
                new RouteFuelPlanningRequest(
                    routeDistanceNauticalMiles,
                    routeDistanceNauticalMiles,
                    ClimbTimeMinutes: 0,
                    DescentTimeMinutes: 0,
                    ClimbFuelGallons: 0,
                    DescentFuelGallons: 0,
                    new RouteFuelReservePolicy(
                        ContingencyFuelPercent,
                        FinalReserveMinutes)),
                new OperationDispatchRequirements(
                    payloadPounds,
                    routeDistanceNauticalMiles,
                    RangeSafetyMarginPercent: RangeSafetyMarginPercent,
                    RunwayLengthSafetyMarginPercent: RunwaySafetyMarginPercent));

            PrepareDispatchPlanResult result = await preparedPlans
                .PrepareAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsPrepared)
                return result;

            lastFailure = result;
        }

        return lastFailure
            ?? new(
                PrepareDispatchPlanStatus.PlanningInsufficient,
                "Automatic preflight found no usable aircraft performance profile.");
    }
}
