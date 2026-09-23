using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public interface ICareerJobBoardRefillService
{
    Task<JobBoardState> RefillAsync(
        PlayerCareerProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed class CareerJobBoardRefillService
    : ICareerJobBoardRefillService
{
    private const double EarthRadiusNauticalMiles = 3440.065;
    private const double NominalPlanningSpeedKnots = 120.0;

    private static readonly string[] SupportedDestinationIcaos =
    [
        "KRME",
        "KSYR",
        "KALB"
    ];

    private static readonly IReadOnlySet<ContractKind> SupportedKinds =
        new HashSet<ContractKind>
        {
            ContractKind.Ferry,
            ContractKind.Reposition
        };

    private static readonly JobMarketPolicy PlayableLoopPolicy =
        JobMarketPolicy.Default with
        {
            MinimumOffers = 1,
            MaximumOffers = 2,
            ShowLockedPreviews = false
        };

    private readonly JobBoardGenerationService _generation;
    private readonly IJobBoardStateStore _store;
    private readonly IAirportDataSource _airports;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CareerJobBoardRefillService(
        JobBoardGenerationService generation,
        IJobBoardStateStore store,
        IAirportDataSource airports,
        TimeProvider timeProvider)
    {
        _generation =
            generation
            ?? throw new ArgumentNullException(nameof(generation));
        _store =
            store
            ?? throw new ArgumentNullException(nameof(store));
        _airports =
            airports
            ?? throw new ArgumentNullException(nameof(airports));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<JobBoardState> RefillAsync(
        PlayerCareerProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            string originIcao =
                profile.Location.CurrentAirportIcao;

            DateTimeOffset now =
                _timeProvider.GetUtcNow();

            if (!MeetsRequiredQualifications(
                    profile.Qualifications))
            {
                return await PreserveOrCreateValidBoardAsync(
                        originIcao,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            AirportRecord? origin =
                await _airports
                    .FindAirportAsync(
                        originIcao,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!IsUsableMarketAirport(origin)
                || !origin!.HasPosition)
            {
                return await PreserveOrCreateValidBoardAsync(
                        originIcao,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            IReadOnlyList<JobMarketDestination> destinations =
                await BuildDestinationsAsync(
                        origin,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (destinations.Count == 0)
            {
                return await PreserveOrCreateValidBoardAsync(
                        originIcao,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            AirportCareerProfile marketOrigin =
                CreateBaselineCivilianProfile(
                    origin);

            var request =
                new JobMarketGenerationRequest(
                    GenerationSeed:
                        CreateGenerationSeed(
                            profile.CareerId),
                    Time:
                        now,
                    Origin:
                        marketOrigin,
                    Destinations:
                        destinations,
                    Access:
                        JobMarketAccess.CivilianEmployment,
                    CareerLevel:
                        1,
                    Policy:
                        PlayableLoopPolicy,
                    Capacity:
                        AirportMarketCapacity.ForScale(
                            AirportMarketScale.Regional),
                    AllowedContractKinds:
                        SupportedKinds);

            JobBoardState board =
                await _generation
                    .RefreshAsync(
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);

            board.Validate();

            if (!string.Equals(
                    board.AirportIcao,
                    originIcao,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generated job board does not belong to the authoritative career location.");
            }

            return board;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<JobMarketDestination>> BuildDestinationsAsync(
        AirportRecord origin,
        CancellationToken cancellationToken)
    {
        var result =
            new List<JobMarketDestination>();

        foreach (string destinationIcao
            in SupportedDestinationIcaos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(
                    destinationIcao,
                    origin.Icao,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AirportRecord? destination =
                await _airports
                    .FindAirportAsync(
                        destinationIcao,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!IsUsableMarketAirport(destination)
                || !destination!.HasPosition)
            {
                continue;
            }

            double distanceNm =
                GreatCircleDistanceNauticalMiles(
                    origin.LatitudeDegrees!.Value,
                    origin.LongitudeDegrees!.Value,
                    destination.LatitudeDegrees!.Value,
                    destination.LongitudeDegrees!.Value);

            if (!double.IsFinite(distanceNm)
                || distanceNm <= 0
                || distanceNm
                    > PlayableLoopPolicy.MaximumGeneratedDistanceNm)
            {
                continue;
            }

            double estimatedHours =
                Math.Max(
                    0.25,
                    distanceNm
                    / NominalPlanningSpeedKnots);

            result.Add(
                new JobMarketDestination(
                    destination.Icao
                        .Trim()
                        .ToUpperInvariant(),
                    distanceNm,
                    RouteStrength:
                        0,
                    RelationshipStrength:
                        0,
                    MarketAttractiveness:
                        1,
                    EstimatedFlightHours:
                        estimatedHours));
        }

        return result
            .OrderBy(
                static destination =>
                    destination.DistanceNm)
            .ThenBy(
                static destination =>
                    destination.Icao,
                StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<JobBoardState> PreserveOrCreateValidBoardAsync(
        string airportIcao,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        JobBoardState? existing =
            await _store
                .GetAsync(
                    airportIcao,
                    cancellationToken)
                .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Validate();

            if (!string.Equals(
                    existing.AirportIcao,
                    airportIcao,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Job-board store returned a different airport than the authoritative career location.");
            }

            return existing;
        }

        JobBoardState empty =
            JobBoardState.Empty(
                airportIcao,
                now);

        await _store
            .SaveAsync(
                empty,
                cancellationToken)
            .ConfigureAwait(false);

        JobBoardState authoritative =
            await _store
                .GetAsync(
                    airportIcao,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Empty authoritative job board was not readable after persistence.");

        authoritative.Validate();
        return authoritative;
    }

    private static AirportCareerProfile CreateBaselineCivilianProfile(
        AirportRecord airport)
    {
        airport.Validate();

        return new(
            airport.Icao
                .Trim()
                .ToUpperInvariant(),
            airport.Name,
            AirportOpportunity.Civilian,
            CivilianDemand:
                1,
            GovernmentDemand:
                0,
            MilitaryDemand:
                0,
            UasResearchDemand:
                0,
            MonthlyStorageCostIndex:
                1m,
            StorageScarcity:
                0);
    }

    private static bool MeetsRequiredQualifications(
        PilotQualificationState actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        actual.Validate();

        PilotQualificationState required =
            PilotQualificationState.Entry;

        return actual.License >= required.License
            && actual.Ratings.IsSupersetOf(
                required.Ratings);
    }

    private static bool IsUsableMarketAirport(
        AirportRecord? airport)
    {
        if (airport is null)
            return false;

        airport.Validate();

        // Market generation needs a real airport identity and position only.
        // Runway feasibility remains an authoritative dispatch concern.
        return true;
    }

    private static double GreatCircleDistanceNauticalMiles(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB)
    {
        double latitudeARadians =
            DegreesToRadians(latitudeA);
        double latitudeBRadians =
            DegreesToRadians(latitudeB);
        double deltaLatitude =
            DegreesToRadians(
                latitudeB - latitudeA);
        double deltaLongitude =
            DegreesToRadians(
                longitudeB - longitudeA);

        double sinLatitude =
            Math.Sin(
                deltaLatitude / 2);
        double sinLongitude =
            Math.Sin(
                deltaLongitude / 2);

        double a =
            sinLatitude * sinLatitude
            + Math.Cos(latitudeARadians)
                * Math.Cos(latitudeBRadians)
                * sinLongitude
                * sinLongitude;

        double centralAngle =
            2
            * Math.Asin(
                Math.Min(
                    1,
                    Math.Sqrt(a)));

        return EarthRadiusNauticalMiles
            * centralAngle;
    }

    private static double DegreesToRadians(
        double degrees) =>
        degrees
        * Math.PI
        / 180.0;

    private static ulong CreateGenerationSeed(
        Guid careerId)
    {
        byte[] bytes =
            careerId.ToByteArray();

        ulong hash =
            14695981039346656037UL;

        unchecked
        {
            foreach (byte value in bytes)
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }

        return hash;
    }
}
