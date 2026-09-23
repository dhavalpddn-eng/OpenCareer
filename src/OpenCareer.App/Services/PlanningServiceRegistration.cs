using Microsoft.Extensions.DependencyInjection;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Airports;
using OpenCareer.Infrastructure.Weather;
using OpenCareer.SimConnect;

namespace OpenCareer.App.Services;

internal static class PlanningServiceRegistration
{
    internal static IServiceCollection AddOpenCareerPlanningServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SimConnectInstalledAircraftObservationSource>();
        services.AddSingleton<MsfsPackageInstalledAircraftDiscoverySource>(_ =>
            new MsfsPackageInstalledAircraftDiscoverySource(
                MsfsUserConfigLocator.FindPackageRoots()));
        services.AddSingleton<IInstalledAircraftDiscoverySource>(provider =>
            new CompositeInstalledAircraftDiscoverySource(
                provider.GetRequiredService<SimConnectInstalledAircraftObservationSource>(),
                provider.GetRequiredService<MsfsPackageInstalledAircraftDiscoverySource>()));

        services.AddSingleton<IInstalledAircraftRegistryStore>(provider =>
            new SqliteInstalledAircraftRegistryStore(
                provider.GetRequiredService<OpenCareerDataPaths>().DatabaseFile));
        services.AddSingleton<SqliteAircraftAvailabilityStore>(provider =>
            new SqliteAircraftAvailabilityStore(
                provider.GetRequiredService<OpenCareerDataPaths>().DatabaseFile));
        services.AddSingleton<IAircraftAvailabilityStore>(provider =>
            provider.GetRequiredService<SqliteAircraftAvailabilityStore>());
        services.AddSingleton<IAircraftReservationStore>(provider =>
            provider.GetRequiredService<SqliteAircraftAvailabilityStore>());
        services.AddSingleton<IAircraftReservationLookup>(provider =>
            provider.GetRequiredService<SqliteAircraftAvailabilityStore>());
        services.AddSingleton<AircraftReservationCoordinator>();
        services.AddSingleton<PersistentInstalledAircraftObservationSource>();
        services.AddSingleton<IAircraftRegistryObservationSource>(provider =>
            provider.GetRequiredService<PersistentInstalledAircraftObservationSource>());

        services.AddSingleton<MsfsAircraftCfgObservationSource>(_ =>
            new MsfsAircraftCfgObservationSource(
                MsfsUserConfigLocator.FindPackageRoots()));
        services.AddSingleton<IAircraftRegistryObservationSource>(provider =>
            provider.GetRequiredService<MsfsAircraftCfgObservationSource>());

        services.AddSingleton<AircraftRegistryCatalogService>(provider =>
            new AircraftRegistryCatalogService(
                provider.GetServices<IAircraftRegistryObservationSource>()));
        services.AddSingleton<IAircraftRegistrySource>(provider =>
            provider.GetRequiredService<AircraftRegistryCatalogService>());

        services.AddSingleton<CruisePerformancePlanningService>(provider =>
            new CruisePerformancePlanningService(
                provider.GetRequiredService<IAircraftRegistrySource>()));

        services.AddSingleton<RouteFuelPlanningService>(provider =>
            new RouteFuelPlanningService(
                provider.GetRequiredService<CruisePerformancePlanningService>()));

        services.AddSingleton<RouteFuelWeightPlanningService>(provider =>
            new RouteFuelWeightPlanningService(
                provider.GetRequiredService<IAircraftRegistrySource>(),
                provider.GetRequiredService<RouteFuelPlanningService>()));

        services.AddSingleton<SimConnectAirportDataObservationSource>();
        services.AddSingleton<IAirportDataObservationSource>(provider =>
            provider.GetRequiredService<SimConnectAirportDataObservationSource>());
        services.AddSingleton<PlayableLoopReferenceAirportObservationSource>();
        services.AddSingleton<ICareerJobMarketAirportSource>(provider =>
            provider.GetRequiredService<PlayableLoopReferenceAirportObservationSource>());

        services.AddSingleton<CachedAirportDataSource>(provider =>
            new CachedAirportDataSource(
                provider.GetServices<IAirportDataObservationSource>()));
        services.AddSingleton<IAirportDataSource>(provider =>
            provider.GetRequiredService<CachedAirportDataSource>());

        services.AddSingleton<SimConnectLocalAirportWeatherSource>();
        services.AddSingleton<AviationWeatherMetarSource>(provider =>
            new AviationWeatherMetarSource(
                provider.GetRequiredService<IAirportDataSource>()));
        services.AddSingleton<LocalThenReferenceAirportWeatherSource>(provider =>
            new LocalThenReferenceAirportWeatherSource(
                provider.GetRequiredService<SimConnectLocalAirportWeatherSource>(),
                provider.GetRequiredService<AviationWeatherMetarSource>()));
        services.AddSingleton<IAirportDispatchWeatherSource>(provider =>
            provider.GetRequiredService<LocalThenReferenceAirportWeatherSource>());

        services.AddSingleton<OperationDispatchPlanningService>(provider =>
            new OperationDispatchPlanningService(
                provider.GetRequiredService<IAircraftRegistrySource>(),
                provider.GetRequiredService<IAirportDataSource>(),
                provider.GetRequiredService<IAirportDispatchWeatherSource>(),
                provider.GetRequiredService<IAircraftAvailabilityStore>()));

        services.AddSingleton<CompositeDispatchPlanningService>(provider =>
            new CompositeDispatchPlanningService(
                provider.GetRequiredService<RouteFuelWeightPlanningService>(),
                provider.GetRequiredService<OperationDispatchPlanningService>()));

        return services;
    }
}
