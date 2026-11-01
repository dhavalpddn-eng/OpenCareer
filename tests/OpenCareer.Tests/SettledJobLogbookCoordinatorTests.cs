using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class SettledJobLogbookCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SettledCompletedJobCommitsThroughExistingLogbookAuthority()
    {
        Guid contractId =
            Guid.Parse(
                "9b000000-0000-0000-0000-000000000001");

        FlightSession session =
            CompletedSession(
                contractId);

        EconomySettlementResult settlement =
            Settlement(
                contractId,
                session.Milestones.CompletedAt!.Value
                    .AddMinutes(1));

        var sessions =
            new FlightSessionCoordinator();
        sessions.Restore(
            session);

        var writer =
            new FakeWriter();

        var coordinator =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    writer));

        LogbookAppendResult result =
            await coordinator.CommitAsync(
                settlement,
                Context(),
                settlement.Settlement.Transaction.OccurredAt
                    .AddMinutes(1));

        Assert.Equal(
            LogbookAppendDisposition.Appended,
            result.Disposition);
        Assert.Equal(
            session.SessionId,
            result.Entry.EntryId);
        Assert.Equal(
            contractId,
            result.Entry.Debrief.ContractId);
        Assert.Equal(
            LogbookEntryKind.CareerJob,
            result.Entry.Debrief.EntryKind);
        Assert.Equal(
            SettlementRecordStatus.Settled,
            result.Entry.Debrief.Settlement.Status);
        Assert.Equal(
            settlement.Settlement.Transaction.IdempotencyKey,
            result.Entry.Debrief.Settlement.IdempotencyKey);
        Assert.Equal(
            settlement.Settlement.NetCashChange,
            result.Entry.Debrief.Settlement.CashDelta);
        Assert.Equal(
            2.5d,
            result.Entry.Debrief.Settlement.ReputationDelta);
        Assert.Single(
            writer.StoredEntries);
    }

    [Fact]
    public async Task ReplayUsesSettlementKeyAndDoesNotDuplicateLogbookEntry()
    {
        Guid contractId =
            Guid.Parse(
                "9b000000-0000-0000-0000-000000000002");

        FlightSession session =
            CompletedSession(
                contractId);

        EconomySettlementResult settlement =
            Settlement(
                contractId,
                session.Milestones.CompletedAt!.Value
                    .AddMinutes(1));

        var sessions =
            new FlightSessionCoordinator();
        sessions.Restore(
            session);

        var writer =
            new FakeWriter();

        var coordinator =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    writer));

        DateTimeOffset committedAt =
            settlement.Settlement.Transaction.OccurredAt
                .AddMinutes(1);

        LogbookAppendResult first =
            await coordinator.CommitAsync(
                settlement,
                Context(),
                committedAt);

        LogbookAppendResult replay =
            await coordinator.CommitAsync(
                settlement with
                {
                    PostResult =
                        LedgerPostResult.AlreadyPosted
                },
                Context(),
                committedAt.AddMinutes(1));

        Assert.Equal(
            LogbookAppendDisposition.Appended,
            first.Disposition);
        Assert.Equal(
            LogbookAppendDisposition.AlreadyExists,
            replay.Disposition);
        Assert.Equal(
            first.Entry,
            replay.Entry);
        Assert.Single(
            writer.StoredEntries);
    }

    [Fact]
    public async Task DifferentFlightSessionCannotConsumeSettlement()
    {
        Guid contractId =
            Guid.Parse(
                "9b000000-0000-0000-0000-000000000003");

        FlightSession matching =
            CompletedSession(
                contractId);

        EconomySettlementResult settlement =
            Settlement(
                contractId,
                matching.Milestones.CompletedAt!.Value
                    .AddMinutes(1));

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            CompletedSession(
                Guid.Parse(
                    "9b000000-0000-0000-0000-000000000099")));

        var writer =
            new FakeWriter();

        var coordinator =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    writer));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CommitAsync(
                settlement,
                Context(),
                settlement.Settlement.Transaction.OccurredAt
                    .AddMinutes(1)));

        Assert.Empty(
            writer.StoredEntries);
    }

    [Fact]
    public async Task MismatchedSettlementIdentityIsRejectedBeforeLogbookWrite()
    {
        Guid contractId =
            Guid.Parse(
                "9b000000-0000-0000-0000-000000000004");

        FlightSession session =
            CompletedSession(
                contractId);

        EconomySettlementResult valid =
            Settlement(
                contractId,
                session.Milestones.CompletedAt!.Value
                    .AddMinutes(1));

        EconomySettlementResult mismatched =
            valid with
            {
                Settlement =
                    valid.Settlement with
                    {
                        Transaction =
                            valid.Settlement.Transaction with
                            {
                                IdempotencyKey =
                                    "contract:wrong:settlement-v1"
                            }
                    }
            };

        var sessions =
            new FlightSessionCoordinator();
        sessions.Restore(
            session);

        var writer =
            new FakeWriter();

        var coordinator =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    writer));

        await Assert.ThrowsAnyAsync<Exception>(
            () => coordinator.CommitAsync(
                mismatched,
                Context(),
                valid.Settlement.Transaction.OccurredAt
                    .AddMinutes(1)));

        Assert.Empty(
            writer.StoredEntries);
    }

    [Fact]
    public async Task PendingMissionOutcomeCannotFreezeIntoCareerLogbook()
    {
        Guid contractId =
            Guid.Parse(
                "9b000000-0000-0000-0000-000000000005");

        FlightSession session =
            CompletedSession(
                contractId);

        EconomySettlementResult settlement =
            Settlement(
                contractId,
                session.Milestones.CompletedAt!.Value
                    .AddMinutes(1));

        var sessions =
            new FlightSessionCoordinator();
        sessions.Restore(
            session);

        var writer =
            new FakeWriter();

        var coordinator =
            new SettledJobLogbookCoordinator(
                sessions,
                new LogbookCommitCoordinator(
                    writer));

        SettledJobLogbookContext context =
            Context() with
            {
                MissionOutcome =
                    MissionOutcome.Pending
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.CommitAsync(
                settlement,
                context,
                settlement.Settlement.Transaction.OccurredAt
                    .AddMinutes(1)));

        Assert.Empty(
            writer.StoredEntries);
    }

    private static SettledJobLogbookContext Context() =>
        new(
            new AircraftDebrief(
                "Integration Aircraft",
                Family:
                    "Cargo"),
            ActualDeparture:
                "KRME",
            ActualArrival:
                "KSYR",
            DiversionLocation:
                null,
            Payload:
                new PayloadDebrief(
                    PassengerCount:
                        null,
                    CargoMassPounds:
                        500,
                    CargoDescription:
                        "Career cargo",
                    Outcome:
                        "Delivered",
                    EvidenceQuality:
                        EvidenceQuality.MissionDeclared),
            SafetyOutcome:
                FlightSafetyOutcome.CompletedNormally,
            MissionOutcome:
                MissionOutcome.Succeeded,
            ReputationDelta:
                2.5d);

    private static EconomySettlementResult Settlement(
        Guid contractId,
        DateTimeOffset settledAt)
    {
        PersistedJobContract persisted =
            CompletedContract(
                contractId);

        ContractSettlementSummary summary =
            ContractSettlementEngine.Create(
                persisted.Contract,
                new ContractSettlementCosts(
                    FuelCost:
                        100m,
                    MaintenanceReserveCost:
                        50m,
                    AirportFees:
                        25m),
                settledAt);

        return new EconomySettlementResult(
            persisted,
            summary,
            LedgerPostResult.Posted,
            CashBalanceAfter:
                summary.NetCashChange);
    }

    private static PersistedJobContract CompletedContract(
        Guid contractId)
    {
        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CompanyContract,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.CompanyRevenue,
                        GrossCustomerRevenue:
                            1_000m,
                        PilotCompensation:
                            0m,
                        EmployerCoversFuel:
                            false,
                        EmployerCoversMaintenance:
                            false,
                        EmployerCoversAirportFees:
                            false),
                OfferedAt:
                    Epoch.AddHours(-1),
                MustStartBy:
                    null,
                MustCompleteBy:
                    Epoch.AddHours(2),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.Completed,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch,
                CompletedAt:
                    Epoch.AddSeconds(8));

        contract.Validate();

        return new(
            contract,
            Version: 3);
    }

    private static FlightSession CompletedSession(
        Guid contractId)
    {
        var plan =
            new FlightSessionPlan(
                "KRME",
                "KSYR");

        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId,
                plan:
                    plan);

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

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(8),
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

    private sealed class FakeWriter
        : ILogbookWriter
    {
        private readonly Dictionary<string, LogbookEntry>
            _entries =
                new(StringComparer.Ordinal);

        public IReadOnlyCollection<LogbookEntry> StoredEntries =>
            _entries.Values;

        public Task<LogbookAppendResult> TryAppendAsync(
            LogbookEntry entry,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_entries.TryGetValue(
                    idempotencyKey,
                    out LogbookEntry? existing))
            {
                return Task.FromResult(
                    new LogbookAppendResult(
                        LogbookAppendDisposition.AlreadyExists,
                        existing));
            }

            _entries.Add(
                idempotencyKey,
                entry);

            return Task.FromResult(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry));
        }
    }
}
