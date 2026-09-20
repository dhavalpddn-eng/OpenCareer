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
    public void FirstJobFlightRequiresRecordedTakeoff()
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
                Step("job-fly")));

        sessions.CommitPersisted(
            ToAirborne(taxi));

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(
                Step("job-fly")));
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

        sessions.CommitPersisted(
            ToEngineStart(ready));

        coordinator.RefreshLiveEvidence();

        Assert.Equal(
            "job-engine-start",
            coordinator.Current.Step?.Id);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            coordinator.Current.EvidenceState);
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

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(3),
                    Connected: true,
                    ContinuityPlausible: true,
                    SelfPoweredMovementForFlight: true)));
    }

    private static FlightSession ToAirborne(
        FlightSession session)
    {
        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(4),
                        Connected: true,
                        ContinuityPlausible: true,
                        TakeoffCandidate: true)));

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(5),
                    Connected: true,
                    ContinuityPlausible: true,
                    AirborneConfirmed: true)));
    }

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
