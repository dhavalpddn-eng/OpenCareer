using OpenCareer.Domain.Military;

namespace OpenCareer.Domain.Conflict;

public sealed record FactionInfluenceState(
    string FriendlyFactionId,
    string HostileFactionId,
    double FriendlyInfluence,
    double HostileInfluence)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FriendlyFactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(HostileFactionId);

        if (string.Equals(
            FriendlyFactionId,
            HostileFactionId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Friendly and hostile faction IDs must be distinct.");
        }

        ValidateInfluence(FriendlyInfluence, nameof(FriendlyInfluence));
        ValidateInfluence(HostileInfluence, nameof(HostileInfluence));
    }

    private static void ValidateInfluence(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class FactionInfluenceConsequence
{
    public static FactionInfluenceState Apply(
        FactionInfluenceState current,
        OperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(outcome);

        current.Validate();
        outcome.Validate();

        double shift = outcome.Status switch
        {
            OperationOutcomeStatus.Success => 0.04,
            OperationOutcomeStatus.PartialSuccess => 0.02,
            OperationOutcomeStatus.Failure => -0.03,
            OperationOutcomeStatus.Aborted => -0.01,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome.Status))
        };

        var updated = current with
        {
            FriendlyInfluence = Clamp(current.FriendlyInfluence + shift),
            HostileInfluence = Clamp(current.HostileInfluence - shift)
        };

        updated.Validate();
        return updated;
    }

    private static double Clamp(double value) =>
        Math.Clamp(value, 0, 1);
}
