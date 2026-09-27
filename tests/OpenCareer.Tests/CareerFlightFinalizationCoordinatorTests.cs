using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class CareerFlightFinalizationCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppliedExperienceAndFleetReleaseClearTerminalCheckpoint()
    {
        LogbookEntry entry =
            CareerEntry();

        TestContext context =
            CreateContext(
                entry);

        CareerFlightFinalizationResult result =
            await context.Finalizer.FinalizeAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                AppliedProfile(
                    entry));

        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            result.Status);
        Assert.Equal(
            CareerFlightReservationReleaseStatus.Released,
            result.ReservationRelease.Status);
        Assert.Null(
            context.Store.Checkpoint);
        Assert.Null(
            context.Sessions.Current);
        Assert.Equal(
            1,
            context.Store.ClearCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
    }

    [Fact]
    public async Task FleetReleaseFailureLeavesTerminalCheckpointIntact()
    {
        LogbookEntry entry =
            CareerEntry();

        TestContext context =
            CreateContext(
                entry);

        context.Fleet.ReleaseResult =
            AircraftReservationReleaseResult.HeldByAnotherReservation;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Finalizer.FinalizeAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                AppliedProfile(
                    entry)));

        Assert.NotNull(
            context.Store.Checkpoint);
        Assert.NotNull(
            context.Sessions.Current);
        Assert.Equal(
            0,
            context.Store.ClearCount);
    }

    [Fact]
    public async Task ClearFailureAfterReleaseRecoversOnRetry()
    {
        LogbookEntry entry =
            CareerEntry();

        TestContext context =
            CreateContext(
                entry);

        context.Store.FailNextClear =
            true;

        await Assert.ThrowsAsync<IOException>(
            () => context.Finalizer.FinalizeAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                AppliedProfile(
                    entry)));

        Assert.Null(
            context.Fleet.Ownership);
        Assert.NotNull(
            context.Store.Checkpoint);
        Assert.NotNull(
            context.Sessions.Current);

        CareerFlightFinalizationResult retry =
            await context.Finalizer.FinalizeAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.AlreadyExists,
                    entry),
                AppliedProfile(
                    entry));

        Assert.Equal(
            CareerFlightFinalizationStatus.Finalized,
            retry.Status);
        Assert.Equal(
            CareerFlightReservationReleaseStatus.AlreadyReleased,
            retry.ReservationRelease.Status);
        Assert.Null(
            context.Store.Checkpoint);
        Assert.Null(
            context.Sessions.Current);
        Assert.Equal(
            2,
            context.Store.ClearCount);
        Assert.Equal(
            1,
            context.Fleet.ReleaseCount);
    }

    [Fact]
    public async Task ReplayAfterSuccessfulFinalizationIsNoOp()
    {
        LogbookEntry entry =
            CareerEntry();

        TestContext context =
            CreateContext(
                entry);

        await context.Finalizer.FinalizeAsync(
            new LogbookAppendResult(
                LogbookAppendDisposition.Appended,
                entry),
            AppliedProfile(
                entry));

        int clearCount =
            context.Store.ClearCount;

        CareerFlightFinalizationResult replay =
            await context.Finalizer.FinalizeAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.AlreadyExists,
                    entry),
                AppliedProfile(
                    entry));

        Assert.Equal(
            CareerFlightFinalizationStatus.AlreadyFinalized,
            replay.Status);
        Assert.Equal(
            CareerFlightReservationReleaseStatus.AlreadyReleased,
            replay.ReservationRelease.Status);
        Assert.Equal(
            clearCount,
            context.Store.ClearCount);
        Assert.Null(
            context.Sessions.Current);
    }

    [Fact]
    public async Task DifferentCurrentSessionCannotReleaseOrClear()
    {
        LogbookEntry entry =
            CareerEntry();

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleet(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        var store =
            new MemoryStore();

        var sessions =
            new FlightSessionCoordinator();

        FlightSession different =
            CompletedSession(
                Guid.Parse(
                    "9e000000-0000-0000-0000-000000000099"));

        sessions.Restore(
            different);
        store.Checkpoint =
            different;

        var persistence =
            new FlightSessionPersistenceService(
                sessions,
                store);

        var finalizer =
            new CareerFlightFinalizationCoordinator(
                new CareerFlightReservationReleaseCoordinator(
                    fleet,
                    fleet),
                persistence,
                sessions);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => finalizer.FinalizeAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                AppliedProfile(
                    entry)));

        Assert.Equal(
            0,
            fleet.ReleaseCount);
        Assert.Equal(
            0,
            store.ClearCount);
        Assert.Equal(
            different,
            sessions.Current);
    }

    private static TestContext CreateContext(
        LogbookEntry entry)
    {
        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleet(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        FlightSession session =
            CompletedSession(
                entry.Debrief.ContractId!.Value);

        var store =
            new MemoryStore
            {
                Checkpoint =
                    session
            };

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            session);

        var persistence =
            new FlightSessionPersistenceService(
                sessions,
                store);

        var finalizer =
            new CareerFlightFinalizationCoordinator(
                new CareerFlightReservationReleaseCoordinator(
                    fleet,
                    fleet),
                persistence,
                sessions);

        return new(
            finalizer,
            fleet,
            store,
            sessions);
    }

    private static PlayerCareerProfileStoreRecord AppliedProfile(
        LogbookEntry entry)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "9e000000-0000-0000-0000-000000000001"),
                "KRME",
                Epoch.AddHours(-1))
            with
            {
                AppliedExperienceDebriefIds =
                    ImmutableHashSet<Guid>.Empty
                        .Add(
                            entry.Debrief.DebriefId)
            };

        profile.Validate();

        return new(
            Revision: 2,
            profile,
            SavedAt:
                entry.CommittedAt.AddMinutes(1));
    }

    private static LogbookEntry CareerEntry()
    {
        Guid contractId =
            Guid.Parse(
                "9e000000-0000-0000-0000-000000000002");

        FlightSession session =
            CompletedSession(
                contractId);

        DateTimeOffset endedAt =
            session.Milestones.CompletedAt!.Value;

        var route =
            new FlightRouteDebrief(
                "KRME",
                "KSYR",
                "KRME",
                "KSYR",
                null,
                80);

        var leg =
            new FlightLegDebrief(
                session.SessionId,
                Sequence: 1,
                session.CreatedAt,
                endedAt,
                route,
                session.TimeLedger,
                Array.Empty<FlightTrackPoint>(),
                LandingEpisodeNumbers:
                    [1]);

        var landing =
            new LandingDebrief(
                EpisodeNumber: 1,
                Timestamp:
                    session.Milestones.FirstTouchdownAt!.Value,
                LandingOperationType.FullStop,
                BounceCount: 0,
                VerticalSpeedFeetPerMinute:
                    null,
                TouchdownG:
                    null,
                IndicatedAirspeedKnots:
                    null,
                PitchDegrees:
                    null,
                BankDegrees:
                    null,
                HardLanding:
                    null,
                EvidenceQuality:
                    EvidenceQuality.DerivedHighConfidence);

        FlightDebrief debrief =
            FlightDebriefFactory.Create(
                new FlightDebriefDraft(
                    DebriefId:
                        session.SessionId,
                    SessionId:
                        session.SessionId,
                    ContractId:
                        contractId,
                    EntryKind:
                        LogbookEntryKind.CareerJob,
                    StartedAt:
                        session.CreatedAt,
                    EndedAt:
                        endedAt,
                    Route:
                        route,
                    Aircraft:
                        new AircraftDebrief(
                            "Integration Aircraft"),
                    Time:
                        session.TimeLedger,
                    Tracking:
                        session.Tracking,
                    Legs:
                        [leg],
                    Fuel:
                        new FlightFuelDebrief(
                            null,
                            null,
                            null,
                            EvidenceQuality.Unavailable),
                    Payload:
                        new PayloadDebrief(
                            null,
                            500,
                            "Cargo",
                            "Delivered",
                            EvidenceQuality.MissionDeclared),
                    Assistance:
                        new FlightAssistanceDebrief(
                            false,
                            false,
                            false,
                            false,
                            false),
                    SafetyOutcome:
                        FlightSafetyOutcome.CompletedNormally,
                    MissionOutcome:
                        MissionOutcome.Succeeded,
                    Landings:
                        [landing],
                    Events:
                        Array.Empty<FlightDebriefEvent>(),
                    Settlement:
                        new FlightSettlementRecord(
                            SettlementRecordStatus.Settled,
                            $"contract:{contractId:D}:settlement-v1",
                            contractId.ToString("D"),
                            endedAt.AddMinutes(1),
                            900m,
                            2d)));

        return LogbookEntry.Commit(
            session.SessionId,
            debrief,
            endedAt.AddMinutes(2),
            LogbookCommitKind.AutomaticCareerSettlement);
    }

    private static FlightSession CompletedSession(
        Guid contractId)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId,
                plan:
                    new FlightSessionPlan(
                        "KRME",
                        "KSYR"));

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

    private sealed record TestContext(
        CareerFlightFinalizationCoordinator Finalizer,
        FakeFleet Fleet,
        MemoryStore Store,
        FlightSessionCoordinator Sessions);

    private sealed class FakeFleet(
        AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup,
          IAircraftReservationStore
    {
        public AircraftReservationOwnership? Ownership { get; private set; } =
            ownership;

        public int ReleaseCount { get; private set; }

        public AircraftReservationReleaseResult ReleaseResult { get; set; } =
            AircraftReservationReleaseResult.Released;

        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Ownership is not null
                && string.Equals(
                    Ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal)
                    ? Ownership
                    : null);
        }

        public Task<AircraftReservationAcquireResult> TryReserveAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;

            if (ReleaseResult
                is AircraftReservationReleaseResult.Released
                    or AircraftReservationReleaseResult.AlreadyReleased)
            {
                Ownership =
                    null;
            }

            return Task.FromResult(
                ReleaseResult);
        }
    }

    private sealed class MemoryStore
        : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; set; }

        public int ClearCount { get; private set; }

        public bool FailNextClear { get; set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint =
                session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Checkpoint);
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearCount++;

            if (FailNextClear)
            {
                FailNextClear =
                    false;
                throw new IOException(
                    "Synthetic clear failure.");
            }

            Checkpoint =
                null;
            return Task.CompletedTask;
        }
    }
}
