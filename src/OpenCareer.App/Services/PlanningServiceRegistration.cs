using Microsoft.Extensions.DependencyInjection;
using OpenCareer.Application.Planning;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.SimConnect;

namespace OpenCareer.App.Services;

internal static class PlanningServiceRegistration
{
    internal static IServiceCollection AddOpenCareerPlanningServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SimConnectInstalledAircraftObservationSource>();
        services.AddSingleton<IInstalledAircraftDiscoverySource>(provider =>
            provider.GetRequiredService<SimConnectInstalledAircraftObservationSource>());
        services.AddSingleton<IAircraftRegistryObservationSource>(provider =>
            provider.GetRequiredService<SimConnectInstalledAircraftObservationSource>());

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

        services.AddSingleton<SimConnectAirportDataObservationSource>();
        services.AddSingleton<IAirportDataObservationSource>(provider =>
            provider.GetRequiredService<SimConnectAirportDataObservationSource>());

        services.AddSingleton<CachedAirportDataSource>(provider =>
            new CachedAirportDataSource(
                provider.GetServices<IAirportDataObservationSource>()));
        services.AddSingleton<IAirportDataSource>(provider =>
            provider.GetRequiredService<CachedAirportDataSource>());

        services.AddSingleton<SimConnectLocalAirportWeatherSource>();
        services.AddSingleton<IAirportDispatchWeatherSource>(provider =>
            provider.GetRequiredService<SimConnectLocalAirportWeatherSource>());

        services.AddSingleton<OperationDispatchPlanningService>(provider =>
            new OperationDispatchPlanningService(
                provider.GetRequiredService<IAircraftRegistrySource>(),
                provider.GetRequiredService<IAirportDataSource>(),
                provider.GetRequiredService<IAirportDispatchWeatherSource>()));

        return services;
    }
}
