using OpenCareer.Application.Checklists;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Checklists;

namespace OpenCareer.Tests;

public sealed class FlightChecklistProfileSelectorTests
{
    private readonly FlightChecklistProfileSelector _selector = new();

    [Fact]
    public void StandardFixedWingUsesStandardChecklist()
    {
        FlightChecklistProfile profile =
            _selector.Select(
                new FlightChecklistSelectionContext(
                    Aircraft()));

        Assert.Same(
            FlightChecklistProfiles.Standard,
            profile);
    }

    [Fact]
    public void HelicopterOmitsFixedWingGroundRollSteps()
    {
        FlightChecklistProfile profile =
            _selector.Select(
                new FlightChecklistSelectionContext(
                    Aircraft(
                        AircraftCapability.Helicopter
                        | AircraftCapability.Passenger)));

        Assert.Same(
            FlightChecklistProfiles.Helicopter,
            profile);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.TaxiMovementEstablished);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.TakeoffRollEstablished);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.LandingRolloutComplete);
    }

    [Fact]
    public void AuthorizedRunwayStartSkipsEngineAndTaxiPrerequisites()
    {
        FlightChecklistProfile profile =
            _selector.Select(
                new FlightChecklistSelectionContext(
                    Aircraft(),
                    AuthorizedRunwayStart: true));

        Assert.Equal(
            "standard-runway-start",
            profile.Id);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.EngineStarted);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.TaxiMovementEstablished);

        Assert.Contains(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.TakeoffRollEstablished);
    }

    [Fact]
    public void AuthorizedAirborneStartBeginsWithReadyThenAirborneEvidence()
    {
        FlightChecklistProfile profile =
            _selector.Select(
                new FlightChecklistSelectionContext(
                    Aircraft(),
                    AuthorizedAirborneStart: true));

        Assert.Equal(
            "standard-airborne-start",
            profile.Id);

        FlightChecklistStepId[] expected =
        [
            FlightChecklistStepId.AircraftReady,
            FlightChecklistStepId.AirborneEstablished,
            FlightChecklistStepId.ApproachEstablished,
            FlightChecklistStepId.TouchdownConfirmed,
            FlightChecklistStepId.LandingRolloutComplete,
            FlightChecklistStepId.AircraftParked,
            FlightChecklistStepId.ShutdownConfirmed
        ];

        Assert.Equal(
            expected,
            profile.Steps
                .Select(static step => step.Id)
                .ToArray());
    }

    [Fact]
    public void HelicopterRunwayStartPreservesHelicopterProfileShape()
    {
        FlightChecklistProfile profile =
            _selector.Select(
                new FlightChecklistSelectionContext(
                    Aircraft(AircraftCapability.Helicopter),
                    AuthorizedRunwayStart: true));

        Assert.Equal(
            "helicopter-runway-start",
            profile.Id);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.TakeoffRollEstablished);

        Assert.DoesNotContain(
            profile.Steps,
            static step =>
                step.Id == FlightChecklistStepId.EngineStarted);

        Assert.Equal(
            FlightChecklistStepId.AircraftReady,
            profile.Steps[0].Id);
        Assert.Equal(
            FlightChecklistStepId.AirborneEstablished,
            profile.Steps[1].Id);
    }

    [Fact]
    public void ConflictingAuthorizedStartModesAreRejected()
    {
        Assert.Throws<ArgumentException>(
            () =>
                _selector.Select(
                    new FlightChecklistSelectionContext(
                        Aircraft(),
                        AuthorizedAirborneStart: true,
                        AuthorizedRunwayStart: true)));
    }

    private static AircraftCapabilityProfile Aircraft(
        AircraftCapability capabilities =
            AircraftCapability.Passenger) =>
        new(
            AircraftId: "test-aircraft",
            DisplayName: "Test Aircraft",
            Capabilities: capabilities,
            Access: AircraftAccess.Civilian,
            MaximumPayloadPounds: 1200,
            MaximumRangeNauticalMiles: 600,
            TypicalCruiseKnots: 120,
            Seats: 4,
            EngineCount: 1,
            IfrCapable: true,
            Pressurized: false,
            RetractableGear: false);
}
