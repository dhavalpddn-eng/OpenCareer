using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class LiveFlightChecklistSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FlightTrackingState.Preflight, LiveFlightChecklistPhase.PreflightPreparation)]
    [InlineData(FlightTrackingState.EngineStart, LiveFlightChecklistPhase.EngineStart)]
    [InlineData(FlightTrackingState.TaxiOut, LiveFlightChecklistPhase.Taxi)]
    [InlineData(FlightTrackingState.TakeoffRoll, LiveFlightChecklistPhase.Takeoff)]
    [InlineData(FlightTrackingState.Airborne, LiveFlightChecklistPhase.Airborne)]
    [InlineData(FlightTrackingState.Approach, LiveFlightChecklistPhase.ApproachLanding)]
    [InlineData(FlightTrackingState.LandingEpisode, LiveFlightChecklistPhase.ApproachLanding)]
    [InlineData(FlightTrackingState.TaxiIn, LiveFlightChecklistPhase.Parking)]
    [InlineData(FlightTrackingState.Parked, LiveFlightChecklistPhase.Parking)]
    [InlineData(FlightTrackingState.Complete, LiveFlightChecklistPhase.ShutdownCompletion)]
    public void ProjectsDeterministicCurrentPhase(
        FlightTrackingState trackingState,
        LiveFlightChecklistPhase expectedPhase)
    {
        FlightSession session = Session(trackingState);
        LiveFlightChecklistSnapshot snapshot = Read(session);

        Assert.Equal(expectedPhase, snapshot.CurrentPhase);
        Assert.All(snapshot.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.Id)));
        Assert.All(snapshot.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.EvidenceDetail)));
    }

    [Fact]
    public void ShutdownOperationAdvancesParkedTrackingToShutdownChecklist()
    {
        FlightSession session = Session(FlightTrackingState.Parked) with
        {
            OperationState = FlightOperationState.Shutdown,
            Milestones = new FlightSessionMilestones(
                ParkedAt: Epoch.AddMinutes(25),
                ShutdownAt: Epoch.AddMinutes(26))
        };

        LiveFlightChecklistSnapshot snapshot = Read(session);

        Assert.Equal(LiveFlightChecklistPhase.ShutdownCompletion, snapshot.CurrentPhase);
        Assert.Equal(LiveFlightChecklistStepState.Satisfied, Step(snapshot, "shutdown").State);
        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "operation-complete").State);
    }

    [Fact]
    public void PersistedMilestonesSatisfyStepsAndDuplicateReadsAreStable()
    {
        FlightSession session = Session(FlightTrackingState.TakeoffRoll) with
        {
            Milestones = new FlightSessionMilestones(
                AircraftReadyAt: Epoch.AddMinutes(1),
                EngineStartAt: Epoch.AddMinutes(2),
                TaxiOutAt: Epoch.AddMinutes(3),
                TakeoffRollAt: Epoch.AddMinutes(4))
        };
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(session);
        var source = new LiveFlightChecklistSource(coordinator, StableEvidence());

        LiveFlightChecklistSnapshot first = source.Read();
        LiveFlightChecklistSnapshot second = source.Read();

        Assert.Equal(first, second);
        Assert.Equal(
            LiveFlightChecklistStepState.Satisfied,
            Step(first, "takeoff-roll").State);
        Assert.Equal(
            LiveFlightChecklistStepState.Pending,
            Step(first, "takeoff").State);
    }

    [Fact]
    public void NoisyLaterSnapshotCannotBackfillEarlierChecklistSteps()
    {
        FlightSession session = Session(FlightTrackingState.Preflight);
        var evidence = new TestEvidenceSource
        {
            Current = new FlightStateEvidence(
                Epoch.AddHours(1),
                Connected: true,
                StableTelemetry: true,
                ValidLoadedAircraft: true,
                ContinuityPlausible: true,
                EngineStartObserved: true,
                SelfPoweredMovementForFlight: true,
                TakeoffCandidate: true,
                AirborneConfirmed: true,
                ApproachConfirmed: true,
                TouchdownConfirmed: true,
                LandingRolloutConfirmed: true,
                ParkingConfirmed: true,
                OperationCompleteConfirmed: true)
        };

        LiveFlightChecklistSnapshot snapshot = Read(session, evidence);

        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "engine-start").State);
        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "takeoff").State);
        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "parked").State);
    }

    [Fact]
    public void SuspendedAndRecoveredSessionRetainsVerifiedProgress()
    {
        FlightSession suspended = Session(FlightTrackingState.Suspended) with
        {
            Status = FlightSessionStatus.Suspended,
            Tracking = new FlightTrackingSnapshot(
                FlightTrackingState.Suspended,
                FlightTrackingState.Airborne,
                Epoch.AddMinutes(20),
                1,
                0,
                0,
                0,
                0,
                false),
            Milestones = new FlightSessionMilestones(
                TakeoffAt: Epoch.AddMinutes(10))
        };
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(suspended);
        var source = new LiveFlightChecklistSource(coordinator, new TestEvidenceSource());

        LiveFlightChecklistSnapshot beforeRecovery = source.Read();
        coordinator.CommitPersisted(
            suspended with
            {
                Status = FlightSessionStatus.Active,
                UpdatedAt = suspended.UpdatedAt.AddMinutes(1),
                Tracking = suspended.Tracking with
                {
                    State = FlightTrackingState.Airborne,
                    SuspendedFrom = null,
                    UpdatedAt = suspended.Tracking.UpdatedAt.AddMinutes(1)
                }
            });
        LiveFlightChecklistSnapshot afterRecovery = source.Read();

        Assert.Equal(LiveFlightChecklistPhase.Airborne, beforeRecovery.CurrentPhase);
        Assert.Equal(LiveFlightChecklistPhase.Airborne, afterRecovery.CurrentPhase);
        Assert.Equal(LiveFlightChecklistStepState.Satisfied, Step(beforeRecovery, "airborne").State);
        Assert.Equal(LiveFlightChecklistStepState.Satisfied, Step(afterRecovery, "airborne").State);
    }

    [Fact]
    public void UnsupportedAndManualStepsNeverAutoSatisfy()
    {
        FlightSession session = Session(FlightTrackingState.Complete) with
        {
            Status = FlightSessionStatus.Completed,
            OperationState = FlightOperationState.Complete,
            Milestones = new FlightSessionMilestones(
                AircraftReadyAt: Epoch,
                EngineStartAt: Epoch,
                TaxiOutAt: Epoch,
                TakeoffRollAt: Epoch,
                TakeoffAt: Epoch,
                ApproachAt: Epoch,
                LandingAt: Epoch,
                TaxiInAt: Epoch,
                ParkedAt: Epoch,
                ShutdownAt: Epoch,
                CompletedAt: Epoch)
        };

        LiveFlightChecklistSnapshot snapshot = Read(session, StableEvidence());

        LiveFlightChecklistStep unavailable = Step(snapshot, "cockpit-preflight");
        Assert.Equal(LiveFlightChecklistVerificationMode.Unavailable, unavailable.VerificationMode);
        Assert.Equal(LiveFlightChecklistStepState.Unavailable, unavailable.State);
        Assert.All(
            snapshot.Steps.Where(step => step.VerificationMode == LiveFlightChecklistVerificationMode.Manual),
            step => Assert.Equal(LiveFlightChecklistStepState.Pending, step.State));
    }

    [Fact]
    public void NewLegDoesNotInheritPriorLegMilestones()
    {
        FlightLeg first = FlightLeg.First(Guid.NewGuid(), Epoch, plan: null)
            .Complete(Epoch.AddMinutes(30));
        FlightLeg second = new(
            Guid.NewGuid(),
            2,
            Epoch.AddMinutes(35),
            TimeLedger: FlightTimeLedger.Empty,
            Statistics: FlightSessionStatistics.Empty,
            LandingEpisodeNumbers: Array.Empty<int>());
        FlightSession session = Session(FlightTrackingState.Preflight) with
        {
            Legs = [first, second],
            Milestones = new FlightSessionMilestones(
                AircraftReadyAt: Epoch.AddMinutes(1),
                EngineStartAt: Epoch.AddMinutes(2),
                TakeoffAt: Epoch.AddMinutes(10))
        };

        LiveFlightChecklistSnapshot snapshot = Read(session, StableEvidence());

        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "aircraft-ready").State);
        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "engine-start").State);
        Assert.Equal(LiveFlightChecklistStepState.Pending, Step(snapshot, "takeoff").State);
    }

    private static FlightSession Session(FlightTrackingState state) =>
        FlightSession.Start(Epoch) with
        {
            UpdatedAt = Epoch.AddMinutes(30),
            Tracking = FlightTrackingSnapshot.Start(Epoch) with
            {
                State = state,
                UpdatedAt = Epoch.AddMinutes(30)
            }
        };

    private static LiveFlightChecklistSnapshot Read(
        FlightSession session,
        TestEvidenceSource? evidence = null)
    {
        var coordinator = new FlightSessionCoordinator();
        coordinator.Restore(session);
        return new LiveFlightChecklistSource(
            coordinator,
            evidence ?? new TestEvidenceSource()).Read();
    }

    private static TestEvidenceSource StableEvidence() =>
        new()
        {
            Current = new FlightStateEvidence(
                Epoch,
                Connected: true,
                StableTelemetry: true)
        };

    private static LiveFlightChecklistStep Step(
        LiveFlightChecklistSnapshot snapshot,
        string id) =>
        Assert.Single(snapshot.Steps, step => step.Id == id);

    private sealed class TestEvidenceSource : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current { get; init; }

        public event Action<FlightStateEvidence?>? EvidenceChanged
        {
            add { }
            remove { }
        }
    }
}
