using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class StandardPointToPointMissionCompletionSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DevelopmentLocalCircuitRequiresRealTakeoffAndLanding(
        bool flewCircuit)
    {
        Guid contractId =
            Guid.Parse(
                "a3000000-0000-0000-0000-000000000090");

        JobContract contract =
            InProgressContract(
                contractId,
                ContractKind.Reposition) with
            {
                OriginIcao = "KJFK",
                DestinationIcao = "KJFK",
                MarketId = DevelopmentFlight.MarketId
            };

        FlightSession session =
            ShutdownSession(
                contractId,
                latitude:
                    40.6413,
                longitude:
                    -73.7781) with
            {
                Plan =
                    new FlightSessionPlan(
                        "KJFK",
                        "KJFK")
            };

        if (!flewCircuit)
        {
            session =
                session with
                {
                    Tracking =
                        session.Tracking with
                        {
                            TakeoffCount = 0,
                            LandingEpisodeCount = 0
                        }
                };
        }

        var source =
            new StandardPointToPointMissionCompletionSource(
                new StubAirportSource(
                    new AirportRecord(
                        "KJFK",
                        "John F. Kennedy International",
                        Array.Empty<RunwayRecord>(),
                        LatitudeDegrees:
                            40.6413,
                        LongitudeDegrees:
                            -73.7781)),
                StandardPointToPointMissionPolicy.Default);

        CareerJobMissionCompletionEvidence? evidence =
            await source.ReadAsync(
                contract,
                session);

        if (flewCircuit)
        {
            Assert.NotNull(evidence);
            Assert.True(
                evidence.MissionConditionsVerified);
            Assert.Equal(
                "KJFK",
                evidence.ActualArrival);
        }
        else
        {
            Assert.Null(evidence);
        }
    }

    [Fact]
    public async Task FerryAtContractedDestinationProducesVerifiedMissionEvidence()
    {
        Guid contractId =
            Guid.Parse(
                "a3000000-0000-0000-0000-000000000001");

        var source =
            new StandardPointToPointMissionCompletionSource(
                new StubAirportSource(
                    Destination()),
                StandardPointToPointMissionPolicy.Default);

        CareerJobMissionCompletionEvidence? evidence =
            await source.ReadAsync(
                InProgressContract(
                    contractId,
                    ContractKind.Ferry),
                ShutdownSession(
                    contractId,
                    latitude:
                        43.111,
                    longitude:
                        -76.106));

        Assert.NotNull(evidence);
        Assert.True(
            evidence.MissionConditionsVerified);
        Assert.Equal(
            MissionOutcome.Succeeded,
            evidence.MissionOutcome);
        Assert.Equal(
            "KSYR",
            evidence.ActualArrival);
        Assert.Null(
            evidence.ActualDeparture);
    }

    [Fact]
    public async Task ShutdownAwayFromDestinationDoesNotVerifyMission()
    {
        Guid contractId =
            Guid.Parse(
                "a3000000-0000-0000-0000-000000000002");

        var source =
            new StandardPointToPointMissionCompletionSource(
                new StubAirportSource(
                    Destination()),
                StandardPointToPointMissionPolicy.Default);

        CareerJobMissionCompletionEvidence? evidence =
            await source.ReadAsync(
                InProgressContract(
                    contractId,
                    ContractKind.Reposition),
                ShutdownSession(
                    contractId,
                    latitude:
                        43.20,
                    longitude:
                        -76.30));

        Assert.NotNull(evidence);
        Assert.False(
            evidence.MissionConditionsVerified);
        Assert.Equal(
            MissionOutcome.Failed,
            evidence.MissionOutcome);
        Assert.Null(
            evidence.ActualArrival);
    }

    [Fact]
    public async Task CargoContractIsLeftForManifestAwareMissionAuthority()
    {
        Guid contractId =
            Guid.Parse(
                "a3000000-0000-0000-0000-000000000003");

        var source =
            new StandardPointToPointMissionCompletionSource(
                new StubAirportSource(
                    Destination()),
                StandardPointToPointMissionPolicy.Default);

        Assert.Null(
            await source.ReadAsync(
                InProgressContract(
                    contractId,
                    ContractKind.Cargo),
                ShutdownSession(
                    contractId,
                    43.111,
                    -76.106)));
    }

    [Fact]
    public async Task MissingAirportCoordinatesFailsClosed()
    {
        Guid contractId =
            Guid.Parse(
                "a3000000-0000-0000-0000-000000000004");

        var source =
            new StandardPointToPointMissionCompletionSource(
                new StubAirportSource(
                    new AirportRecord(
                        "KSYR",
                        "Syracuse",
                        Array.Empty<RunwayRecord>())),
                StandardPointToPointMissionPolicy.Default);

        Assert.Null(
            await source.ReadAsync(
                InProgressContract(
                    contractId,
                    ContractKind.Ferry),
                ShutdownSession(
                    contractId,
                    43.111,
                    -76.106)));
    }

    [Fact]
    public async Task OffGroundContinuityCannotVerifyDestinationArrival()
    {
        Guid contractId =
            Guid.Parse(
                "a3000000-0000-0000-0000-000000000005");

        var source =
            new StandardPointToPointMissionCompletionSource(
                new StubAirportSource(
                    Destination()),
                StandardPointToPointMissionPolicy.Default);

        FlightSession session =
            ShutdownSession(
                contractId,
                43.111,
                -76.106) with
            {
                ContinuityAnchor =
                    new FlightContinuityAnchor(
                        Epoch.AddSeconds(7),
                        43.111,
                        -76.106,
                        1_000,
                        OnGround:
                            false)
            };

        CareerJobMissionCompletionEvidence? evidence =
            await source.ReadAsync(
                InProgressContract(
                    contractId,
                    ContractKind.Ferry),
                session);

        Assert.NotNull(evidence);
        Assert.False(
            evidence.MissionConditionsVerified);
    }

    private static AirportRecord Destination() =>
        new(
            "KSYR",
            "Syracuse Hancock International",
            Array.Empty<RunwayRecord>(),
            LatitudeDegrees:
                43.1112,
            LongitudeDegrees:
                -76.1063);

    private static JobContract InProgressContract(
        Guid contractId,
        ContractKind kind)
    {
        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    null,
                Kind:
                    kind,
                ServiceTrack:
                    ServiceTrack.CompanyContract,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.CompanyRevenue,
                        1_000m,
                        0m,
                        false,
                        false,
                        false),
                OfferedAt:
                    Epoch.AddHours(-1),
                MustStartBy:
                    null,
                MustCompleteBy:
                    Epoch.AddHours(2),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.InProgress,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch);

        contract.Validate();
        return contract;
    }

    private static FlightSession ShutdownSession(
        Guid contractId,
        double latitude,
        double longitude)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId);

        session =
            Advance(
                session,
                1,
                stable:
                    true,
                validAircraft:
                    true);

        session =
            Advance(
                session,
                2,
                movement:
                    true);

        session =
            Advance(
                session,
                3,
                takeoffCandidate:
                    true);

        session =
            Advance(
                session,
                4,
                airborne:
                    true);

        session =
            Advance(
                session,
                5,
                touchdown:
                    true);

        session =
            Advance(
                session,
                6,
                rollout:
                    true);

        session =
            Advance(
                session,
                7,
                parking:
                    true,
                shutdown:
                    true);

        return session with
        {
            ContinuityAnchor =
                new FlightContinuityAnchor(
                    Epoch.AddSeconds(7),
                    latitude,
                    longitude,
                    400,
                    OnGround:
                        true)
        };
    }

    private static FlightSession Advance(
        FlightSession session,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(seconds),
                    Connected:
                        true,
                    StableTelemetry:
                        stable,
                    ValidLoadedAircraft:
                        validAircraft,
                    ContinuityPlausible:
                        true,
                    SelfPoweredMovementForFlight:
                        movement,
                    TakeoffCandidate:
                        takeoffCandidate,
                    AirborneConfirmed:
                        airborne,
                    TouchdownConfirmed:
                        touchdown,
                    LandingRolloutConfirmed:
                        rollout,
                    ParkingConfirmed:
                        parking),
                ShutdownConfirmed:
                    shutdown));

    private sealed class StubAirportSource(
        AirportRecord? airport)
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                airport is not null
                && string.Equals(
                    airport.Icao,
                    icao,
                    StringComparison.OrdinalIgnoreCase)
                    ? airport
                    : null);
        }
    }
}
