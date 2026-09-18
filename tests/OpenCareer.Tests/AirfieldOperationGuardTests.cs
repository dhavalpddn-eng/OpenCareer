using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class AirfieldOperationGuardTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContestedAirfieldAllowsReconButNotPassengerLanding()
    {
        var state = new AirfieldControlState(
            AirfieldControlState.CurrentSchemaVersion,
            "KAAA",
            "region-a",
            AirfieldOperationalStatus.Contested,
            ControllingSideId: null,
            ControlBalance: 0,
            RunwayServiceability: 0.80,
            GroundServicesCapacity: 0.50,
            SecurityPressure: 0.95,
            Epoch);

        Assert.True(AirfieldOperationGuard.Evaluate(
            state,
            AirfieldOperationClass.Reconnaissance).IsAllowed);

        Assert.False(AirfieldOperationGuard.Evaluate(
            state,
            AirfieldOperationClass.CivilianPassenger).IsAllowed);
    }

    [Fact]
    public void SecuredLowServiceAirfieldCanTakeMilitaryButNotPassengers()
    {
        var state = new AirfieldControlState(
            AirfieldControlState.CurrentSchemaVersion,
            "KAAA",
            "region-a",
            AirfieldOperationalStatus.Secured,
            "side-a",
            ControlBalance: 0.80,
            RunwayServiceability: 0.70,
            GroundServicesCapacity: 0.20,
            SecurityPressure: 0.90,
            Epoch);

        Assert.True(AirfieldOperationGuard.Evaluate(
            state,
            AirfieldOperationClass.MilitaryLogistics).IsAllowed);

        Assert.False(AirfieldOperationGuard.Evaluate(
            state,
            AirfieldOperationClass.CivilianPassenger).IsAllowed);
    }
}
