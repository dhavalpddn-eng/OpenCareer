using OpenCareer.Domain.Military;

namespace OpenCareer.Domain.Conflict;

public sealed record TerritoryPressureState(
    string SectorId,
    double AccumulatedFriendlyPressure)
{
    public const double ControlThreshold = 0.20;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SectorId);

        if (!double.IsFinite(AccumulatedFriendlyPressure)
            || AccumulatedFriendlyPressure <= -ControlThreshold
            || AccumulatedFriendlyPressure >= ControlThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccumulatedFriendlyPressure),
                "Accumulated territory pressure must remain inside the unresolved threshold.");
        }
    }
}

public sealed record TerritoryPressureResult(
    TerritoryPressureState State,
    double FriendlyControlDelta)
{
    public bool ThresholdCrossed =>
        Math.Abs(FriendlyControlDelta) > 0.0000001;
}

public static class TerritoryPressureConsequence
{
    private const double ControlDeltaPerThreshold = 0.04;
    private const double ComparisonEpsilon = 0.000000001;

    public static TerritoryPressureResult Apply(
        TerritoryPressureState current,
        OperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(outcome);

        current.Validate();
        outcome.Validate();

        double pressureDelta = outcome.Status switch
        {
            OperationOutcomeStatus.Success => 0.10,
            OperationOutcomeStatus.PartialSuccess => 0.05,
            OperationOutcomeStatus.Failure => -0.08,
            OperationOutcomeStatus.Aborted => -0.02,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome.Status))
        };

        double accumulated =
            current.AccumulatedFriendlyPressure + pressureDelta;

        double controlDelta = 0;

        if (accumulated
            >= TerritoryPressureState.ControlThreshold - ComparisonEpsilon)
        {
            accumulated -= TerritoryPressureState.ControlThreshold;
            controlDelta = ControlDeltaPerThreshold;
        }
        else if (accumulated
            <= -TerritoryPressureState.ControlThreshold + ComparisonEpsilon)
        {
            accumulated += TerritoryPressureState.ControlThreshold;
            controlDelta = -ControlDeltaPerThreshold;
        }

        if (Math.Abs(accumulated) < ComparisonEpsilon)
            accumulated = 0;

        var updated = current with
        {
            AccumulatedFriendlyPressure = accumulated
        };

        updated.Validate();

        return new TerritoryPressureResult(
            updated,
            controlDelta);
    }
}
