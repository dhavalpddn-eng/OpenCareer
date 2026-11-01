using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

/// <summary>
/// Shared inputs for market screening and accepted-job preparation. Screening
/// never creates a contract, reservation or prepared plan.
/// </summary>
internal static class AutomaticDispatchPolicy
{
    internal static AutomaticDispatchInputs Create(
        DispatchCruiseProfileOption profile,
        double payloadPounds,
        double routeDistanceNauticalMiles)
    {
        DispatchCruiseProfileEnvelope envelope = profile.Envelope;
        envelope.Validate();

        return new(
            new CruisePerformancePlanningRequest(
                profile.ProfileIndex,
                profile.FuelTypeIndex,
                envelope.MaximumWeightPounds,
                Math.Clamp(0, envelope.MinimumIsaDeviationCelsius,
                    envelope.MaximumIsaDeviationCelsius),
                envelope.MinimumPressureAltitudeFeet),
            new RouteFuelPlanningRequest(
                routeDistanceNauticalMiles,
                routeDistanceNauticalMiles,
                ClimbTimeMinutes: 0,
                DescentTimeMinutes: 0,
                ClimbFuelGallons: 0,
                DescentFuelGallons: 0,
                new RouteFuelReservePolicy(
                    ContingencyPercentOfTripFuel: 10,
                    FinalReserveMinutesAtCruiseConsumption: 45)),
            new OperationDispatchRequirements(
                payloadPounds,
                routeDistanceNauticalMiles,
                RangeSafetyMarginPercent: 10,
                RunwayLengthSafetyMarginPercent: 15));
    }
}

internal sealed record AutomaticDispatchInputs(
    CruisePerformancePlanningRequest Cruise,
    RouteFuelPlanningRequest RouteFuel,
    OperationDispatchRequirements Requirements);
