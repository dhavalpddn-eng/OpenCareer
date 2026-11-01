using OpenCareer.Application.Careers;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class CareerLogbookExperienceCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SettledCareerLogbookEntryAppliesExperience()
    {
        var store =
            new FakeProfileStore(
                ExistingProfile());

        var runtime =
            new PlayerCareerRuntimeState(
                store);

        var coordinator =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    store,
                    runtime,
                    new FakeContractStore()));

        LogbookEntry entry =
            CareerEntry();

        PlayerCareerProfileStoreRecord saved =
            await coordinator.ApplyAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                entry.CommittedAt.AddMinutes(1));

        Assert.Equal(
            1,
            saved.Profile.Experience.FlightCount);
        Assert.Equal(
            TimeSpan.FromMinutes(75),
            saved.Profile.Experience.CareerCreditTime);
        Assert.Equal(
            1,
            saved.Profile.Experience.TakeoffCount);
        Assert.Equal(
            1,
            saved.Profile.Experience.LandingEpisodeCount);
        Assert.Contains(
            entry.Debrief.DebriefId,
            saved.Profile.AppliedExperienceDebriefIds);
        Assert.Equal(
            1,
            store.SaveCount);
    }

    [Fact]
    public async Task ReplayedLogbookResultDoesNotApplyExperienceTwice()
    {
        var store =
            new FakeProfileStore(
                ExistingProfile());

        var runtime =
            new PlayerCareerRuntimeState(
                store);

        var coordinator =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    store,
                    runtime,
                    new FakeContractStore()));

        LogbookEntry entry =
            CareerEntry();

        PlayerCareerProfileStoreRecord first =
            await coordinator.ApplyAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                entry.CommittedAt.AddMinutes(1));

        PlayerCareerProfileStoreRecord replay =
            await coordinator.ApplyAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.AlreadyExists,
                    entry),
                entry.CommittedAt.AddMinutes(2));

        Assert.Same(
            first,
            replay);
        Assert.Equal(
            1,
            replay.Profile.Experience.FlightCount);
        Assert.Equal(
            1,
            store.SaveCount);
    }

    [Fact]
    public async Task CareerJobExperienceWithoutContractAuthorityFailsClosed()
    {
        var store =
            new FakeProfileStore(
                ExistingProfile());

        var coordinator =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    store,
                    new PlayerCareerRuntimeState(
                        store)));

        LogbookEntry entry =
            CareerEntry();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                entry.CommittedAt.AddMinutes(1)));

        Assert.Equal(
            0,
            store.SaveCount);
    }

    [Fact]
    public async Task ManualLogbookEntryCannotApplyCareerJobExperience()
    {
        var store =
            new FakeProfileStore(
                ExistingProfile());

        var runtime =
            new PlayerCareerRuntimeState(
                store);

        var coordinator =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    store,
                    runtime,
                    new FakeContractStore()));

        LogbookEntry manual =
            ManualEntry();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ApplyAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    manual),
                manual.CommittedAt.AddMinutes(1)));

        Assert.Equal(
            0,
            store.SaveCount);
    }

    [Fact]
    public async Task ExperienceSaveCannotPredateLogbookCommit()
    {
        var store =
            new FakeProfileStore(
                ExistingProfile());

        var runtime =
            new PlayerCareerRuntimeState(
                store);

        var coordinator =
            new CareerLogbookExperienceCoordinator(
                new PlayerCareerExperienceCoordinator(
                    store,
                    runtime,
                    new FakeContractStore()));

        LogbookEntry entry =
            CareerEntry();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => coordinator.ApplyAsync(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry),
                entry.CommittedAt.AddTicks(-1)));

        Assert.Equal(
            0,
            store.SaveCount);
    }

    private static PlayerCareerProfileStoreRecord ExistingProfile()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "9c000000-0000-0000-0000-000000000001"),
                "KRME",
                Epoch.AddHours(-1));

        return new(
            Revision: 1,
            profile,
            SavedAt:
                Epoch.AddMinutes(-30));
    }

    private static LogbookEntry CareerEntry()
    {
        Guid contractId =
            Guid.Parse(
                "9c000000-0000-0000-0000-000000000002");

        FlightDebrief debrief =
            CreateDebrief(
                contractId,
                LogbookEntryKind.CareerJob,
                MissionOutcome.Succeeded,
                new FlightSettlementRecord(
                    SettlementRecordStatus.Settled,
                    $"contract:{contractId:D}:settlement-v1",
                    contractId.ToString("D"),
                    Epoch.AddMinutes(91),
                    900m,
                    2d));

        return LogbookEntry.Commit(
            debrief.SessionId,
            debrief,
            Epoch.AddMinutes(92),
            LogbookCommitKind.AutomaticCareerSettlement);
    }

    private static LogbookEntry ManualEntry()
    {
        FlightDebrief debrief =
            CreateDebrief(
                contractId:
                    null,
                LogbookEntryKind.FreeFlight,
                MissionOutcome.NotApplicable,
                FlightSettlementRecord.NotApplicable);

        return LogbookEntry.Commit(
            debrief.SessionId,
            debrief,
            Epoch.AddMinutes(92),
            LogbookCommitKind.ManualPilotLog);
    }

    private static FlightDebrief CreateDebrief(
        Guid? contractId,
        LogbookEntryKind entryKind,
        MissionOutcome missionOutcome,
        FlightSettlementRecord settlement)
    {
        Guid sessionId =
            Guid.NewGuid();

        DateTimeOffset startedAt =
            Epoch;
        DateTimeOffset endedAt =
            Epoch.AddMinutes(90);

        var time =
            FlightTimeLedger.Empty with
            {
                CareerCreditTime =
                    TimeSpan.FromMinutes(75),
                NightCareerCreditTime =
                    TimeSpan.FromMinutes(20),
                ActualInstrumentCareerCreditTime =
                    TimeSpan.FromMinutes(10)
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
                startedAt,
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

        return FlightDebriefFactory.Create(
            new FlightDebriefDraft(
                DebriefId:
                    sessionId,
                SessionId:
                    sessionId,
                ContractId:
                    contractId,
                EntryKind:
                    entryKind,
                StartedAt:
                    startedAt,
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
                    missionOutcome,
                Landings:
                    [landing],
                Events:
                    Array.Empty<FlightDebriefEvent>(),
                Settlement:
                    settlement));
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord current)
        : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord _current =
            current;

        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PlayerCareerProfileStoreRecord?>(
                _current);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;

            if (expectedRevision
                != _current.Revision)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Synthetic stale career revision.");
            }

            _current =
                new PlayerCareerProfileStoreRecord(
                    checked(_current.Revision + 1),
                    profile,
                    savedAt);

            _current.Validate();
            return Task.FromResult(
                _current);
        }
    }

    private sealed class FakeContractStore
        : IJobContractStore
    {
        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contract =
                new JobContract(
                    ContractId:
                        contractId,
                    EmployerId:
                        null,
                    Kind:
                        ContractKind.Ferry,
                    ServiceTrack:
                        ServiceTrack.CivilianEmployment,
                    OriginIcao:
                        "KRME",
                    DestinationIcao:
                        "KSYR",
                    Compensation:
                        new ContractCompensation(
                            CompensationModel.PilotWage,
                            GrossCustomerRevenue:
                                900m,
                            PilotCompensation:
                                900m,
                            EmployerCoversFuel:
                                true,
                            EmployerCoversMaintenance:
                                true,
                            EmployerCoversAirportFees:
                                true),
                    OfferedAt:
                        Epoch.AddHours(-1),
                    MustStartBy:
                        null,
                    MustCompleteBy:
                        null,
                    AircraftRequirements:
                        new AircraftMissionRequirements(),
                    Status:
                        ContractStatus.Completed,
                    AcceptedAt:
                        Epoch.AddMinutes(-30),
                    StartedAt:
                        Epoch,
                    CompletedAt:
                        Epoch.AddMinutes(90));

            contract.Validate();

            return Task.FromResult<PersistedJobContract?>(
                new PersistedJobContract(
                    contract,
                    Version:
                        1));
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
