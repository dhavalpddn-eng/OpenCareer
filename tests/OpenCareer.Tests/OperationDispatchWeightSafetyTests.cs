using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class OperationDispatchWeightSafetyTests
{
    [Fact]
    public async Task PlannedFuelRequiresDispatchPerformanceData()
    {
        var service = Service(
            Resolution(dispatchPerformance: null),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1000,
                RequiredRangeNauticalMiles: 500,
                PlannedFuelPounds: 800));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.AircraftDispatchPerformanceUnknown);
    }

    [Fact]
    public async Task LoadedTakeoffWeightAboveMtowIsInfeasible()
    {
        var service = Service(
            Resolution(
                dispatchPerformance: DispatchProfile(
                    operatingEmptyWeight: 6000,
                    maximumTakeoffWeight: 8000,
                    maximumFuelWeight: 1500)),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1500,
                RequiredRangeNauticalMiles: 500,
                PlannedFuelPounds: 1000));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.TakeoffWeightExceedsMaximum);

        Assert.Equal(8500, issue.RequiredPounds);
        Assert.Equal(8000, issue.AvailablePounds);
    }

    [Fact]
    public async Task PlannedFuelAboveKnownCapacityIsInfeasible()
    {
        var service = Service(
            Resolution(
                dispatchPerformance: DispatchProfile(
                    operatingEmptyWeight: 5000,
                    maximumTakeoffWeight: 9000,
                    maximumFuelWeight: 1000)),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1000,
                RequiredRangeNauticalMiles: 500,
                PlannedFuelPounds: 1200));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.FuelExceedsAircraftMaximum);

        Assert.Equal(1200, issue.RequiredPounds);
        Assert.Equal(1000, issue.AvailablePounds);
    }

    [Fact]
    public async Task MissingWeightFieldsRemainInsufficientData()
    {
        var service = Service(
            Resolution(
                dispatchPerformance: DispatchProfile(
                    operatingEmptyWeight: null,
                    maximumTakeoffWeight: null,
                    maximumFuelWeight: null)),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1000,
                RequiredRangeNauticalMiles: 500,
                PlannedFuelPounds: 700));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.AircraftOperatingEmptyWeightUnknown);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.AircraftMaximumTakeoffWeightUnknown);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.AircraftMaximumFuelWeightUnknown);
    }

    [Fact]
    public async Task PayloadRangeEnvelopeInterpolatesAtRequiredPayload()
    {
        var service = Service(
            Resolution(
                dispatchPerformance: DispatchProfile(
                    envelope:
                    [
                        new(0, 1000),
                        new(2000, 600)
                    ])),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1000,
                RequiredRangeNauticalMiles: 850));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.PayloadRangeExceeded);

        Assert.Equal(1000, issue.RequiredPounds);
        Assert.Equal(850, issue.RequiredNauticalMiles);
        Assert.Equal(800, issue.AvailableNauticalMiles);
    }

    [Fact]
    public async Task PayloadBeyondEnvelopeCoverageIsInsufficientData()
    {
        var service = Service(
            Resolution(
                maximumPayloadPounds: 3000,
                dispatchPerformance: DispatchProfile(
                    envelope:
                    [
                        new(0, 1000),
                        new(2000, 600)
                    ])),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 2500,
                RequiredRangeNauticalMiles: 500));

        Assert.Equal(DispatchFeasibilityStatus.InsufficientData, result.Status);
        Assert.Contains(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.PayloadRangeEnvelopeDoesNotCoverPayload);
    }

    [Fact]
    public async Task RangeSafetyMarginIsAppliedBeforeCapabilityComparison()
    {
        var service = Service(
            Resolution(maximumRangeNauticalMiles: 1000),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1000,
                RequiredRangeNauticalMiles: 950,
                RangeSafetyMarginPercent: 10));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Reason
                == DispatchFeasibilityReason.RangeExceedsAircraftMaximum);

        Assert.Equal(1045, issue.RequiredNauticalMiles);
        Assert.Equal(1000, issue.AvailableNauticalMiles);
    }

    [Fact]
    public async Task RunwaySafetyMarginIsAppliedToTakeoffRequirement()
    {
        var service = Service(
            Resolution(),
            Airport("KAAA", Runway("18", lengthFeet: 2000)),
            Airport("KBBB", Runway("36", lengthFeet: 5000)));

        DispatchFeasibilityResult result = await service.EvaluateAsync(
            "fixture-aircraft",
            "KAAA",
            "KBBB",
            new(
                PayloadPounds: 1000,
                RequiredRangeNauticalMiles: 500,
                RunwayLengthSafetyMarginPercent: 20));

        Assert.Equal(DispatchFeasibilityStatus.Infeasible, result.Status);
        DispatchFeasibilityIssue issue = Assert.Single(
            result.Issues,
            static issue => issue.Endpoint == DispatchEndpoint.Origin
                && issue.Reason == DispatchFeasibilityReason.RunwayTooShort);

        Assert.Equal(2160, issue.RequiredFeet);
        Assert.Equal(2000, issue.AvailableFeet);
    }

    [Fact]
    public void DispatchPerformanceResolverUsesProfileConfidenceAndProvenance()
    {
        AircraftDispatchPerformanceProfile inferred = DispatchProfile(
            maximumTakeoffWeight: 8000,
            confidence: AircraftDataConfidence.Inferred,
            source: "inferred");

        AircraftDispatchPerformanceProfile verified = DispatchProfile(
            maximumTakeoffWeight: 9000,
            confidence: AircraftDataConfidence.Verified,
            source: "verified");

        AircraftRegistryResolution result = AircraftRegistryResolver.Resolve(
        [
            Observation("provider-a", "one", inferred),
            Observation("provider-b", "two", verified)
        ]);

        Assert.Same(verified, result.DispatchPerformance);
        AircraftRegistryFieldProvenance provenance =
            result.Provenance[AircraftRegistryField.DispatchPerformance];

        Assert.Equal("provider-b", provenance.ProviderId);
        Assert.Equal(AircraftDataConfidence.Verified, provenance.Confidence);
    }

    [Fact]
    public void PayloadRangeEnvelopeRejectsIncreasingRange()
    {
        AircraftDispatchPerformanceProfile profile = DispatchProfile(
            envelope:
            [
                new(0, 600),
                new(1000, 700)
            ]);

        Assert.Throws<ArgumentException>(profile.Validate);
    }

    [Theory]
    [InlineData(10, -1)]
    [InlineData(-1, 10)]
    [InlineData(101, 0)]
    [InlineData(0, 101)]
    public async Task InvalidSafetyMarginsAreRejected(
        double rangeMargin,
        double runwayMargin)
    {
        var service = Service(
            Resolution(),
            Airport("KAAA", Runway("18")),
            Airport("KBBB", Runway("36")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.EvaluateAsync(
                "fixture-aircraft",
                "KAAA",
                "KBBB",
                new(
                    PayloadPounds: 1000,
                    RequiredRangeNauticalMiles: 500,
                    RangeSafetyMarginPercent: rangeMargin,
                    RunwayLengthSafetyMarginPercent: runwayMargin)));
    }

    private static OperationDispatchPlanningService Service(
        AircraftRegistryResolution resolution,
        params AirportRecord[] airports) =>
        new(
            new StubAircraftRegistrySource(resolution),
            new StubAirportDataSource(
                airports.ToDictionary(
                    static airport => airport.Icao,
                    StringComparer.OrdinalIgnoreCase)));

    private static AircraftRegistryResolution Resolution(
        double? maximumPayloadPounds = 3000,
        double? maximumRangeNauticalMiles = 1200,
        AircraftDispatchPerformanceProfile? dispatchPerformance = null) =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: "fixture-aircraft",
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: true,
                MaximumPayloadPounds: maximumPayloadPounds,
                MaximumRangeNauticalMiles: maximumRangeNauticalMiles,
                RunwayPerformance: new(
                    MinimumTakeoffRunwayFeet: 1800,
                    MinimumLandingRunwayFeet: 1600,
                    MinimumRunwayWidthFeet: 50,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                    Confidence: AircraftDataConfidence.Verified,
                    Source: "test"),
                DispatchPerformance: dispatchPerformance)
        ]);

    private static AircraftRegistryObservation Observation(
        string provider,
        string record,
        AircraftDispatchPerformanceProfile profile) =>
        new(
            CanonicalAircraftId: "fixture-aircraft",
            ProviderId: provider,
            ProviderRecordId: record,
            Confidence: AircraftDataConfidence.Reference,
            IsInstalled: false,
            DispatchPerformance: profile);

    private static AircraftDispatchPerformanceProfile DispatchProfile(
        double? operatingEmptyWeight = 5000,
        double? maximumTakeoffWeight = 9000,
        double? maximumFuelWeight = 1500,
        IReadOnlyList<AircraftPayloadRangePoint>? envelope = null,
        AircraftDataConfidence confidence = AircraftDataConfidence.Verified,
        string source = "test") =>
        new(
            operatingEmptyWeight,
            maximumTakeoffWeight,
            maximumFuelWeight,
            envelope,
            confidence,
            source);

    private static AirportRecord Airport(
        string icao,
        params RunwayRecord[] runways) =>
        new(icao, $"{icao} Fixture", runways);

    private static RunwayRecord Runway(
        string identifier,
        double? lengthFeet = 5000,
        double? widthFeet = 100,
        RunwaySurface surface = RunwaySurface.Asphalt) =>
        new(identifier, lengthFeet, widthFeet, surface);

    private sealed class StubAircraftRegistrySource(AircraftRegistryResolution? result)
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class StubAirportDataSource(
        IReadOnlyDictionary<string, AirportRecord> airports)
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            airports.TryGetValue(icao, out AirportRecord? airport);
            return Task.FromResult(airport);
        }
    }
}
