using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class TerritoryPressureConsequenceTests
{
    [Fact]
    public void SingleSuccessBuildsPressureWithoutChangingControl()
    {
        TerritoryPressureResult result =
            TerritoryPressureConsequence.Apply(
                Baseline(),
                Outcome(OperationOutcomeStatus.Success));

        Assert.False(result.ThresholdCrossed);
        Assert.Equal(0, result.FriendlyControlDelta);
        Assert.Equal(
            0.10,
            result.State.AccumulatedFriendlyPressure,
            precision: 10);
    }

    [Fact]
    public void TwoSuccessesCrossThresholdAndEmitOneControlStep()
    {
        TerritoryPressureResult first =
            TerritoryPressureConsequence.Apply(
                Baseline(),
                Outcome(OperationOutcomeStatus.Success));

        TerritoryPressureResult second =
            TerritoryPressureConsequence.Apply(
                first.State,
                Outcome(OperationOutcomeStatus.Success));

        Assert.True(second.ThresholdCrossed);
        Assert.Equal(
            0.04,
            second.FriendlyControlDelta,
            precision: 10);
        Assert.Equal(
            0,
            second.State.AccumulatedFriendlyPressure,
            precision: 10);
    }

    [Fact]
    public void PartialSuccessRequiresAccumulationBeforeControlChange()
    {
        TerritoryPressureState state = Baseline();
        TerritoryPressureResult result = null!;

        for (int index = 0; index < 4; index++)
        {
            result = TerritoryPressureConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.PartialSuccess));

            state = result.State;
        }

        Assert.True(result.ThresholdCrossed);
        Assert.Equal(
            0.04,
            result.FriendlyControlDelta,
            precision: 10);
        Assert.Equal(
            0,
            result.State.AccumulatedFriendlyPressure,
            precision: 10);
    }

    [Fact]
    public void RepeatedFailuresEventuallyEmitHostileControlStep()
    {
        TerritoryPressureState state = Baseline();
        TerritoryPressureResult result = null!;

        for (int index = 0; index < 3; index++)
        {
            result = TerritoryPressureConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Failure));

            state = result.State;
        }

        Assert.True(result.ThresholdCrossed);
        Assert.Equal(
            -0.04,
            result.FriendlyControlDelta,
            precision: 10);
        Assert.Equal(
            -0.04,
            result.State.AccumulatedFriendlyPressure,
            precision: 10);
    }

    [Fact]
    public void CrossingThresholdPreservesResidualPressure()
    {
        var state = Baseline() with
        {
            AccumulatedFriendlyPressure = 0.15
        };

        TerritoryPressureResult result =
            TerritoryPressureConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(
            0.04,
            result.FriendlyControlDelta,
            precision: 10);
        Assert.Equal(
            0.05,
            result.State.AccumulatedFriendlyPressure,
            precision: 10);
    }

    [Fact]
    public void ApplyPreservesSectorIdentity()
    {
        TerritoryPressureState state = Baseline();

        TerritoryPressureResult result =
            TerritoryPressureConsequence.Apply(
                state,
                Outcome(OperationOutcomeStatus.Aborted));

        Assert.Equal(state.SectorId, result.State.SectorId);
    }

    [Theory]
    [InlineData(-0.20)]
    [InlineData(0.20)]
    [InlineData(-0.21)]
    [InlineData(0.21)]
    public void StateRejectsAlreadyResolvedThresholdPressure(double pressure)
    {
        var state = Baseline() with
        {
            AccumulatedFriendlyPressure = pressure
        };

        Assert.Throws<ArgumentOutOfRangeException>(state.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void StateRejectsMissingSectorId(string sectorId)
    {
        var state = Baseline() with
        {
            SectorId = sectorId
        };

        Assert.Throws<ArgumentException>(state.Validate);
    }

    private static TerritoryPressureState Baseline() =>
        new(
            "sector-alpha",
            AccumulatedFriendlyPressure: 0);

    private static OperationOutcome Outcome(OperationOutcomeStatus status) =>
        new(
            Guid.Parse("dd7d8165-26bd-44cb-a30c-10f84c9b1470"),
            "support-007",
            status,
            new DateTimeOffset(
                2026,
                9,
                21,
                2,
                0,
                0,
                TimeSpan.Zero));
}
