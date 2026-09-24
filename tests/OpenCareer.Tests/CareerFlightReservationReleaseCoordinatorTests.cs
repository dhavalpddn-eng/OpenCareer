using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class CareerFlightReservationReleaseCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppliedCareerExperienceReleasesContractReservation()
    {
        LogbookEntry entry =
            CareerEntry();

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleetReservationStore(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        var coordinator =
            new CareerFlightReservationReleaseCoordinator(
                fleet,
                fleet);

        CareerFlightReservationReleaseResult result =
            await coordinator.ReleaseAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                AppliedProfile(
                    entry));

        Assert.Equal(
            CareerFlightReservationReleaseStatus.Released,
            result.Status);
        Assert.Equal(
            reservationId,
            result.ReservationId);
        Assert.Equal(
            "canonical-aircraft",
            result.CanonicalAircraftId);
        Assert.Equal(
            1,
            fleet.ReleaseCount);
        Assert.Null(
            fleet.Ownership);
    }

    [Fact]
    public async Task SubMillisecondLogbookCommitCannotMakePersistedAppliedProfilePredateReleaseAuthority()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            LogbookEntry baseEntry =
                CareerEntry();

            LogbookEntry entry =
                baseEntry with
                {
                    CommittedAt =
                        baseEntry.CommittedAt.AddTicks(4_321)
                };

            var profileStore =
                new SqlitePlayerCareerProfileStore(
                    new OpenCareerDatabaseOptions(
                        Path.Combine(directory, "opencareer.db")),
                    NullLogger<SqlitePlayerCareerProfileStore>.Instance);

            PlayerCareerProfile initial =
                PlayerCareerProfile.Start(
                    Guid.Parse("9d000000-0000-0000-0000-000000000099"),
                    "KRME",
                    Epoch.AddHours(-1));

            await profileStore.SaveAsync(
                initial,
                expectedRevision: null,
                savedAt: Epoch);

            var runtime =
                new PlayerCareerRuntimeState(
                    profileStore);

            PlayerCareerProfileStoreRecord applied =
                await new PlayerCareerExperienceCoordinator(
                        profileStore,
                        runtime)
                    .ApplyCommittedAsync(
                        entry,
                        savedAt: entry.CommittedAt);

            string reservationId =
                JobAcceptanceFleetBridge.GetReservationId(
                    entry.Debrief.ContractId!.Value);

            var fleet =
                new FakeFleetReservationStore(
                    new AircraftReservationOwnership(
                        "canonical-aircraft",
                        reservationId));

            CareerFlightReservationReleaseResult released =
                await new CareerFlightReservationReleaseCoordinator(
                        fleet,
                        fleet)
                    .ReleaseAsync(
                        new LogbookAppendResult(
                            LogbookAppendDisposition.Appended,
                            entry),
                        applied);

            Assert.True(
                applied.SavedAt >= entry.CommittedAt);
            Assert.Equal(
                CareerFlightReservationReleaseStatus.Released,
                released.Status);
        }
        finally
        {
            try
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
            catch
            {
                // Cleanup must not make a passing SQLite assertion platform-specific.
            }
        }
    }

    [Fact]
    public async Task ReplayAfterReleaseConvergesWithoutSecondFleetWrite()
    {
        LogbookEntry entry =
            CareerEntry();

        var fleet =
            new FakeFleetReservationStore(
                ownership:
                    null);

        var coordinator =
            new CareerFlightReservationReleaseCoordinator(
                fleet,
                fleet);

        CareerFlightReservationReleaseResult result =
            await coordinator.ReleaseAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.AlreadyExists,
                    entry),
                AppliedProfile(
                    entry));

        Assert.Equal(
            CareerFlightReservationReleaseStatus.AlreadyReleased,
            result.Status);
        Assert.Equal(
            0,
            fleet.ReleaseCount);
    }

    [Fact]
    public async Task ExperienceMustBeAppliedBeforeFleetRelease()
    {
        LogbookEntry entry =
            CareerEntry();

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleetReservationStore(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        var profile =
            AppliedProfile(
                entry) with
            {
                Profile =
                    AppliedProfile(entry).Profile with
                    {
                        AppliedExperienceDebriefIds =
                            ImmutableHashSet<Guid>.Empty
                    }
            };

        var coordinator =
            new CareerFlightReservationReleaseCoordinator(
                fleet,
                fleet);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ReleaseAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                profile));

        Assert.Equal(
            0,
            fleet.ReleaseCount);
        Assert.NotNull(
            fleet.Ownership);
    }

    [Fact]
    public async Task AppliedProfileTimestampBeforeLogbookCommitCannotReleaseFleet()
    {
        LogbookEntry entry =
            CareerEntry();

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleetReservationStore(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        PlayerCareerProfileStoreRecord stale =
            AppliedProfile(
                entry) with
            {
                SavedAt =
                    entry.CommittedAt.AddTicks(-1)
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CareerFlightReservationReleaseCoordinator(
                    fleet,
                    fleet)
                .ReleaseAsync(
                    new LogbookAppendResult(
                        LogbookAppendDisposition.Appended,
                        entry),
                    stale));

        Assert.Equal(
            0,
            fleet.ReleaseCount);
        Assert.NotNull(
            fleet.Ownership);
    }

    [Fact]
    public async Task WrongSettlementKeyCannotReleaseFleetReservation()
    {
        LogbookEntry entry =
            CareerEntry();

        FlightDebrief debrief =
            entry.Debrief with
            {
                Settlement =
                    entry.Debrief.Settlement with
                    {
                        IdempotencyKey =
                            "contract:wrong:settlement-v1"
                    }
            };

        LogbookEntry mismatched =
            entry with
            {
                Debrief =
                    debrief
            };

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleetReservationStore(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId));

        var coordinator =
            new CareerFlightReservationReleaseCoordinator(
                fleet,
                fleet);

        await Assert.ThrowsAnyAsync<Exception>(
            () => coordinator.ReleaseAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    mismatched),
                AppliedProfile(
                    mismatched)));

        Assert.Equal(
            0,
            fleet.ReleaseCount);
    }

    [Fact]
    public async Task ChangedReservationOwnershipCannotBeSilentlyReleased()
    {
        LogbookEntry entry =
            CareerEntry();

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                entry.Debrief.ContractId!.Value);

        var fleet =
            new FakeFleetReservationStore(
                new AircraftReservationOwnership(
                    "canonical-aircraft",
                    reservationId))
            {
                ReleaseResult =
                    AircraftReservationReleaseResult.HeldByAnotherReservation
            };

        var coordinator =
            new CareerFlightReservationReleaseCoordinator(
                fleet,
                fleet);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ReleaseAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                AppliedProfile(
                    entry)));

        Assert.Equal(
            1,
            fleet.ReleaseCount);
    }

    private static PlayerCareerProfileStoreRecord AppliedProfile(
        LogbookEntry entry)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "9d000000-0000-0000-0000-000000000001"),
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
                "9d000000-0000-0000-0000-000000000002");

        Guid sessionId =
            contractId;

        DateTimeOffset endedAt =
            Epoch.AddMinutes(90);

        var time =
            FlightTimeLedger.Empty with
            {
                CareerCreditTime =
                    TimeSpan.FromMinutes(75)
            };

        var tracking =
            FlightTrackingSnapshot.Start(
                endedAt) with
            {
                State =
                    FlightTrackingState.Complete,
                TakeoffCount =
                    1,
                LandingEpisodeCount =
                    1
            };

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
                sessionId,
                Sequence: 1,
                Epoch,
                endedAt,
                route,
                time,
                Array.Empty<FlightTrackPoint>(),
                LandingEpisodeNumbers:
                    [1]);

        var landing =
            new LandingDebrief(
                EpisodeNumber: 1,
                Timestamp:
                    endedAt.AddMinutes(-5),
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

        var settlement =
            new FlightSettlementRecord(
                SettlementRecordStatus.Settled,
                $"contract:{contractId:D}:settlement-v1",
                contractId.ToString("D"),
                endedAt.AddMinutes(1),
                900m,
                2d);

        FlightDebrief debrief =
            FlightDebriefFactory.Create(
                new FlightDebriefDraft(
                    DebriefId:
                        sessionId,
                    SessionId:
                        sessionId,
                    ContractId:
                        contractId,
                    EntryKind:
                        LogbookEntryKind.CareerJob,
                    StartedAt:
                        Epoch,
                    EndedAt:
                        endedAt,
                    Route:
                        route,
                    Aircraft:
                        new AircraftDebrief(
                            "Integration Aircraft"),
                    Time:
                        time,
                    Tracking:
                        tracking,
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
                        settlement));

        return LogbookEntry.Commit(
            sessionId,
            debrief,
            endedAt.AddMinutes(2),
            LogbookCommitKind.AutomaticCareerSettlement);
    }

    private sealed class FakeFleetReservationStore(
        AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup,
          IAircraftReservationStore
    {
        public AircraftReservationOwnership? Ownership { get; private set; } =
            ownership;

        public int ReleaseCount { get; private set; }

        public AircraftReservationReleaseResult ReleaseResult { get; init; } =
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
                Ownership = null;
            }

            return Task.FromResult(
                ReleaseResult);
        }
    }
}
