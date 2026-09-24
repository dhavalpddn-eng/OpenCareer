using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Planning;
using OpenCareer.Infrastructure.Airports;

namespace OpenCareer.Tests;

public sealed class OfflineCareerDispatchTests
{
    [Fact]
    public async Task SupportedAircraftAndReferenceAirportsAllowOfflineDispatchScreening()
    {
        var registry = new AircraftRegistryCatalogService(
            [new PlayableLoopAircraftRegistrySource()]);
        var airports = new CachedAirportDataSource(
            [new PlayableLoopReferenceAirportObservationSource()]);
        AircraftRegistryResolution aircraft = (await registry.FindAircraftAsync(
            PlayableLoopAircraftRegistrySource.AircraftId))!;

        DispatchFeasibilityResult result =
            OperationDispatchPhysicalEvaluator.Evaluate(
                aircraft,
                new OperationDispatchRequirements(
                    PayloadPounds: 0,
                    RequiredRangeNauticalMiles: 50),
                (await airports.FindAirportAsync("KRME"))!,
                (await airports.FindAirportAsync("KSYR"))!);

        Assert.Equal(DispatchFeasibilityStatus.Feasible, result.Status);
        Assert.Equal(AircraftInstallationStatus.KnownOnly, aircraft.InstallationStatus);
    }
}
