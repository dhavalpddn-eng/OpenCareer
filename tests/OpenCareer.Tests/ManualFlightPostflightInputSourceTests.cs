using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class ManualFlightPostflightInputSourceTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 30, 14, 0, 0, TimeSpan.Zero);
    private const string AircraftId = "msfs-title:test-aircraft";

    [Fact]
    public async Task CompletedManualSessionWithAuthoritativeAircraftIsReady()
    {
        var registry = new FakeRegistry(Resolution("Test Aircraft"));
        ManualFlightPostflightInputSource source = CreateSource(CompletedSession(), Identified(), registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.True(snapshot.IsReady);
        Assert.Equal(ManualFlightPostflightInputState.Ready, snapshot.State);
        Assert.Equal(1, registry.ReadCount);
        ManualFlightPostflightLogRequest request = Assert.IsType<ManualFlightPostflightLogRequest>(snapshot.Request);
        Assert.Equal(LogbookEntryKind.FreeFlight, request.DebriefContext.EntryKind);
        Assert.Equal("Test Aircraft", request.DebriefContext.Aircraft.DisplayName);
        Assert.Null(request.DebriefContext.ActualDeparture);
        Assert.Null(request.DebriefContext.ActualArrival);
        Assert.Null(request.DebriefContext.DiversionLocation);
        Assert.Equal(EvidenceQuality.Unavailable, request.DebriefContext.Payload.EvidenceQuality);
        Assert.Equal(MissionOutcome.NotApplicable, request.DebriefContext.MissionOutcome);
        Assert.Equal(SettlementRecordStatus.NotApplicable, request.DebriefContext.Settlement.Status);
        Assert.Equal(Epoch.AddHours(2), request.LogbookCommittedAt);
        Assert.Equal(Epoch.AddHours(2), request.ExperienceSavedAt);
    }

    [Fact]
    public async Task ActiveSessionIsBlockedWithoutRegistryRead()
    {
        var registry = new FakeRegistry(Resolution("Test Aircraft"));
        ManualFlightPostflightInputSource source = CreateSource(FlightSession.Start(Epoch), Identified(), registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.Equal(ManualFlightPostflightInputState.FlightNotCompleted, snapshot.State);
        Assert.False(snapshot.IsReady);
        Assert.Null(snapshot.Request);
        Assert.Equal(0, registry.ReadCount);
    }

    [Fact]
    public async Task ContractLinkedSessionIsBlockedWithoutRegistryRead()
    {
        var registry = new FakeRegistry(Resolution("Test Aircraft"));
        ManualFlightPostflightInputSource source = CreateSource(CompletedSession(Guid.NewGuid()), Identified(), registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.Equal(ManualFlightPostflightInputState.ContractLinked, snapshot.State);
        Assert.Equal(0, registry.ReadCount);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Interrupted, FlightOperationState.Failed, FlightTrackingState.Interrupted, false)]
    [InlineData(FlightSessionStatus.Cancelled, FlightOperationState.Cancelled, FlightTrackingState.Complete, false)]
    [InlineData(FlightSessionStatus.Completed, FlightOperationState.Complete, FlightTrackingState.Complete, true)]
    public async Task UnsuccessfulTerminalSessionIsBlocked(
        FlightSessionStatus status,
        FlightOperationState operation,
        FlightTrackingState tracking,
        bool crashed)
    {
        FlightSession session = CompletedSession() with
        {
            Status = status,
            OperationState = operation,
            Tracking = CompletedSession().Tracking with { State = tracking, CrashReported = crashed }
        };
        var registry = new FakeRegistry(Resolution("Test Aircraft"));
        ManualFlightPostflightInputSource source = CreateSource(session, Identified(), registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.Equal(ManualFlightPostflightInputState.FlightNotLoggable, snapshot.State);
        Assert.Equal(0, registry.ReadCount);
    }

    [Fact]
    public async Task MissingLoadedIdentityFailsClosed()
    {
        var registry = new FakeRegistry(Resolution("Test Aircraft"));
        ManualFlightPostflightInputSource source = CreateSource(
            CompletedSession(),
            CurrentLoadedAircraftIdentitySnapshot.Unavailable,
            registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.Equal(ManualFlightPostflightInputState.AircraftIdentityUnavailable, snapshot.State);
        Assert.Equal(0, registry.ReadCount);
    }

    [Fact]
    public async Task ConflictingSessionAndLoadedIdentityFailsClosed()
    {
        FlightSession session = CompletedSession() with
        {
            Plan = new FlightSessionPlan(null, null, ExpectedCanonicalAircraftId: "different-aircraft")
        };
        var registry = new FakeRegistry(Resolution("Test Aircraft"));
        ManualFlightPostflightInputSource source = CreateSource(session, Identified(), registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.Equal(ManualFlightPostflightInputState.AircraftIdentityConflict, snapshot.State);
        Assert.Equal(0, registry.ReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnresolvedRegistryDisplayIdentityFailsClosed(bool missingResolution)
    {
        var registry = new FakeRegistry(missingResolution ? null : Resolution(displayName: null));
        ManualFlightPostflightInputSource source = CreateSource(CompletedSession(), Identified(), registry);

        ManualFlightPostflightInputSnapshot snapshot = await source.ReadCurrentAsync(Epoch.AddHours(2));

        Assert.Equal(ManualFlightPostflightInputState.AircraftDebriefUnavailable, snapshot.State);
        Assert.Null(snapshot.Request);
    }

    private static ManualFlightPostflightInputSource CreateSource(
        FlightSession session,
        CurrentLoadedAircraftIdentitySnapshot identity,
        FakeRegistry registry)
    {
        var sessions = new FlightSessionCoordinator();
        sessions.Restore(session);
        return new(sessions, new FakeLoadedAircraft(identity), registry);
    }

    private static FlightSession CompletedSession(Guid? contractId = null)
    {
        FlightSession session = FlightSession.Start(Epoch, contractId);
        DateTimeOffset completedAt = Epoch.AddHours(1);
        return session with
        {
            UpdatedAt = completedAt,
            Status = FlightSessionStatus.Completed,
            OperationState = FlightOperationState.Complete,
            Tracking = session.Tracking with { State = FlightTrackingState.Complete, UpdatedAt = completedAt },
            Milestones = session.Milestones with { CompletedAt = completedAt },
            Legs = [session.EffectiveLegs[0].Complete(completedAt)]
        };
    }

    private static CurrentLoadedAircraftIdentitySnapshot Identified() =>
        CurrentLoadedAircraftIdentitySnapshot.Identified(AircraftId);

    private static AircraftRegistryResolution Resolution(string? displayName) =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                AircraftId,
                "test",
                "test-aircraft",
                AircraftDataConfidence.Verified,
                IsInstalled: true,
                DisplayName: displayName)
        ]);

    private sealed class FakeLoadedAircraft(CurrentLoadedAircraftIdentitySnapshot current)
        : ICurrentLoadedAircraftIdentitySource
    {
        public CurrentLoadedAircraftIdentitySnapshot Current { get; } = current;
    }

    private sealed class FakeRegistry(AircraftRegistryResolution? resolution)
        : IAircraftRegistrySource
    {
        public int ReadCount { get; private set; }

        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(resolution);
        }
    }
}
