using OpenCareer.Application.Flights;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionTutorialEvidenceSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstJobPreparationRequiresAircraftReadyMilestone()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        TutorialStep step =
            Step("job-prepare");

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(step));

        FlightSession started =
            FlightSession.Start(Epoch);

        sessions.Restore(started);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(step));

        sessions.CommitPersisted(
            ToAircraftReady(started));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(step));
    }

    [Fact]
    public void FirstJobEngineStartRequiresEngineStartMilestone()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        FlightSession ready =
            ToAircraftReady(
                FlightSession.Start(Epoch));

        sessions.Restore(ready);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-engine-start")));

        sessions.CommitPersisted(
            ToEngineStart(ready));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(
                Step("job-engine-start")));
    }

    [Fact]
    public void InterruptedOrCancelledSessionDoesNotSatisfyEngineStart()
    {
        FlightSession engineStarted =
            ToEngineStart(
                ToAircraftReady(
                    FlightSession.Start(Epoch)));

        FlightSession suspended =
            FlightSessionEngine.Advance(
                engineStarted,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(3),
                        Connected: false)));

        FlightSession interrupted =
            FlightSessionEngine.Advance(
                suspended,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(4),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: false)));

        FlightSession cancelled =
            FlightSessionEngine.Advance(
                engineStarted,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(3),
                        Connected: true,
                        ContinuityPlausible: true),
                    CancelRequested: true));

        var interruptedSessions =
            new FlightSessionCoordinator();

        interruptedSessions.Restore(interrupted);

        var interruptedSource =
            new FlightSessionTutorialEvidenceSource(
                interruptedSessions);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            interruptedSource.GetState(
                Step("job-engine-start")));

        var cancelledSessions =
            new FlightSessionCoordinator();

        cancelledSessions.Restore(cancelled);

        var cancelledSource =
            new FlightSessionTutorialEvidenceSource(
                cancelledSessions);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            cancelledSource.GetState(
                Step("job-engine-start")));
    }

    [Fact]
    public void FirstJobTaxiOutRequiresTaxiOutMilestone()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        FlightSession engineStarted =
            ToEngineStart(
                ToAircraftReady(
                    FlightSession.Start(Epoch)));

        sessions.Restore(engineStarted);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-taxi-out")));

        sessions.CommitPersisted(
            ToTaxiOutFromEngineStart(
                engineStarted));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(
                Step("job-taxi-out")));
    }

    [Fact]
    public void FirstJobTakeoffWaitsThroughTakeoffRollUntilAirborne()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        FlightSession taxi =
            ToTaxiOut(
                FlightSession.Start(Epoch));

        sessions.Restore(taxi);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-takeoff")));

        FlightSession takeoffRoll =
            ToTakeoffRoll(taxi);

        sessions.CommitPersisted(
            takeoffRoll);

        Assert.Null(
            takeoffRoll.Milestones.TakeoffAt);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-takeoff")));

        sessions.CommitPersisted(
            ToAirborneFromTakeoffRoll(
                takeoffRoll));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(
                Step("job-takeoff")));
    }

    [Fact]
    public void FirstJobInitialClimbRequiresInitialClimbMilestone()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        FlightSession airborne =
            ToAirborne(
                ToTaxiOut(
                    FlightSession.Start(Epoch)));

        sessions.Restore(airborne);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-initial-climb")));

        sessions.CommitPersisted(
            ToInitialClimb(airborne));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(
                Step("job-initial-climb")));
    }

    [Fact]
    public void FirstJobFlyRequiresMissionFlightProgressMilestone()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        FlightSession climbed =
            ToInitialClimb(
                ToAirborne(
                    ToTaxiOut(
                        FlightSession.Start(Epoch))));

        sessions.Restore(climbed);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-fly")));

        sessions.CommitPersisted(
            ToMissionFlightProgress(climbed));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(
                Step("job-fly")));
    }

    [Fact]
    public void FirstJobApproachRequiresApproachAfterMissionFlightProgress()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);
        TutorialStep step = Step("job-approach");

        Assert.Equal(TutorialStepEvidenceState.Waiting, source.GetState(step));

        FlightSession climbed = ToInitialClimb(
            ToAirborne(ToTaxiOut(FlightSession.Start(Epoch))));
        sessions.Restore(climbed);
        Assert.Equal(TutorialStepEvidenceState.Waiting, source.GetState(step));

        FlightSession progressing = ToMissionFlightProgress(climbed);
        sessions.CommitPersisted(progressing);
        Assert.Equal(TutorialStepEvidenceState.Waiting, source.GetState(step));

        FlightSession approaching = FlightSessionEngine.Advance(
            progressing,
            new FlightSessionAdvance(new FlightStateEvidence(
                Epoch.AddSeconds(8), Connected: true,
                ContinuityPlausible: true, ApproachConfirmed: true)));
        sessions.CommitPersisted(approaching);
        Assert.Equal(TutorialStepEvidenceState.Satisfied, source.GetState(step));

        FlightSession interrupted = FlightSessionEngine.Advance(
            approaching,
            new FlightSessionAdvance(new FlightStateEvidence(
                Epoch.AddSeconds(9), Connected: true, CrashReported: true)));
        sessions.CommitPersisted(interrupted);
        Assert.Equal(TutorialStepEvidenceState.Waiting, source.GetState(step));
    }

    [Fact]
    public void ApproachRecordedBeforeFlightProgressCannotSatisfyTutorial()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        FlightSession climbed = ToInitialClimb(
            ToAirborne(ToTaxiOut(FlightSession.Start(Epoch))));
        FlightSession previousApproach = climbed with
        {
            Milestones = climbed.Milestones with
            {
                ApproachAt = Epoch.AddSeconds(6)
            }
        };

        sessions.Restore(ToMissionFlightProgress(previousApproach));

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step("job-approach")));
    }

    [Fact]
    public void InterruptedSessionDoesNotSatisfyArrival()
    {
        var sessions =
            new FlightSessionCoordinator();

        FlightSession active =
            ToAirborne(
                ToTaxiOut(
                    FlightSession.Start(Epoch)));

        FlightSession interrupted =
            FlightSessionEngine.Advance(
                active,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(10),
                        Connected: false)));

        interrupted =
            FlightSessionEngine.Advance(
                interrupted,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(11),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: false)));

        sessions.Restore(interrupted);

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(
                Step("job-arrive")));
    }

    [Fact]
    public void UnmappedTutorialStepDoesNotPretendToHaveEvidence()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        Assert.Equal(
            TutorialStepEvidenceState.NotApplicable,
            source.GetState(
                Step("job-find")));
    }

    [Fact]
    public async Task CoordinatorRefreshesEvidenceWithoutAutoAdvancing()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new FlightSessionTutorialEvidenceSource(
                sessions);

        var progress =
            new MemoryProgressStore();

        var coordinator =
            new TutorialCoordinator(
                new AppTutorialCatalog(),
                new AlwaysAvailableReadiness(),
                progress,
                source);

        await coordinator.StartAsync(
            AppTutorialCatalog.FirstJobId,
            resume: false);

        for (var index = 0; index < 4; index++)
            await coordinator.NextAsync();

        Assert.Equal(
            "job-prepare",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession ready =
            ToAircraftReady(
                FlightSession.Start(Epoch));

        sessions.Restore(ready);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-prepare",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();

        Assert.Equal(
            "job-engine-start",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession engineStarted =
            ToEngineStart(ready);

        sessions.CommitPersisted(
            engineStarted);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-engine-start",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();

        Assert.Equal(
            "job-taxi-out",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession taxi =
            ToTaxiOutFromEngineStart(
                engineStarted);

        sessions.CommitPersisted(
            taxi);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-taxi-out",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();

        Assert.Equal(
            "job-takeoff",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession takeoffRoll =
            ToTakeoffRoll(taxi);

        sessions.CommitPersisted(
            takeoffRoll);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-takeoff",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession airborne =
            ToAirborneFromTakeoffRoll(
                takeoffRoll);

        sessions.CommitPersisted(
            airborne);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-takeoff",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();

        Assert.Equal(
            "job-initial-climb",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession climbed =
            ToInitialClimb(airborne);

        sessions.CommitPersisted(
            climbed);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-initial-climb",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();

        Assert.Equal(
            "job-fly",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession inFlight = ToMissionFlightProgress(climbed);
        sessions.CommitPersisted(inFlight);

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-fly",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();

        Assert.Equal("job-approach", coordinator.Current.Step?.Id);
        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            coordinator.Current.EvidenceState);

        FlightSession approaching = FlightSessionEngine.Advance(
            inFlight,
            new FlightSessionAdvance(new FlightStateEvidence(
                Epoch.AddSeconds(8), Connected: true,
                ContinuityPlausible: true, ApproachConfirmed: true)));
        sessions.CommitPersisted(approaching);
        coordinator.RefreshLiveEvidence();

        Assert.Equal("job-approach", coordinator.Current.Step?.Id);
        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);

        await coordinator.NextAsync();
        Assert.Equal("job-land", coordinator.Current.Step?.Id);
        Assert.Equal(TutorialStepEvidenceState.Waiting, coordinator.Current.EvidenceState);

        FlightSession touchdown = FlightSessionEngine.Advance(
            approaching,
            new FlightSessionAdvance(new FlightStateEvidence(
                Epoch.AddSeconds(9), Connected: true,
                ContinuityPlausible: true, TouchdownConfirmed: true)));
        sessions.CommitPersisted(touchdown);
        coordinator.RefreshLiveEvidence();
        Assert.Equal(TutorialStepEvidenceState.Waiting, coordinator.Current.EvidenceState);

        FlightSession landed = FlightSessionEngine.Advance(
            touchdown,
            new FlightSessionAdvance(new FlightStateEvidence(
                Epoch.AddSeconds(10), Connected: true,
                ContinuityPlausible: true, LandingRolloutConfirmed: true)));
        sessions.CommitPersisted(landed);
        coordinator.RefreshLiveEvidence();

        Assert.Equal("job-land", coordinator.Current.Step?.Id);
        Assert.Equal(TutorialStepEvidenceState.Satisfied, coordinator.Current.EvidenceState);

        await coordinator.NextAsync();
        Assert.Equal("job-taxi-in", coordinator.Current.Step?.Id);
        Assert.Equal(TutorialStepEvidenceState.Waiting, coordinator.Current.EvidenceState);

        sessions.CommitPersisted(FlightSessionEngine.Advance(
            landed,
            new FlightSessionAdvance(new FlightStateEvidence(
                Epoch.AddSeconds(11), Connected: true,
                ContinuityPlausible: true, TaxiInMovementConfirmed: true))));
        coordinator.RefreshLiveEvidence();

        Assert.Equal("job-taxi-in", coordinator.Current.Step?.Id);
        Assert.Equal(TutorialStepEvidenceState.Satisfied, coordinator.Current.EvidenceState);
    }

    private static TutorialStep Step(string id) =>
        new(
            id,
            1,
            id,
            id,
            "current-flight",
            null,
            "current-flight");

    private static FlightSession ToAircraftReady(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

    private static FlightSession ToEngineStart(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(2),
                    Connected: true,
                    ContinuityPlausible: true,
                    EngineStartObserved: true)));

    private static FlightSession ToTaxiOut(
        FlightSession session)
    {
        session =
            ToAircraftReady(session);

        session =
            ToEngineStart(session);

        return ToTaxiOutFromEngineStart(session);
    }

    private static FlightSession ToTaxiOutFromEngineStart(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(3),
                    Connected: true,
                    ContinuityPlausible: true,
                    SelfPoweredMovementForFlight: true)));

    private static FlightSession ToTakeoffRoll(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(4),
                    Connected: true,
                    ContinuityPlausible: true,
                    TakeoffCandidate: true)));

    private static FlightSession ToAirborneFromTakeoffRoll(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(5),
                    Connected: true,
                    ContinuityPlausible: true,
                    AirborneConfirmed: true)));

    private static FlightSession ToAirborne(
        FlightSession session) =>
        ToAirborneFromTakeoffRoll(
            ToTakeoffRoll(session));

    private static FlightSession ToInitialClimb(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(6),
                    Connected: true,
                    ContinuityPlausible: true,
                    InitialClimbConfirmed: true)));

    private static FlightSession ToMissionFlightProgress(
        FlightSession session) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(7),
                    Connected: true,
                    ContinuityPlausible: true,
                    MissionFlightProgressConfirmed: true)));

    private sealed class MemoryProgressStore :
        ITutorialProgressStore
    {
        private readonly Dictionary<string, TutorialProgress> _values =
            new(StringComparer.Ordinal);

        public Task<TutorialProgress> GetAsync(
            string tutorialId,
            CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(
                    tutorialId,
                    out TutorialProgress? progress))
            {
                return Task.FromResult(progress);
            }

            return Task.FromResult(
                new TutorialProgress(tutorialId));
        }

        public Task SaveAsync(
            TutorialProgress progress,
            CancellationToken cancellationToken = default)
        {
            _values[progress.TutorialId] = progress;
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysAvailableReadiness :
        ITutorialFeatureReadiness
    {
        public TutorialFeatureState GetState(
            string featureKey) =>
            TutorialFeatureState.Available;
    }
}
