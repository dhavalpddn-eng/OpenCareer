using OpenCareer.Application.Flights;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FirstJobOperationCompleteTutorialTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstJobOperationCompleteRequiresCompletedMilestoneAfterShutdown()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        FlightSession shutdown = Session(
            status: FlightSessionStatus.Active,
            operationState: FlightOperationState.Shutdown,
            shutdownAt: Epoch.AddSeconds(2),
            completedAt: null);

        sessions.Restore(shutdown);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step("job-complete")));

        FlightSession completed = shutdown with
        {
            Status = FlightSessionStatus.Completed,
            OperationState = FlightOperationState.Complete,
            UpdatedAt = Epoch.AddSeconds(4),
            Milestones = shutdown.Milestones with
            {
                CompletedAt = Epoch.AddSeconds(4)
            }
        };

        sessions.CommitPersisted(completed);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(Step("job-complete")));
    }

    [Fact]
    public void CompletedStatusWithoutCompletedMilestoneDoesNotSatisfyStep()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        sessions.Restore(
            Session(
                status: FlightSessionStatus.Completed,
                operationState: FlightOperationState.Complete,
                shutdownAt: Epoch.AddSeconds(2),
                completedAt: null));

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step("job-complete")));
    }

    [Fact]
    public void CompletedMilestoneMustFollowShutdown()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        sessions.Restore(
            Session(
                status: FlightSessionStatus.Completed,
                operationState: FlightOperationState.Complete,
                shutdownAt: Epoch.AddSeconds(4),
                completedAt: Epoch.AddSeconds(3)));

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step("job-complete")));
    }

    [Fact]
    public void DebriefRemainsUnmappedUntilDebriefSystemExists()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        sessions.Restore(
            Session(
                status: FlightSessionStatus.Completed,
                operationState: FlightOperationState.Complete,
                shutdownAt: Epoch.AddSeconds(2),
                completedAt: Epoch.AddSeconds(4)));

        Assert.Equal(
            TutorialStepEvidenceState.NotApplicable,
            source.GetState(Step("job-debrief")));
    }

    [Fact]
    public void FirstJobDefinitionAddsOperationCompleteBeforeDebrief()
    {
        TutorialDefinition firstJob =
            Assert.IsType<TutorialDefinition>(
                new AppTutorialCatalog().Get(
                    AppTutorialCatalog.FirstJobId));

        Assert.Equal(11, firstJob.Version);

        string[] ids =
            firstJob.Steps
                .Select(static step => step.Id)
                .ToArray();

        int completeIndex = Array.IndexOf(ids, "job-complete");
        int debriefIndex = Array.IndexOf(ids, "job-debrief");

        Assert.True(completeIndex >= 0);
        Assert.Equal(completeIndex + 1, debriefIndex);
    }

    private static FlightSession Session(
        FlightSessionStatus status,
        FlightOperationState operationState,
        DateTimeOffset? shutdownAt,
        DateTimeOffset? completedAt) =>
        FlightSession.Start(Epoch) with
        {
            Status = status,
            OperationState = operationState,
            UpdatedAt = Epoch.AddSeconds(5),
            Milestones = new FlightSessionMilestones(
                ShutdownAt: shutdownAt,
                CompletedAt: completedAt)
        };

    private static TutorialStep Step(string id) =>
        new(
            id,
            1,
            id,
            id,
            "current-flight",
            null,
            "current-flight");
}
