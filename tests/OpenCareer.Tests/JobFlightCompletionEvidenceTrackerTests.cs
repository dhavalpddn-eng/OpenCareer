using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class JobFlightCompletionEvidenceTrackerTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContractLinkedSessionProducesReadOnlyCorrelatedSnapshot()
    {
        var sessions =
            new FlightSessionCoordinator();
        var evidence =
            new FakeEvidenceSource();
        var tracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                evidence);

        Guid contractId =
            Guid.Parse(
                "97000000-0000-0000-0000-000000000001");

        FlightSession session =
            CompletedSequenceSession(
                contractId);

        sessions.Restore(session);

        evidence.Publish(
            new FlightStateEvidence(
                session.UpdatedAt,
                Connected: true,
                StableTelemetry: true,
                ValidLoadedAircraft: true,
                ContinuityPlausible: true,
                ParkingConfirmed: true));

        JobFlightCompletionEvidenceSnapshot snapshot =
            Assert.IsType<JobFlightCompletionEvidenceSnapshot>(
                tracker.Current);

        Assert.Equal(
            contractId,
            snapshot.ContractId);
        Assert.Equal(
            session.SessionId,
            snapshot.FlightSessionId);
        Assert.True(
            snapshot.HasTrustworthyLatestEvidence);
        Assert.True(
            snapshot.TakeoffObserved);
        Assert.True(
            snapshot.AirborneObserved);
        Assert.True(
            snapshot.TouchdownObserved);
        Assert.True(
            snapshot.ParkingObserved);
        Assert.True(
            snapshot.ShutdownObserved);
        Assert.True(
            snapshot.CoreFlightSequenceObserved);
        Assert.False(
            snapshot.OperationCompleteObserved);
    }

    [Fact]
    public void RecoveredSessionRetainsDurableSequenceWithoutEvidenceReplay()
    {
        Guid contractId =
            Guid.Parse(
                "97000000-0000-0000-0000-000000000002");

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            CompletedSequenceSession(
                contractId));

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                new FakeEvidenceSource());

        JobFlightCompletionEvidenceSnapshot snapshot =
            Assert.IsType<JobFlightCompletionEvidenceSnapshot>(
                tracker.Current);

        Assert.Equal(
            contractId,
            snapshot.ContractId);
        Assert.True(
            snapshot.CoreFlightSequenceObserved);
        Assert.Null(
            snapshot.LatestEvidenceAt);
        Assert.False(
            snapshot.HasTrustworthyLatestEvidence);
    }

    [Fact]
    public void DifferentContractSessionDoesNotInheritStalePublishedEvidence()
    {
        var sessions =
            new FlightSessionCoordinator();
        var evidence =
            new FakeEvidenceSource();
        var tracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                evidence);

        FlightSession first =
            FlightSession.Start(
                Epoch,
                Guid.Parse(
                    "97000000-0000-0000-0000-000000000003"),
                Guid.Parse(
                    "97000000-0000-0000-0000-000000000013"));

        sessions.Restore(first);

        evidence.Publish(
            new FlightStateEvidence(
                Epoch.AddSeconds(1),
                Connected: true,
                StableTelemetry: true,
                ValidLoadedAircraft: true,
                ContinuityPlausible: true,
                AirborneConfirmed: true));

        FlightSession terminal =
            first with
            {
                Status =
                    FlightSessionStatus.Cancelled,
                OperationState =
                    FlightOperationState.Cancelled,
                UpdatedAt =
                    Epoch.AddSeconds(2)
            };

        sessions.CommitPersisted(terminal);
        sessions.ClearTerminalSession();

        FlightSession second =
            FlightSession.Start(
                Epoch.AddSeconds(3),
                Guid.Parse(
                    "97000000-0000-0000-0000-000000000004"),
                Guid.Parse(
                    "97000000-0000-0000-0000-000000000014"));

        sessions.Restore(second);

        JobFlightCompletionEvidenceSnapshot snapshot =
            Assert.IsType<JobFlightCompletionEvidenceSnapshot>(
                tracker.Current);

        Assert.Equal(
            second.ContractId,
            snapshot.ContractId);
        Assert.Null(
            snapshot.LatestEvidenceAt);
        Assert.False(
            snapshot.AirborneObserved);
        Assert.False(
            snapshot.HasTrustworthyLatestEvidence);
    }

    [Fact]
    public void NullPublicationClearsOnlyLiveEvidenceNotPersistedSequence()
    {
        var sessions =
            new FlightSessionCoordinator();
        var evidence =
            new FakeEvidenceSource();

        FlightSession session =
            CompletedSequenceSession(
                Guid.Parse(
                    "97000000-0000-0000-0000-000000000005"));

        sessions.Restore(session);

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                evidence);

        evidence.Publish(
            new FlightStateEvidence(
                session.UpdatedAt,
                Connected: true,
                StableTelemetry: true,
                ValidLoadedAircraft: true,
                ContinuityPlausible: true));

        Assert.True(
            tracker.Current?.HasTrustworthyLatestEvidence);

        evidence.Publish(null);

        JobFlightCompletionEvidenceSnapshot snapshot =
            Assert.IsType<JobFlightCompletionEvidenceSnapshot>(
                tracker.Current);

        Assert.Null(
            snapshot.LatestEvidenceAt);
        Assert.False(
            snapshot.HasTrustworthyLatestEvidence);
        Assert.True(
            snapshot.CoreFlightSequenceObserved);
    }

    [Fact]
    public void CrashEvidenceFailsCoreSequenceWithoutMutatingSession()
    {
        var sessions =
            new FlightSessionCoordinator();
        var evidence =
            new FakeEvidenceSource();

        FlightSession session =
            CompletedSequenceSession(
                Guid.Parse(
                    "97000000-0000-0000-0000-000000000006"));

        sessions.Restore(session);

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                evidence);

        evidence.Publish(
            new FlightStateEvidence(
                session.UpdatedAt,
                Connected: true,
                StableTelemetry: true,
                ValidLoadedAircraft: true,
                ContinuityPlausible: true,
                CrashReported: true));

        Assert.True(
            tracker.Current?.CrashReported);
        Assert.True(
            tracker.Current?.IsFailedOrCancelled);
        Assert.False(
            tracker.Current?.CoreFlightSequenceObserved);
        Assert.Equal(
            FlightSessionStatus.Active,
            sessions.Current?.Status);
    }

    private static FlightSession CompletedSequenceSession(
        Guid contractId)
    {
        DateTimeOffset updatedAt =
            Epoch.AddMinutes(20);

        return FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId)
            with
            {
                UpdatedAt =
                    updatedAt,
                Status =
                    FlightSessionStatus.Active,
                OperationState =
                    FlightOperationState.Shutdown,
                Tracking =
                    new FlightTrackingSnapshot(
                        FlightTrackingState.Parked,
                        SuspendedFrom:
                            null,
                        UpdatedAt:
                            updatedAt,
                        TakeoffCount:
                            1,
                        LandingEpisodeCount:
                            1,
                        BounceCount:
                            0,
                        TouchAndGoCount:
                            0,
                        RejectedTakeoffCount:
                            0,
                        CrashReported:
                            false),
                Milestones =
                    new FlightSessionMilestones(
                        TakeoffAt:
                            Epoch.AddMinutes(5),
                        FirstTouchdownAt:
                            Epoch.AddMinutes(15),
                        LandingAt:
                            Epoch.AddMinutes(16),
                        ParkedAt:
                            Epoch.AddMinutes(19),
                        ShutdownAt:
                            updatedAt)
            };
    }

    private sealed class FakeEvidenceSource
        : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current { get; private set; }

        public event Action<FlightStateEvidence?>? EvidenceChanged;

        public void Publish(
            FlightStateEvidence? evidence)
        {
            Current = evidence;
            EvidenceChanged?.Invoke(evidence);
        }
    }
}
