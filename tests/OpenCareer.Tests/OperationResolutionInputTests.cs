using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class OperationResolutionInputTests
{
    [Theory]
    [InlineData(MissionExecutionResult.Completed)]
    [InlineData(MissionExecutionResult.Failed)]
    [InlineData(MissionExecutionResult.Aborted)]
    public void ValidateAcceptsEveryDefinedMissionResult(MissionExecutionResult result)
    {
        var input = ValidInput() with
        {
            MissionResult = result
        };

        input.Validate();

        Assert.Equal(result, input.MissionResult);
    }

    [Fact]
    public void ObjectiveCompletionRatioUsesCompletedAndRequiredCounts()
    {
        var input = ValidInput() with
        {
            ObjectivesCompleted = 2,
            ObjectivesRequired = 4
        };

        Assert.Equal(0.5, input.ObjectiveCompletionRatio, precision: 10);
    }

    [Fact]
    public void ValidateRejectsEmptyMissionId()
    {
        var input = ValidInput() with
        {
            MissionId = Guid.Empty
        };

        Assert.Throws<ArgumentException>(input.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateRejectsMissingOperationId(string operationId)
    {
        var input = ValidInput() with
        {
            OperationId = operationId
        };

        Assert.Throws<ArgumentException>(input.Validate);
    }

    [Fact]
    public void ValidateRejectsUndefinedMissionResult()
    {
        var input = ValidInput() with
        {
            MissionResult = (MissionExecutionResult)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(input.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateRejectsNonPositiveRequiredObjectiveCount(int required)
    {
        var input = ValidInput() with
        {
            ObjectivesRequired = required
        };

        Assert.Throws<ArgumentOutOfRangeException>(input.Validate);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 1)]
    public void ValidateRejectsInvalidCompletedObjectiveCount(
        int completed,
        int required)
    {
        var input = ValidInput() with
        {
            ObjectivesCompleted = completed,
            ObjectivesRequired = required
        };

        Assert.Throws<ArgumentOutOfRangeException>(input.Validate);
    }

    [Fact]
    public void ValidateRejectsNegativeMissionDuration()
    {
        var input = ValidInput() with
        {
            MissionDuration = TimeSpan.FromSeconds(-1)
        };

        Assert.Throws<ArgumentOutOfRangeException>(input.Validate);
    }

    [Fact]
    public void ValidateRejectsDefaultResolutionTimestamp()
    {
        var input = ValidInput() with
        {
            ResolvedAt = default
        };

        Assert.Throws<ArgumentOutOfRangeException>(input.Validate);
    }

    [Fact]
    public void SurvivalFlagsAreIndependent()
    {
        var input = ValidInput() with
        {
            AircraftSurvived = false,
            CrewSurvived = true
        };

        input.Validate();

        Assert.False(input.AircraftSurvived);
        Assert.True(input.CrewSurvived);
    }

    private static OperationResolutionInput ValidInput() =>
        new(
            Guid.Parse("db28df45-7c79-4d1c-b8f5-93568cd98a5a"),
            "support-002",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(42),
            ResolvedAt: new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.Zero));
}
