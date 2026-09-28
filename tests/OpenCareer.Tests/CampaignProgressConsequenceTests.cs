using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class CampaignProgressConsequenceTests
{
    [Theory]
    [InlineData(OperationOutcomeStatus.Success, 0.56)]
    [InlineData(OperationOutcomeStatus.PartialSuccess, 0.53)]
    [InlineData(OperationOutcomeStatus.Failure, 0.45)]
    [InlineData(OperationOutcomeStatus.Aborted, 0.49)]
    public void ApplyChangesCampaignProgressDeterministically(
        OperationOutcomeStatus status,
        double expected)
    {
        CampaignProgressState result =
            CampaignProgressConsequence.Apply(
                Baseline(),
                Outcome(status));

        Assert.Equal(expected, result.FriendlyProgress, precision: 10);
    }

    [Fact]
    public void SuccessClampsProgressAtUpperBound()
    {
        var state = Baseline() with
        {
            FriendlyProgress = 0.98
        };

        CampaignProgressState result =
            CampaignProgressConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(1, result.FriendlyProgress);
    }

    [Fact]
    public void FailureClampsProgressAtLowerBound()
    {
        var state = Baseline() with
        {
            FriendlyProgress = 0.02
        };

        CampaignProgressState result =
            CampaignProgressConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Failure));

        Assert.Equal(0, result.FriendlyProgress);
    }

    [Fact]
    public void ApplyPreservesCampaignIdentity()
    {
        CampaignProgressState current = Baseline();

        CampaignProgressState result =
            CampaignProgressConsequence.Apply(
                current,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(current.CampaignId, result.CampaignId);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void StateRejectsOutOfRangeProgress(double progress)
    {
        var state = Baseline() with
        {
            FriendlyProgress = progress
        };

        Assert.Throws<ArgumentOutOfRangeException>(state.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void StateRejectsMissingCampaignId(string campaignId)
    {
        var state = Baseline() with
        {
            CampaignId = campaignId
        };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    private static CampaignProgressState Baseline() =>
        new(
            "campaign:test",
            FriendlyProgress: 0.50);

    private static OperationOutcome Outcome(OperationOutcomeStatus status) =>
        new(
            Guid.Parse("d3e4c67b-7812-457a-8e8e-c2f4ac8aeeb8"),
            "support-006",
            status,
            new DateTimeOffset(2026, 9, 21, 1, 0, 0, TimeSpan.Zero));
}
