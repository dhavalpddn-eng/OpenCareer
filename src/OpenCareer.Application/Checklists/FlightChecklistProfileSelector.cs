using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Checklists;

namespace OpenCareer.Application.Checklists;

public sealed record FlightChecklistSelectionContext(
    AircraftCapabilityProfile Aircraft,
    bool AuthorizedAirborneStart = false,
    bool AuthorizedRunwayStart = false)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Aircraft);
        Aircraft.Validate();

        if (AuthorizedAirborneStart && AuthorizedRunwayStart)
        {
            throw new ArgumentException(
                "A checklist start cannot be both airborne and runway-authorized.");
        }
    }
}

public sealed class FlightChecklistProfileSelector
{
    public FlightChecklistProfile Select(
        FlightChecklistSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        FlightChecklistProfile baseProfile =
            context.Aircraft.Has(AircraftCapability.Helicopter)
                ? FlightChecklistProfiles.Helicopter
                : FlightChecklistProfiles.Standard;

        if (context.AuthorizedAirborneStart)
        {
            return DeriveStartProfile(
                baseProfile,
                "airborne-start",
                ShouldKeepForAirborneStart);
        }

        if (context.AuthorizedRunwayStart)
        {
            return DeriveStartProfile(
                baseProfile,
                "runway-start",
                ShouldKeepForRunwayStart);
        }

        return baseProfile;
    }

    private static FlightChecklistProfile DeriveStartProfile(
        FlightChecklistProfile baseProfile,
        string suffix,
        Func<FlightChecklistStepId, bool> include)
    {
        FlightChecklistProfileStep[] steps =
            baseProfile.Steps
                .Where(step => include(step.Id))
                .ToArray();

        var profile =
            new FlightChecklistProfile(
                $"{baseProfile.Id}-{suffix}",
                steps);

        profile.Validate();
        return profile;
    }

    private static bool ShouldKeepForRunwayStart(
        FlightChecklistStepId stepId) =>
        stepId is not FlightChecklistStepId.EngineStarted
            and not FlightChecklistStepId.TaxiMovementEstablished;

    private static bool ShouldKeepForAirborneStart(
        FlightChecklistStepId stepId) =>
        stepId is FlightChecklistStepId.AircraftReady
            or FlightChecklistStepId.AirborneEstablished
            or FlightChecklistStepId.ApproachEstablished
            or FlightChecklistStepId.TouchdownConfirmed
            or FlightChecklistStepId.LandingRolloutComplete
            or FlightChecklistStepId.AircraftParked
            or FlightChecklistStepId.ShutdownConfirmed;
}
