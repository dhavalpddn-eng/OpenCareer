using OpenCareer.Domain.Military;

namespace OpenCareer.Domain.Conflict;

public sealed record CampaignProgressState(
    string CampaignId,
    double FriendlyProgress)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);

        if (!double.IsFinite(FriendlyProgress)
            || FriendlyProgress is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(FriendlyProgress));
        }
    }
}

public static class CampaignProgressConsequence
{
    public static CampaignProgressState Apply(
        CampaignProgressState current,
        OperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(outcome);

        current.Validate();
        outcome.Validate();

        double delta = outcome.Status switch
        {
            OperationOutcomeStatus.Success => 0.06,
            OperationOutcomeStatus.PartialSuccess => 0.03,
            OperationOutcomeStatus.Failure => -0.05,
            OperationOutcomeStatus.Aborted => -0.01,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome.Status))
        };

        var updated = current with
        {
            FriendlyProgress = Math.Clamp(
                current.FriendlyProgress + delta,
                0,
                1)
        };

        updated.Validate();
        return updated;
    }
}
