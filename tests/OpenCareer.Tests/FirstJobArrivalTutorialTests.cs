using OpenCareer.Application.Flights;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FirstJobArrivalTutorialTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstJobArrivalRequiresOrderedTaxiParkingAndShutdownMilestones()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        FlightSession taxiIn = Session(
            status: FlightSessionStatus.Active,
            operationState: FlightOperationState.TaxiIn,
            taxiInProgressAt: Epoch.AddSeconds(1));

        sessions.Restore(taxiIn);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step()));

        FlightSession parked = taxiIn with
        {
            UpdatedAt = Epoch.AddSeconds(5),
            OperationState = FlightOperationState.Parked,
            Milestones = taxiIn.Milestones with
            {
                ParkedAt = Epoch.AddSeconds(5)
            }
        };

        sessions.CommitPersisted(parked);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step()));

        FlightSession shutdown = parked with
        {
            UpdatedAt = Epoch.AddSeconds(6),
            OperationState = FlightOperationState.Shutdown,
            Milestones = parked.Milestones with
            {
                ShutdownAt = Epoch.AddSeconds(6)
            }
        };

        sessions.CommitPersisted(shutdown);

        Assert.Equal(
            TutorialStepEvidenceState.Satisfied,
            source.GetState(Step()));
    }

    [Fact]
    public void CompletedStatusAloneDoesNotSatisfyFirstJobArrival()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        sessions.Restore(
            Session(
                status: FlightSessionStatus.Completed,
                operationState: FlightOperationState.Complete,
                taxiInProgressAt: null));

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step()));
    }

    [Fact]
    public void ArrivalMilestonesMustOccurInOrder()
    {
        var sessions = new FlightSessionCoordinator();
        var source = new FlightSessionTutorialEvidenceSource(sessions);

        FlightSession invalid = Session(
            status: FlightSessionStatus.Active,
            operationState: FlightOperationState.Shutdown,
            taxiInProgressAt: Epoch.AddSeconds(3)) with
        {
            Milestones = new FlightSessionMilestones(
                TaxiInProgressAt: Epoch.AddSeconds(3),
                ParkedAt: Epoch.AddSeconds(2),
                ShutdownAt: Epoch.AddSeconds(1))
        };

        sessions.Restore(invalid);

        Assert.Equal(
            TutorialStepEvidenceState.Waiting,
            source.GetState(Step()));
    }

    [Fact]
    public void FirstJobDefinitionAdvancesForArrivalEvidenceSlice()
    {
        TutorialDefinition firstJob =
            Assert.IsType<TutorialDefinition>(
                new AppTutorialCatalog().Get(
                    AppTutorialCatalog.FirstJobId));

        Assert.Equal(11, firstJob.Version);

        TutorialStep arrival = Assert.Single(
            firstJob.Steps,
            static step => step.Id == "job-arrive");

        Assert.Equal("Park and shut down", arrival.Title);
    }

    private static FlightSession Session(
        FlightSessionStatus status,
        FlightOperationState operationState,
        DateTimeOffset? taxiInProgressAt) =>
        FlightSession.Start(Epoch) with
        {
            Status = status,
            OperationState = operationState,
            UpdatedAt = Epoch.AddSeconds(4),
            Milestones = new FlightSessionMilestones(
                TaxiInProgressAt: taxiInProgressAt)
        };

    private static TutorialStep Step() =>
        new(
            "job-arrive",
            1,
            "Park and shut down",
            "Park and shut down.",
            "current-flight",
            null,
            "current-flight");
}
