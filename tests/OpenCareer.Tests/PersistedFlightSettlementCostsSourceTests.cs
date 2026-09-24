using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class PersistedFlightSettlementCostsSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingPricingAuthorityDoesNotInventZeroCosts()
    {
        Guid contractId =
            Guid.Parse(
                "a4000000-0000-0000-0000-000000000001");

        var source =
            new PersistedFlightSettlementCostsSource(
                Array.Empty<ICareerJobOperatingCostQuoteSource>());

        CareerJobSettlementCostsEvidence? result =
            await source.ReadAsync(
                InProgressContract(contractId),
                ShutdownSessionWithFuel(contractId));

        Assert.Null(result);
    }

    [Fact]
    public async Task PersistedFlightEvidenceIsPassedToPricingAuthority()
    {
        Guid contractId =
            Guid.Parse(
                "a4000000-0000-0000-0000-000000000002");

        var pricing =
            new CapturingPricingSource(
                new ContractSettlementCosts(
                    FuelCost:
                        120m,
                    MaintenanceReserveCost:
                        40m,
                    AirportFees:
                        15m));

        var source =
            new PersistedFlightSettlementCostsSource(
                [pricing]);

        FlightSession session =
            ShutdownSessionWithFuel(
                contractId);

        CareerJobSettlementCostsEvidence result =
            Assert.IsType<CareerJobSettlementCostsEvidence>(
                await source.ReadAsync(
                    InProgressContract(contractId),
                    session));

        Assert.NotNull(
            pricing.LastBasis);
        Assert.Equal(
            1_000d,
            pricing.LastBasis.StartFuelPounds);
        Assert.Equal(
            940d,
            pricing.LastBasis.LastFuelPounds);
        Assert.Equal(
            60d,
            pricing.LastBasis.FuelBurnedPounds);
        Assert.Equal(
            session.UpdatedAt,
            pricing.LastBasis.EvidenceAt);
        Assert.Equal(
            new ContractSettlementCosts(
                120m,
                40m,
                15m),
            result.Costs);
        Assert.Equal(
            Epoch.AddSeconds(8),
            result.ObservedAt);
    }

    [Fact]
    public async Task MissingPersistedFuelEvidenceFailsClosedBeforePricing()
    {
        Guid contractId =
            Guid.Parse(
                "a4000000-0000-0000-0000-000000000003");

        var pricing =
            new CapturingPricingSource(
                new ContractSettlementCosts(
                    1m,
                    1m,
                    1m));

        var source =
            new PersistedFlightSettlementCostsSource(
                [pricing]);

        Assert.Null(
            await source.ReadAsync(
                InProgressContract(contractId),
                ShutdownSessionWithoutObservations(contractId)));

        Assert.Null(
            pricing.LastBasis);
    }

    [Fact]
    public async Task MismatchedQuoteIdentityIsRejected()
    {
        Guid contractId =
            Guid.Parse(
                "a4000000-0000-0000-0000-000000000004");

        var source =
            new PersistedFlightSettlementCostsSource(
            [
                new MismatchedPricingSource()
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ReadAsync(
                InProgressContract(contractId),
                ShutdownSessionWithFuel(contractId)));
    }

    [Fact]
    public async Task MultiplePricingAuthoritiesAreRejected()
    {
        Guid contractId =
            Guid.Parse(
                "a4000000-0000-0000-0000-000000000005");

        var source =
            new PersistedFlightSettlementCostsSource(
            [
                new CapturingPricingSource(
                    new ContractSettlementCosts(
                        10m,
                        5m,
                        2m),
                    sourceId:
                        "pricing-a"),
                new CapturingPricingSource(
                    new ContractSettlementCosts(
                        10m,
                        5m,
                        2m),
                    sourceId:
                        "pricing-b")
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ReadAsync(
                InProgressContract(contractId),
                ShutdownSessionWithFuel(contractId)));
    }

    [Fact]
    public async Task CompletedContractAndSessionPreserveSettlementCostsForReplay()
    {
        Guid contractId =
            Guid.Parse(
                "a4000000-0000-0000-0000-000000000006");

        var source =
            new PersistedFlightSettlementCostsSource(
            [
                new CapturingPricingSource(
                    new ContractSettlementCosts(
                        120m,
                        40m,
                        15m))
            ]);

        FlightSession session =
            CompleteSession(
                ShutdownSessionWithFuel(
                    contractId));

        JobContract contract =
            FlightSessionContractBridge.CompleteContract(
                InProgressContract(
                    contractId),
                session);

        CareerJobSettlementCostsEvidence evidence =
            Assert.IsType<CareerJobSettlementCostsEvidence>(
                await source.ReadAsync(
                    contract,
                    session));

        Assert.Equal(
            new ContractSettlementCosts(
                120m,
                40m,
                15m),
            evidence.Costs);
    }

    private static JobContract InProgressContract(
        Guid contractId)
    {
        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    null,
                Kind:
                    ContractKind.Ferry,
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

    private static FlightSession ShutdownSessionWithFuel(
        Guid contractId)
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
                    true,
                fuelPounds:
                    1_000);

        session =
            Advance(
                session,
                2,
                movement:
                    true,
                fuelPounds:
                    940);

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

        return Advance(
            session,
            7,
            parking:
                true,
            shutdown:
                true);
    }

    private static FlightSession ShutdownSessionWithoutObservations(
        Guid contractId)
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

        return Advance(
            session,
            7,
            parking:
                true,
            shutdown:
                true);
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
        bool shutdown = false,
        double? fuelPounds = null)
    {
        DateTimeOffset timestamp =
            Epoch.AddSeconds(seconds);

        FlightSessionObservation? observation =
            fuelPounds is { } fuel
                ? new FlightSessionObservation(
                    timestamp,
                    LatitudeDegrees:
                        43.1,
                    LongitudeDegrees:
                        -76.1,
                    AltitudeMslFeet:
                        500,
                    IndicatedAirspeedKnots:
                        airborne
                            ? 120
                            : 0,
                    GroundSpeedKnots:
                        movement || airborne
                            ? 20
                            : 0,
                    FuelTotalPounds:
                        fuel,
                    PayloadPounds:
                        0,
                    CaptureTrackPoint:
                        true)
                : null;

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    timestamp,
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
                    shutdown,
                Observation:
                observation));
    }

    private static FlightSession CompleteSession(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    session.UpdatedAt.AddSeconds(1),
                    Connected:
                        true,
                    StableTelemetry:
                        true,
                    ContinuityPlausible:
                        true,
                    OperationCompleteConfirmed:
                        true),
                ShutdownConfirmed:
                    true));

    private sealed class CapturingPricingSource(
        ContractSettlementCosts costs,
        string sourceId = "fixture-pricing")
        : ICareerJobOperatingCostQuoteSource
    {
        public string SourceId =>
            sourceId;

        public CareerJobOperatingCostBasis? LastBasis { get; private set; }

        public Task<CareerJobOperatingCostQuote?> QuoteAsync(
            JobContract contract,
            CareerJobOperatingCostBasis basis,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastBasis =
                basis;

            return Task.FromResult<CareerJobOperatingCostQuote?>(
                new(
                    contract.ContractId,
                    basis.FlightSessionId,
                    Epoch.AddSeconds(8),
                    SourceId,
                    costs));
        }
    }

    private sealed class MismatchedPricingSource
        : ICareerJobOperatingCostQuoteSource
    {
        public string SourceId =>
            "mismatched-pricing";

        public Task<CareerJobOperatingCostQuote?> QuoteAsync(
            JobContract contract,
            CareerJobOperatingCostBasis basis,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<CareerJobOperatingCostQuote?>(
                new(
                    Guid.Parse(
                        "a4000000-0000-0000-0000-000000000099"),
                    basis.FlightSessionId,
                    Epoch.AddSeconds(8),
                    SourceId,
                    new ContractSettlementCosts(
                        1m,
                        1m,
                        1m)));
        }
    }
}
