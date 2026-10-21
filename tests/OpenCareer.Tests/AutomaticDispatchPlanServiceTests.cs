using OpenCareer.Application.Planning;

namespace OpenCareer.Tests;

public sealed class AutomaticDispatchPlanServiceTests
{
    private static readonly Guid ContractId =
        Guid.Parse("a5000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task StandardPolicyBuildsAndPersistsAuthoritativePlan()
    {
        var prepared = new StubPreparedPlans(new(
            PrepareDispatchPlanStatus.Prepared,
            "Prepared."));
        var service = new AutomaticDispatchPlanService(
            ReadyProfiles(),
            prepared);

        PrepareDispatchPlanResult result = await service.PrepareAsync(
            ContractId,
            "msfs-title:Cessna 172 Skyhawk",
            "contract:a5000000-0000-0000-0000-000000000001:aircraft-v1",
            "KJFK",
            "KBOS",
            250,
            300);

        Assert.True(result.IsPrepared);
        PrepareDispatchPlanRequest request = Assert.Single(prepared.Requests);
        Assert.Equal(0, request.Cruise.ProfileIndex);
        Assert.Equal(0, request.Cruise.FuelTypeIndex);
        Assert.Equal(2550, request.Cruise.PlannedCruiseWeightPounds);
        Assert.Equal(0, request.Cruise.IsaDeviationCelsius);
        Assert.Equal(0, request.Cruise.PressureAltitudeFeet);
        Assert.Equal(300, request.RouteFuel.TotalRouteDistanceNauticalMiles);
        Assert.Equal(300, request.RouteFuel.CruiseSegmentDistanceNauticalMiles);
        Assert.Equal(10, request.RouteFuel.ReservePolicy.ContingencyPercentOfTripFuel);
        Assert.Equal(45, request.RouteFuel.ReservePolicy.FinalReserveMinutesAtCruiseConsumption);
        Assert.Equal(250, request.Requirements.PayloadPounds);
        Assert.Equal(10, request.Requirements.RangeSafetyMarginPercent);
        Assert.Equal(15, request.Requirements.RunwayLengthSafetyMarginPercent);
        Assert.Null(request.Requirements.WeatherLimits);
    }

    [Fact]
    public async Task MissingProfileFailsBeforePreparedPlanAuthority()
    {
        var prepared = new StubPreparedPlans(new(
            PrepareDispatchPlanStatus.Prepared,
            "Prepared."));
        var service = new AutomaticDispatchPlanService(
            new StubProfiles(new(
                DispatchCruiseProfileCatalogStatus.DispatchPerformanceUnavailable,
                "No proven performance.",
                Array.Empty<DispatchCruiseProfileOption>())),
            prepared);

        PrepareDispatchPlanResult result = await service.PrepareAsync(
            ContractId,
            "aircraft-a",
            "contract:a5000000-0000-0000-0000-000000000001:aircraft-v1",
            "KJFK",
            "KBOS",
            250,
            300);

        Assert.Equal(PrepareDispatchPlanStatus.PlanningInsufficient, result.Status);
        Assert.Empty(prepared.Requests);
        Assert.Contains("No proven performance", result.Detail, StringComparison.Ordinal);
    }

    private static IDispatchCruiseProfileSource ReadyProfiles() =>
        new StubProfiles(new(
            DispatchCruiseProfileCatalogStatus.Ready,
            "One profile.",
            [
                new(
                    0,
                    0,
                    "Economy",
                    new(1800, 2550, -20, 40, 0, 14000),
                    "Registry evidence")
            ]));

    private sealed class StubProfiles(DispatchCruiseProfileCatalog catalog)
        : IDispatchCruiseProfileSource
    {
        public Task<DispatchCruiseProfileCatalog> ReadAsync(
            string canonicalAircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(catalog);
        }
    }

    private sealed class StubPreparedPlans(PrepareDispatchPlanResult result)
        : IPreparedDispatchPlanService
    {
        public List<PrepareDispatchPlanRequest> Requests { get; } = [];

        public Task<PrepareDispatchPlanResult> PrepareAsync(
            PrepareDispatchPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }
}
