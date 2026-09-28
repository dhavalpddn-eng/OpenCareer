using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class FactionInfluenceConsequenceTests
{
    [Theory]
    [InlineData(OperationOutcomeStatus.Success, 0.54, 0.46)]
    [InlineData(OperationOutcomeStatus.PartialSuccess, 0.52, 0.48)]
    [InlineData(OperationOutcomeStatus.Failure, 0.47, 0.53)]
    [InlineData(OperationOutcomeStatus.Aborted, 0.49, 0.51)]
    public void ApplyChangesInfluenceDeterministically(
        OperationOutcomeStatus status,
        double expectedFriendly,
        double expectedHostile)
    {
        FactionInfluenceState result =
            FactionInfluenceConsequence.Apply(
                BalancedState(),
                Outcome(status));

        Assert.Equal(expectedFriendly, result.FriendlyInfluence, precision: 10);
        Assert.Equal(expectedHostile, result.HostileInfluence, precision: 10);
    }

    [Fact]
    public void SuccessClampsInfluenceAtBounds()
    {
        var current = BalancedState() with
        {
            FriendlyInfluence = 0.99,
            HostileInfluence = 0.01
        };

        FactionInfluenceState result =
            FactionInfluenceConsequence.Apply(
                current,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(1, result.FriendlyInfluence);
        Assert.Equal(0, result.HostileInfluence);
    }

    [Fact]
    public void FailureClampsInfluenceAtBounds()
    {
        var current = BalancedState() with
        {
            FriendlyInfluence = 0.01,
            HostileInfluence = 0.99
        };

        FactionInfluenceState result =
            FactionInfluenceConsequence.Apply(
                current,
                Outcome(OperationOutcomeStatus.Failure));

        Assert.Equal(0, result.FriendlyInfluence);
        Assert.Equal(1, result.HostileInfluence);
    }

    [Fact]
    public void ApplyPreservesFactionIdentity()
    {
        FactionInfluenceState current = BalancedState();

        FactionInfluenceState result =
            FactionInfluenceConsequence.Apply(
                current,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(current.FriendlyFactionId, result.FriendlyFactionId);
        Assert.Equal(current.HostileFactionId, result.HostileFactionId);
    }

    [Fact]
    public void StateRejectsDuplicateFactionIds()
    {
        var state = new FactionInfluenceState(
            "faction:ast",
            "faction:ast",
            FriendlyInfluence: 0.5,
            HostileInfluence: 0.5);

        Assert.Throws<ArgumentException>(state.Validate);
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.5, -0.01)]
    [InlineData(0.5, 1.01)]
    public void StateRejectsOutOfRangeInfluence(
        double friendly,
        double hostile)
    {
        var state = BalancedState() with
        {
            FriendlyInfluence = friendly,
            HostileInfluence = hostile
        };

        Assert.Throws<ArgumentOutOfRangeException>(state.Validate);
    }

    private static FactionInfluenceState BalancedState() =>
        new(
            "faction:ast",
            "faction:ghc",
            FriendlyInfluence: 0.5,
            HostileInfluence: 0.5);

    private static OperationOutcome Outcome(OperationOutcomeStatus status) =>
        new(
            Guid.Parse("3ef610b1-a0bb-4bf8-8acb-67a91139ed62"),
            "support-005",
            status,
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));
}
