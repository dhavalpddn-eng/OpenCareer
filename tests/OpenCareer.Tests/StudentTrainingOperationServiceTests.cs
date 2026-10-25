using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenCareer.Tests;

public sealed class StudentTrainingOperationServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("71000000-0000-0000-0000-000000000001");

    private const string AircraftId = "msfs-title:training-aircraft";

    [Fact]
    public async Task StartCreatesPersistedStudentLocalCircuit()
    {
        Fixture fixture = Fixture.Create();

        StudentTrainingOperationStartResult result =
            await fixture.Subject.StartAsync();

        Assert.Equal(
            StudentTrainingOperationService.CreateOperationIdentity(
                result.FlightSession.SessionId),
            result.OperationIdentity);
        Assert.Equal("Training Aircraft", result.AircraftDisplayName);
        Assert.Equal(result.FlightSession, fixture.Checkpoints.Checkpoint);
        Assert.Equal(result.FlightSession, fixture.Sessions.Current);
        Assert.Null(result.FlightSession.ContractId);
        Assert.Equal("KDFW", result.FlightSession.Plan!.PlannedOrigin);
        Assert.Equal("KDFW", result.FlightSession.Plan.PlannedDestination);
        Assert.Equal(
            StudentTrainingOperationService.SourceProvider,
            result.FlightSession.Plan.SourceProvider);
        Assert.Equal(AircraftId, result.FlightSession.Plan.ExpectedCanonicalAircraftId);
        Assert.Contains(
            CareerId.ToString("N"),
            Assert.IsType<string>(result.FlightSession.Plan.SourceReference),
            StringComparison.Ordinal);
        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(PilotLicenseLevel.None, fixture.Profiles.Record.Profile.Qualifications.License);
    }

    [Fact]
    public async Task ReadinessProjectsVerifiedAircraftWithoutWritingAuthority()
    {
        Fixture fixture = Fixture.Create();

        StudentTrainingOperationReadiness ready =
            await fixture.Subject.ReadAsync();

        Assert.Equal(StudentTrainingOperationReadinessState.Ready, ready.State);
        Assert.True(ready.CanStart);
        Assert.Equal("KDFW", ready.AirportIcao);
        Assert.Equal("Training Aircraft", ready.AircraftDisplayName);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Equal(0, fixture.Logbook.AppendCount);

        StudentTrainingOperationStartResult started =
            await fixture.Subject.StartAsync();
        StudentTrainingOperationReadiness active =
            await fixture.Subject.ReadAsync();

        Assert.Equal(StudentTrainingOperationReadinessState.Active, active.State);
        Assert.False(active.CanStart);
        Assert.Equal(started.FlightSession.SessionId, active.OperationId);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Equal(0, fixture.Logbook.AppendCount);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenTrainingCapabilityIsNotVerified()
    {
        Fixture fixture = Fixture.Create(trainingCapable: false);

        StudentTrainingOperationReadiness readiness =
            await fixture.Subject.ReadAsync();

        Assert.Equal(
            StudentTrainingOperationReadinessState.AircraftNotEligible,
            readiness.State);
        Assert.False(readiness.CanStart);
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(0, fixture.Profiles.SaveCount);
    }

    [Fact]
    public async Task CompletedOperationCommitsEvidenceAndAwardsStudent()
    {
        Fixture fixture = Fixture.Create();
        StudentTrainingOperationStartResult started =
            await fixture.Subject.StartAsync();
        fixture.CompleteCurrent(FlightSessionLandingKind.FullStop);

        StudentTrainingOperationCompletionResult result =
            await fixture.Subject.CompleteAsync(started.FlightSession.SessionId);

        Assert.Equal(LogbookAppendDisposition.Appended, result.Logbook.Disposition);
        Assert.Equal(CivilianQualificationAwardStatus.Awarded, result.Award.Status);
        Assert.Equal(PilotLicenseLevel.Student, fixture.Profiles.Record.Profile.Qualifications.License);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
        Assert.Equal(1, fixture.Logbook.AppendCount);

        FlightDebrief debrief = result.Logbook.Entry.Debrief;
        Assert.Equal(LogbookEntryKind.Training, debrief.EntryKind);
        Assert.Equal(MissionOutcome.Succeeded, debrief.MissionOutcome);
        Assert.Equal(SettlementRecordStatus.NotApplicable, debrief.Settlement.Status);
        CivilianQualificationTrainingMetadata metadata =
            Assert.IsType<CivilianQualificationTrainingMetadata>(
                debrief.QualificationTraining);
        Assert.Equal(CareerId, metadata.CareerId);
        Assert.Equal(started.OperationIdentity, metadata.TrainingOperationIdentity);
        Assert.Equal("license.student", metadata.TargetQualificationIdentity);
    }

    [Fact]
    public async Task RuntimeFinalizesRecoveredCompletedTrainingWithoutUiAuthority()
    {
        Fixture fixture = Fixture.Create();
        await fixture.Subject.StartAsync();
        fixture.CompleteCurrent(FlightSessionLandingKind.FullStop);

        using var runtime = new StudentTrainingOperationRuntime(
            fixture.Sessions,
            fixture.Subject,
            NullLogger<StudentTrainingOperationRuntime>.Instance);

        Assert.True(await runtime.InitializeAsync());
        Assert.Equal(
            PilotLicenseLevel.Student,
            fixture.Profiles.Record.Profile.Qualifications.License);
        Assert.Equal(1, fixture.Logbook.AppendCount);
        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
    }

    [Fact]
    public async Task DuplicateAndRestartStyleReplayDoNotDuplicateEvidenceOrAward()
    {
        Fixture fixture = Fixture.Create();
        StudentTrainingOperationStartResult started =
            await fixture.Subject.StartAsync();
        fixture.CompleteCurrent(FlightSessionLandingKind.FullStop);

        StudentTrainingOperationCompletionResult first =
            await fixture.Subject.CompleteAsync(started.FlightSession.SessionId);
        int profileWrites = fixture.Profiles.SaveCount;

        StudentTrainingOperationCompletionResult replay =
            await fixture.Subject.CompleteAsync(started.FlightSession.SessionId);

        Assert.Equal(LogbookAppendDisposition.Appended, first.Logbook.Disposition);
        Assert.Equal(LogbookAppendDisposition.AlreadyExists, replay.Logbook.Disposition);
        Assert.Equal(CivilianQualificationAwardStatus.AlreadyEarned, replay.Award.Status);
        Assert.Equal(1, fixture.Logbook.AppendCount);
        Assert.Equal(profileWrites, fixture.Profiles.SaveCount);
    }

    [Fact]
    public async Task TouchAndGoWithoutFullStopCannotCreateEvidence()
    {
        Fixture fixture = Fixture.Create();
        StudentTrainingOperationStartResult started =
            await fixture.Subject.StartAsync();
        fixture.CompleteCurrent(FlightSessionLandingKind.TouchAndGo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subject.CompleteAsync(started.FlightSession.SessionId));

        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Equal(PilotLicenseLevel.None, fixture.Profiles.Record.Profile.Qualifications.License);
    }

    [Fact]
    public async Task UntrustedSessionSourceCannotCreateEvidence()
    {
        Fixture fixture = Fixture.Create();
        StudentTrainingOperationStartResult started =
            await fixture.Subject.StartAsync();
        fixture.CompleteCurrent(
            FlightSessionLandingKind.FullStop,
            sourceProvider: "OpenCareer.FreeFlight");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subject.CompleteAsync(started.FlightSession.SessionId));

        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(PilotLicenseLevel.None, fixture.Profiles.Record.Profile.Qualifications.License);
    }

    [Fact]
    public async Task StartFailsClosedWithoutAuthoritativeTrainingCapability()
    {
        Fixture fixture = Fixture.Create(trainingCapable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Subject.StartAsync());

        Assert.Null(fixture.Sessions.Current);
        Assert.Null(fixture.Checkpoints.Checkpoint);
        Assert.Equal(0, fixture.Profiles.SaveCount);
    }

    private sealed class Fixture
    {
        private Fixture(
            StudentTrainingOperationService subject,
            FlightSessionCoordinator sessions,
            FakeCheckpointStore checkpoints,
            FakeLogbook logbook,
            FakeProfileStore profiles,
            AdjustableTimeProvider clock)
        {
            Subject = subject;
            Sessions = sessions;
            Checkpoints = checkpoints;
            Logbook = logbook;
            Profiles = profiles;
            Clock = clock;
        }

        public StudentTrainingOperationService Subject { get; }
        public FlightSessionCoordinator Sessions { get; }
        public FakeCheckpointStore Checkpoints { get; }
        public FakeLogbook Logbook { get; }
        public FakeProfileStore Profiles { get; }
        private AdjustableTimeProvider Clock { get; }

        public static Fixture Create(bool trainingCapable = true)
        {
            var profiles = new FakeProfileStore();
            var career = new PlayerCareerRuntimeState(profiles);
            var sessions = new FlightSessionCoordinator();
            var checkpoints = new FakeCheckpointStore();
            var persistence = new FlightSessionPersistenceService(sessions, checkpoints);
            var logbook = new FakeLogbook();
            var experience = new PlayerCareerExperienceCoordinator(profiles, career);
            var postflight = new ManualFlightPostflightCoordinator(
                sessions,
                persistence,
                new LogbookCommitCoordinator(logbook),
                logbook,
                experience);
            var catalog = CivilianQualificationCatalog.Default;
            var qualificationCoordinator =
                new PlayerCareerQualificationCoordinator(profiles, career);
            var evidence =
                new LogbookCivilianTrainingCompletionEvidenceSource(
                    logbook,
                    catalog);
            var clock = new AdjustableTimeProvider(Epoch.AddHours(1));
            var awards = new CivilianQualificationAwardService(
                career,
                evidence,
                catalog,
                qualificationCoordinator,
                clock);
            var subject = new StudentTrainingOperationService(
                career,
                catalog,
                new FakeLoadedAircraft(),
                new FakeRegistry(trainingCapable),
                persistence,
                sessions,
                postflight,
                logbook,
                awards,
                clock);

            return new(subject, sessions, checkpoints, logbook, profiles, clock);
        }

        public void CompleteCurrent(
            FlightSessionLandingKind landingKind,
            string? sourceProvider = null)
        {
            FlightSession session = Sessions.Current
                ?? throw new InvalidOperationException("Expected training session.");
            DateTimeOffset completedAt = Epoch.AddHours(2);
            Clock.UtcNow = Epoch.AddHours(3);
            var landing = new FlightSessionLandingEpisode(
                1,
                completedAt.AddMinutes(-5),
                landingKind,
                BounceCount: 0,
                CompletedAt: completedAt.AddMinutes(-4));
            FlightLeg leg = session.EffectiveLegs[0]
                .ReferenceLandingEpisode(1)
                .Complete(completedAt);
            FlightSessionPlan plan = sourceProvider is null
                ? session.Plan!
                : session.Plan! with { SourceProvider = sourceProvider };
            FlightSession completed = session with
            {
                UpdatedAt = completedAt,
                Status = FlightSessionStatus.Completed,
                OperationState = FlightOperationState.Complete,
                Tracking = session.Tracking with
                {
                    State = FlightTrackingState.Complete,
                    UpdatedAt = completedAt,
                    TakeoffCount = 1,
                    LandingEpisodeCount = 1
                },
                Milestones = session.Milestones with { CompletedAt = completedAt },
                Plan = plan,
                LandingEpisodes = [landing],
                Legs = [leg with { Plan = plan }]
            };

            if (sourceProvider is null)
            {
                Sessions.CommitPersisted(completed);
            }
            else
            {
                FlightSession cancelled =
                    FlightSessionEngine.Advance(
                        session,
                        new FlightSessionAdvance(
                            new FlightStateEvidence(
                                session.UpdatedAt.AddTicks(1),
                                Connected: true),
                            CancelRequested: true));

                Sessions.CommitPersisted(cancelled);
                Sessions.ClearTerminalSession();
                Sessions.Restore(completed);
            }

            Checkpoints.SaveAsync(completed).GetAwaiter().GetResult();
        }
    }

    private sealed class FakeLoadedAircraft : ICurrentLoadedAircraftIdentitySource
    {
        public CurrentLoadedAircraftIdentitySnapshot Current { get; } =
            CurrentLoadedAircraftIdentitySnapshot.Identified(AircraftId);
    }

    private sealed class FakeRegistry(bool trainingCapable) : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AircraftCapability capabilities = trainingCapable
                ? AircraftCapability.Training
                : AircraftCapability.Passenger;
            AircraftRegistryResolution resolution = AircraftRegistryResolver.Resolve(
            [
                new AircraftRegistryObservation(
                    AircraftId,
                    "test",
                    "training-aircraft",
                    AircraftDataConfidence.Verified,
                    IsInstalled: true,
                    DisplayName: "Training Aircraft",
                    Capabilities: capabilities)
            ]);
            return Task.FromResult<AircraftRegistryResolution?>(resolution);
        }
    }

    private sealed class FakeCheckpointStore : IFlightSessionCheckpointStore
    {
        public FlightSession? Checkpoint { get; private set; }

        public Task SaveAsync(
            FlightSession session,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint = session;
            return Task.CompletedTask;
        }

        public Task<FlightSession?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Checkpoint);
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checkpoint = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLogbook
        : ILogbookSource, ILogbookIdempotencySource, ILogbookWriter
    {
        private readonly Dictionary<Guid, LogbookEntry> _entries = [];
        private readonly Dictionary<string, Guid> _keys = [];

        public int AppendCount { get; private set; }

        public Task<IReadOnlyList<LogbookEntry>> QueryAsync(
            LogbookQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                LogbookQueryMatcher.Apply(_entries.Values, query));
        }

        public Task<LogbookEntry?> GetAsync(
            Guid entryId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entries.TryGetValue(entryId, out LogbookEntry? entry);
            return Task.FromResult(entry);
        }

        public Task<LogbookEntry?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _keys.TryGetValue(idempotencyKey, out Guid entryId)
                    ? _entries[entryId]
                    : null);
        }

        public Task<LogbookAppendResult> TryAppendAsync(
            LogbookEntry entry,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_keys.TryGetValue(idempotencyKey, out Guid existingId))
            {
                return Task.FromResult(
                    new LogbookAppendResult(
                        LogbookAppendDisposition.AlreadyExists,
                        _entries[existingId]));
            }

            _entries.Add(entry.EntryId, entry);
            _keys.Add(idempotencyKey, entry.EntryId);
            AppendCount++;
            return Task.FromResult(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry));
        }
    }

    private sealed class FakeProfileStore : IPlayerCareerProfileStore
    {
        public PlayerCareerProfileStoreRecord Record { get; private set; } =
            new(
                1,
                PlayerCareerProfile.Start(CareerId, "KDFW", Epoch),
                Epoch);

        public int SaveCount { get; private set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<PlayerCareerProfileStoreRecord?>(Record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedRevision != Record.Revision)
                throw new PlayerCareerProfileConcurrencyException("Stale test profile.");

            Record = new(Record.Revision + 1, profile, savedAt);
            SaveCount++;
            return Task.FromResult(Record);
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
