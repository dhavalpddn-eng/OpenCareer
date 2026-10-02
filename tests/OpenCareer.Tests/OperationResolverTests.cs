using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class OperationResolverTests
{
    private readonly IOperationResolver _resolver = new OperationResolver();

    [Fact]
    public void CompletedMissionWithAllObjectivesAndSurvivorsIsSuccess()
    {
        OperationOutcome outcome = _resolver.Resolve(ValidInput());

        Assert.Equal(OperationOutcomeStatus.Success, outcome.Status);
    }

    [Fact]
    public void CompletedMissionWithSomeObjectivesIsPartialSuccess()
    {
        OperationOutcome outcome = _resolver.Resolve(
            ValidInput() with
            {
                ObjectivesCompleted = 1,
                ObjectivesRequired = 2
            });

        Assert.Equal(OperationOutcomeStatus.PartialSuccess, outcome.Status);
    }

    [Fact]
    public void CompletedMissionWithNoObjectivesIsFailure()
    {
        OperationOutcome outcome = _resolver.Resolve(
            ValidInput() with
            {
                ObjectivesCompleted = 0,
                ObjectivesRequired = 2
            });

        Assert.Equal(OperationOutcomeStatus.Failure, outcome.Status);
    }

    [Fact]
    public void AircraftLossDowngradesOtherwiseSuccessfulMissionToPartialSuccess()
    {
        OperationOutcome outcome = _resolver.Resolve(
            ValidInput() with
            {
                AircraftSurvived = false
            });

        Assert.Equal(OperationOutcomeStatus.PartialSuccess, outcome.Status);
    }

    [Fact]
    public void CrewLossMakesCompletedMissionFailure()
    {
        OperationOutcome outcome = _resolver.Resolve(
            ValidInput() with
            {
                CrewSurvived = false
            });

        Assert.Equal(OperationOutcomeStatus.Failure, outcome.Status);
    }

    [Fact]
    public void ExplicitMissionFailureRemainsFailureEvenWithCompletedObjectives()
    {
        OperationOutcome outcome = _resolver.Resolve(
            ValidInput() with
            {
                MissionResult = MissionExecutionResult.Failed
            });

        Assert.Equal(OperationOutcomeStatus.Failure, outcome.Status);
    }

    [Fact]
    public void AbortedMissionRemainsAbortedEvenWithCompletedObjectives()
    {
        OperationOutcome outcome = _resolver.Resolve(
            ValidInput() with
            {
                MissionResult = MissionExecutionResult.Aborted
            });

        Assert.Equal(OperationOutcomeStatus.Aborted, outcome.Status);
    }

    [Fact]
    public void ResolverPreservesMissionOperationAndResolutionTime()
    {
        OperationResolutionInput input = ValidInput();

        OperationOutcome outcome = _resolver.Resolve(input);

        Assert.Equal(input.MissionId, outcome.MissionId);
        Assert.Equal(input.OperationId, outcome.OperationId);
        Assert.Equal(input.ResolvedAt, outcome.CompletedAt);
    }

    [Fact]
    public void ResolverRejectsInvalidInput()
    {
        OperationResolutionInput input = ValidInput() with
        {
            MissionId = Guid.Empty
        };

        Assert.Throws<ArgumentException>(() => _resolver.Resolve(input));
    }

    private static OperationResolutionInput ValidInput() =>
        new(
            Guid.Parse("6f7f290c-7c85-4a7a-b159-cab52199b61e"),
            "support-003",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(55),
            ResolvedAt: new DateTimeOffset(2026, 9, 20, 22, 0, 0, TimeSpan.Zero));
}
