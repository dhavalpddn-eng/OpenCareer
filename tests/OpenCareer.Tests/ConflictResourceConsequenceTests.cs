using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictResourceConsequenceTests
{
    [Theory]
    [InlineData(
        OperationOutcomeStatus.Success,
        0.54,
        0.53,
        0.47)]
    [InlineData(
        OperationOutcomeStatus.PartialSuccess,
        0.52,
        0.515,
        0.485)]
    [InlineData(
        OperationOutcomeStatus.Failure,
        0.46,
        0.47,
        0.52)]
    [InlineData(
        OperationOutcomeStatus.Aborted,
        0.49,
        0.495,
        0.50)]
    public void ApplyChangesResourcesDeterministically(
        OperationOutcomeStatus status,
        double expectedFriendlySupply,
        double expectedFriendlyReadiness,
        double expectedHostileSupply)
    {
        ConflictResourceState result =
            ConflictResourceConsequence.Apply(
                Baseline(),
                Outcome(status));

        Assert.Equal(
            expectedFriendlySupply,
            result.FriendlySupply,
            precision: 10);
        Assert.Equal(
            expectedFriendlyReadiness,
            result.FriendlyOperationalReadiness,
            precision: 10);
        Assert.Equal(
            expectedHostileSupply,
            result.HostileSupply,
            precision: 10);
    }

    [Fact]
    public void SuccessClampsAllAffectedResourcesAtBounds()
    {
        var state = Baseline() with
        {
            FriendlySupply = 0.99,
            FriendlyOperationalReadiness = 0.99,
            HostileSupply = 0.01
        };

        ConflictResourceState result =
            ConflictResourceConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(1, result.FriendlySupply);
        Assert.Equal(1, result.FriendlyOperationalReadiness);
        Assert.Equal(0, result.HostileSupply);
    }

    [Fact]
    public void FailureClampsAllAffectedResourcesAtBounds()
    {
        var state = Baseline() with
        {
            FriendlySupply = 0.01,
            FriendlyOperationalReadiness = 0.01,
            HostileSupply = 0.99
        };

        ConflictResourceState result =
            ConflictResourceConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Failure));

        Assert.Equal(0, result.FriendlySupply);
        Assert.Equal(0, result.FriendlyOperationalReadiness);
        Assert.Equal(1, result.HostileSupply);
    }

    [Fact]
    public void ApplyPreservesCampaignIdentity()
    {
        ConflictResourceState state = Baseline();

        ConflictResourceState result =
            ConflictResourceConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(state.CampaignId, result.CampaignId);
    }

    [Theory]
    [InlineData(-0.01, 0.5, 0.5)]
    [InlineData(1.01, 0.5, 0.5)]
    [InlineData(0.5, -0.01, 0.5)]
    [InlineData(0.5, 1.01, 0.5)]
    [InlineData(0.5, 0.5, -0.01)]
    [InlineData(0.5, 0.5, 1.01)]
    public void StateRejectsOutOfRangeResources(
        double friendlySupply,
        double friendlyReadiness,
        double hostileSupply)
    {
        var state = Baseline() with
        {
            FriendlySupply = friendlySupply,
            FriendlyOperationalReadiness = friendlyReadiness,
            HostileSupply = hostileSupply
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

    private static ConflictResourceState Baseline() =>
        new(
            "campaign:test",
            FriendlySupply: 0.50,
            FriendlyOperationalReadiness: 0.50,
            HostileSupply: 0.50);

    private static OperationOutcome Outcome(OperationOutcomeStatus status) =>
        new(
            Guid.Parse("1bd66acd-f52f-4bc6-bdf2-463f7981bb38"),
            "support-009",
            status,
            new DateTimeOffset(
                2026,
                9,
                21,
                4,
                0,
                0,
                TimeSpan.Zero));
}
