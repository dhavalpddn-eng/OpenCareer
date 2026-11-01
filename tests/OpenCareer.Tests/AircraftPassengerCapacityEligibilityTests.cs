using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftPassengerCapacityEligibilityTests
{
    [Fact]
    public void TotalSeatsCannotSubstituteForMissingPassengerCapacity()
    {
        AircraftCapabilityProfile aircraft = Profile(
            seats: 9,
            passengerCapacity: null);

        Assert.False(aircraft.Satisfies(
            new AircraftMissionRequirements(
                RequiredCapabilities: AircraftCapability.Passenger,
                MinimumSeats: 1,
                MinimumPassengerCapacity: 4)));
    }

    [Fact]
    public void ProvenPassengerCapacitySatisfiesOnlyItsOwnRequirement()
    {
        AircraftCapabilityProfile aircraft = Profile(
            seats: 6,
            passengerCapacity: 4);

        Assert.True(aircraft.Satisfies(
            new AircraftMissionRequirements(
                RequiredCapabilities: AircraftCapability.Passenger,
                MinimumSeats: 1,
                MinimumPassengerCapacity: 4)));
        Assert.False(aircraft.Satisfies(
            new AircraftMissionRequirements(
                RequiredCapabilities: AircraftCapability.Passenger,
                MinimumSeats: 1,
                MinimumPassengerCapacity: 5)));
    }

    [Fact]
    public void MissingPassengerCapacityDoesNotAffectNonPassengerRequirements()
    {
        AircraftCapabilityProfile aircraft = Profile(
            seats: 1,
            passengerCapacity: null);

        Assert.True(aircraft.Satisfies(
            new AircraftMissionRequirements(
                MinimumSeats: 1)));
    }

    private static AircraftCapabilityProfile Profile(
        int seats,
        int? passengerCapacity) =>
        new(
            AircraftId: "passenger-fixture",
            DisplayName: "Passenger Fixture",
            Capabilities: AircraftCapability.Passenger,
            Access: AircraftAccess.Civilian,
            MaximumPayloadPounds: 1200,
            MaximumRangeNauticalMiles: 600,
            TypicalCruiseKnots: 140,
            Seats: seats,
            EngineCount: 1,
            IfrCapable: true,
            Pressurized: false,
            RetractableGear: false,
            PassengerCapacity: passengerCapacity);
}
