using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class ManualFlightPostflightCoordinatorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LogCommitsCreditsAndClearsExactlyOnceAcrossRetry()
    {
        Fixture fixture = Fixture.Create();
        fixture.Checkpoints.FailNextClear = true;

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Subject.LogAsync(fixture.SessionId, Request()));

        Assert.Equal(1, fixture.Logbook.AppendCount);
        Assert.Equal(1, fixture.Profiles.SaveCount);
        Assert.NotNull(fixture.Sessions.Current);

        Fixture restarted = await fixture.RestartAsync();
        ManualFlightPostflightLogResult retried = await restarted.Subject
            .LogAsync(restarted.SessionId, Request());

        Assert.Equal(LogbookAppendDisposition.AlreadyExists, retried.Logbook.Disposition);
        Assert.Equal(1, fixture.Logbook.AppendCount);
        Assert.Equal(1, fixture.Profiles.SaveCount);
        Assert.Null(restarted.Sessions.Current);
        Assert.Equal(1, retried.CareerProfile.Profile.Experience.FlightCount);
    }

    [Fact]
    public async Task FailedExperienceLeavesCommittedLogAndCheckpointRetryable()
    {
        Fixture fixture = Fixture.Create();
        fixture.Profiles.FailNextSave = true;

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Subject.LogAsync(fixture.SessionId, Request()));

        Assert.Equal(1, fixture.Logbook.AppendCount);
        Assert.NotNull(fixture.Sessions.Current);
        await fixture.Subject.LogAsync(fixture.SessionId, Request());
        Assert.Equal(1, fixture.Logbook.AppendCount);
        Assert.Equal(2, fixture.Profiles.SaveCount);
        Assert.Null(fixture.Sessions.Current);
    }

    [Fact]
    public async Task FailedLogbookLeavesExperienceAndCheckpointUntouched()
    {
        Fixture fixture = Fixture.Create();
        fixture.Logbook.FailNextAppend = true;

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Subject.LogAsync(fixture.SessionId, Request()));

        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.NotNull(fixture.Sessions.Current);
        await fixture.Subject.LogAsync(fixture.SessionId, Request());
        Assert.Equal(2, fixture.Logbook.AppendAttempts);
        Assert.Equal(1, fixture.Logbook.AppendCount);
    }

    [Fact]
    public async Task DiscardWritesNeitherLogbookNorExperienceAndClearsCheckpoint()
    {
        Fixture fixture = Fixture.Create();

        await fixture.Subject.DiscardAsync(fixture.SessionId);

        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(1, fixture.Checkpoints.ClearCount);
    }

    [Fact]
    public async Task UnsuccessfulTerminalManualSessionCanBeDiscarded()
    {
        Fixture fixture = Fixture.Create(interrupted: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Subject.LogAsync(fixture.SessionId, Request()));
        await fixture.Subject.DiscardAsync(fixture.SessionId);

        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(1, fixture.Checkpoints.ClearCount);
    }

    [Fact]
    public async Task ActiveManualSessionIsCancelledThenDiscardedWithoutCredit()
    {
        Fixture fixture = Fixture.Create(
            completed: false,
            now: Epoch.AddMinutes(20));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Subject.LogAsync(fixture.SessionId, Request()));
        await fixture.Subject.DiscardAsync(fixture.SessionId);

        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Null(fixture.Sessions.Current);
        Assert.Equal(1, fixture.Checkpoints.SaveCount);
        Assert.Equal(1, fixture.Checkpoints.ClearCount);
        Assert.Equal(FlightSessionStatus.Cancelled, fixture.Checkpoints.LastSaved?.Status);
        Assert.Equal(FlightOperationState.Cancelled, fixture.Checkpoints.LastSaved?.OperationState);
        Assert.Equal(Epoch.AddMinutes(20), fixture.Checkpoints.LastSaved?.UpdatedAt);
        Assert.Null(fixture.Checkpoints.LastSaved?.ContractId);
    }

    [Fact]
    public async Task ContractLinkedSessionFailsClosed()
    {
        Fixture contract = Fixture.Create(contractId: Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => contract.Subject.LogAsync(contract.SessionId, Request()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => contract.Subject.DiscardAsync(contract.SessionId));

        Assert.Equal(0, contract.Logbook.AppendCount);
        Assert.Equal(0, contract.Checkpoints.ClearCount);
    }

    [Fact]
    public async Task StaleReviewedIdentityCannotLogOrDiscardReplacementSession()
    {
        Fixture fixture = Fixture.Create();
        Guid reviewedSessionId = fixture.SessionId;
        fixture.Sessions.ClearTerminalSession();
        FlightSession replacement = fixture.Sessions.Start(Epoch.AddHours(3));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Subject.LogAsync(reviewedSessionId, Request()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Subject.DiscardAsync(reviewedSessionId));

        Assert.Same(replacement, fixture.Sessions.Current);
        Assert.Equal(0, fixture.Logbook.AppendCount);
        Assert.Equal(0, fixture.Profiles.SaveCount);
        Assert.Equal(0, fixture.Checkpoints.SaveCount);
        Assert.Equal(0, fixture.Checkpoints.ClearCount);
    }

    private static ManualFlightPostflightLogRequest Request() =>
        new(
            new FlightSessionDebriefContext(
                LogbookEntryKind.FreeFlight,
                new AircraftDebrief("Practice Aircraft"),
                "KORD",
                "KMSN",
                null,
                new PayloadDebrief(null, null, null, null, EvidenceQuality.Unavailable),
                FlightSafetyOutcome.CompletedNormally,
                MissionOutcome.NotApplicable,
                FlightSettlementRecord.NotApplicable),
            Epoch.AddHours(2),
            Epoch.AddHours(2));

    private sealed class Fixture
    {
        private Fixture(
            ManualFlightPostflightCoordinator subject,
            FlightSessionCoordinator sessions,
            FakeCheckpointStore checkpoints,
            FakeLogbook logbook,
            FakeProfileStore profiles)
        {
            Subject = subject;
            Sessions = sessions;
            Checkpoints = checkpoints;
            Logbook = logbook;
            Profiles = profiles;
        }

        public ManualFlightPostflightCoordinator Subject { get; }
        public FlightSessionCoordinator Sessions { get; }
        public FakeCheckpointStore Checkpoints { get; }
        public FakeLogbook Logbook { get; }
        public FakeProfileStore Profiles { get; }
        public Guid SessionId =>
            Sessions.Current?.SessionId
            ?? throw new InvalidOperationException("Expected a current FlightSession.");

        public async Task<Fixture> RestartAsync()
        {
            FlightSession session = await Checkpoints.LoadAsync()
                ?? throw new InvalidOperationException("Expected retry checkpoint.");
            var sessions = new FlightSessionCoordinator();
            sessions.Restore(session);
            var persistence = new FlightSessionPersistenceService(sessions, Checkpoints);
            var runtime = new PlayerCareerRuntimeState(Profiles);
            var subject = new ManualFlightPostflightCoordinator(
                sessions,
                persistence,
                new LogbookCommitCoordinator(Logbook),
                Logbook,
                new PlayerCareerExperienceCoordinator(Profiles, runtime));
            return new(subject, sessions, Checkpoints, Logbook, Profiles);
        }

        public static Fixture Create(
            Guid? contractId = null,
            bool completed = true,
            bool interrupted = false,
            DateTimeOffset? now = null)
        {
            FlightSession session = FlightSession.Start(Epoch, contractId, plan: new("KORD", "KMSN"));
            if (interrupted)
            {
                DateTimeOffset ended = Epoch.AddMinutes(30);
                session = session with
                {
                    UpdatedAt = ended,
                    Status = FlightSessionStatus.Interrupted
                };
            }
            else if (completed)
            {
                DateTimeOffset ended = Epoch.AddHours(1);
                session = session with
                {
                    UpdatedAt = ended,
                    Status = FlightSessionStatus.Completed,
                    OperationState = FlightOperationState.Complete,
                    Tracking = session.Tracking with { State = FlightTrackingState.Complete, UpdatedAt = ended },
                    Milestones = session.Milestones with { CompletedAt = ended },
                    Legs = [session.EffectiveLegs[0].Complete(ended)]
                };
            }

            var sessions = new FlightSessionCoordinator();
            sessions.Restore(session);
            var checkpoints = new FakeCheckpointStore(session);
            var persistence = new FlightSessionPersistenceService(sessions, checkpoints);
            var logbook = new FakeLogbook();
            var profiles = new FakeProfileStore();
            var runtime = new PlayerCareerRuntimeState(profiles);
            var experience = new PlayerCareerExperienceCoordinator(profiles, runtime);
            var subject = new ManualFlightPostflightCoordinator(
                sessions,
                persistence,
                new LogbookCommitCoordinator(logbook),
                logbook,
                experience,
                new FixedTimeProvider(now ?? Epoch.AddHours(2)));
            return new(subject, sessions, checkpoints, logbook, profiles);
        }
    }

    private sealed class FakeLogbook : ILogbookWriter, ILogbookIdempotencySource
    {
        private LogbookEntry? _entry;
        public int AppendAttempts { get; private set; }
        public int AppendCount { get; private set; }
        public bool FailNextAppend { get; set; }

        public Task<LogbookEntry?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entry);

        public Task<LogbookAppendResult> TryAppendAsync(LogbookEntry entry, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            AppendAttempts++;
            if (FailNextAppend)
            {
                FailNextAppend = false;
                throw new IOException("Synthetic Logbook failure.");
            }
            _entry = entry;
            AppendCount++;
            return Task.FromResult(new LogbookAppendResult(LogbookAppendDisposition.Appended, entry));
        }
    }

    private sealed class FakeCheckpointStore(FlightSession session) : IFlightSessionCheckpointStore
    {
        private FlightSession? _session = session;
        public int ClearCount { get; private set; }
        public int SaveCount { get; private set; }
        public FlightSession? LastSaved { get; private set; }
        public bool FailNextClear { get; set; }
        public Task SaveAsync(FlightSession value, CancellationToken cancellationToken = default)
        {
            _session = value;
            LastSaved = value;
            SaveCount++;
            return Task.CompletedTask;
        }
        public Task<FlightSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session);
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextClear) { FailNextClear = false; throw new IOException("Synthetic cleanup failure."); }
            _session = null;
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeProfileStore : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord _record = new(
            1,
            PlayerCareerProfile.Start(Guid.NewGuid(), "KORD", Epoch),
            Epoch);
        public int SaveCount { get; private set; }
        public bool FailNextSave { get; set; }
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<PlayerCareerProfileStoreRecord?>(_record);
        public Task<PlayerCareerProfileStoreRecord> SaveAsync(PlayerCareerProfile profile, long? expectedRevision, DateTimeOffset savedAt, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailNextSave) { FailNextSave = false; throw new IOException("Synthetic experience failure."); }
            _record = new(_record.Revision + 1, profile, savedAt);
            return Task.FromResult(_record);
        }
    }
}
