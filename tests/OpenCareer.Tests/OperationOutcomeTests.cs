using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class OperationOutcomeTests
{
    [Theory]
    [InlineData(OperationOutcomeStatus.Success)]
    [InlineData(OperationOutcomeStatus.PartialSuccess)]
    [InlineData(OperationOutcomeStatus.Failure)]
    [InlineData(OperationOutcomeStatus.Aborted)]
    public void ValidateAcceptsEveryDefinedOutcomeStatus(OperationOutcomeStatus status)
    {
        var outcome = new OperationOutcome(
            Guid.Parse("3f7ef2ce-f3b3-4cb4-9f51-82dc41ad4097"),
            "support-001",
            status,
            new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero));

        outcome.Validate();

        Assert.Equal(status, outcome.Status);
    }

    [Fact]
    public void ValidateRejectsEmptyMissionId()
    {
        var outcome = ValidOutcome() with
        {
            MissionId = Guid.Empty
        };

        Assert.Throws<ArgumentException>(outcome.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateRejectsMissingOperationId(string operationId)
    {
        var outcome = ValidOutcome() with
        {
            OperationId = operationId
        };

        Assert.Throws<ArgumentException>(outcome.Validate);
    }

    [Fact]
    public void ValidateRejectsUndefinedOutcomeStatus()
    {
        var outcome = ValidOutcome() with
        {
            Status = (OperationOutcomeStatus)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(outcome.Validate);
    }

    [Fact]
    public void ValidateRejectsDefaultCompletionTimestamp()
    {
        var outcome = ValidOutcome() with
        {
            CompletedAt = default
        };

        Assert.Throws<ArgumentOutOfRangeException>(outcome.Validate);
    }

    private static OperationOutcome ValidOutcome() =>
        new(
            Guid.Parse("3f7ef2ce-f3b3-4cb4-9f51-82dc41ad4097"),
            "support-001",
            OperationOutcomeStatus.Success,
            new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero));
}
