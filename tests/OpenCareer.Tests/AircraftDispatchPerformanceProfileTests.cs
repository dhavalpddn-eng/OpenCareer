using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftDispatchPerformanceProfileTests
{
    public static TheoryData<AircraftDispatchPerformanceProfile> ContradictoryWeights =>
        new()
        {
            Profile(maximumTakeoff: 8000, maximumLanding: 8100),
            Profile(maximumTakeoff: 8000, maximumZeroFuel: 8100),
            Profile(operatingEmpty: 5000, maximumLanding: 4900),
            Profile(operatingEmpty: 5000, maximumZeroFuel: 4900),
            Profile(configuredEmpty: 5000, maximumLanding: 4900),
            Profile(configuredEmpty: 5000, maximumZeroFuel: 4900)
        };

    [Theory]
    [MemberData(nameof(ContradictoryWeights))]
    public void ContradictoryWeightHierarchyIsRejected(
        AircraftDispatchPerformanceProfile profile) =>
        Assert.Throws<ArgumentException>(profile.Validate);

    [Fact]
    public void CoherentWeightHierarchyIsAccepted()
    {
        AircraftDispatchPerformanceProfile profile = Profile(
            operatingEmpty: 5000,
            configuredEmpty: 4800,
            maximumTakeoff: 8000,
            maximumLanding: 7600,
            maximumZeroFuel: 7000);

        profile.Validate();
    }

    private static AircraftDispatchPerformanceProfile Profile(
        double? operatingEmpty = null,
        double? configuredEmpty = null,
        double? maximumTakeoff = null,
        double? maximumLanding = null,
        double? maximumZeroFuel = null) =>
        new(
            OperatingEmptyWeightPounds: operatingEmpty,
            MaximumTakeoffWeightPounds: maximumTakeoff,
            MaximumFuelWeightPounds: null,
            PayloadRangeEnvelope: null,
            Confidence: AircraftDataConfidence.Verified,
            ConfiguredEmptyWeightPounds: configuredEmpty,
            MaximumLandingWeightPounds: maximumLanding,
            MaximumZeroFuelWeightPounds: maximumZeroFuel);
}
