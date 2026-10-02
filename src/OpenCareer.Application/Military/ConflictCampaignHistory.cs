using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

public sealed record ConflictCampaignHistoryEntry(
    string CampaignId,
    string TheaterId,
    ConflictCampaignIdentity Identity,
    ConflictCampaignOutcome Outcome,
    ConflictCampaignPhase FinalPhase,
    long EvaluationSequence,
    double FinalFriendlyControlAverage,
    DateTimeOffset EndedAt,
    ConflictFactionOperationalPosture? FinalFriendlyPosture = null,
    ConflictFactionOperationalPosture? FinalHostilePosture = null)
{
    public static ConflictCampaignHistoryEntry FromCheckpoint(
        ConflictCampaignCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.Validate();

        ConflictCampaignState campaign = checkpoint.CampaignState;
        if (!campaign.IsTerminal)
        {
            throw new InvalidOperationException(
                "Only terminal military campaigns can be archived.");
        }

        ConflictCampaignIdentity identity =
            campaign.Identity
            ?? ConflictCampaignIdentityGenerator.Create(
                checkpoint.CampaignId,
                checkpoint.World);

        ConflictCampaignIdentity terminalIdentity =
            ConflictFactionOutcomePosturePolicy.Apply(
                identity,
                campaign.Outcome);

        var entry = new ConflictCampaignHistoryEntry(
            checkpoint.CampaignId,
            checkpoint.World.TheaterId,
            identity,
            campaign.Outcome,
            campaign.Phase,
            campaign.EvaluationSequence,
            campaign.FriendlyControlAverage,
            campaign.UpdatedAt,
            terminalIdentity.FriendlyFaction.Posture,
            terminalIdentity.HostileFaction.Posture);

        entry.Validate();
        return entry;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(TheaterId);
        ArgumentNullException.ThrowIfNull(Identity);
        Identity.Validate();

        if (Outcome == ConflictCampaignOutcome.Ongoing)
        {
            throw new ArgumentException(
                "Completed campaign history cannot use the ongoing outcome.",
                nameof(Outcome));
        }

        if (EvaluationSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(EvaluationSequence));

        if (!double.IsFinite(FinalFriendlyControlAverage)
            || FinalFriendlyControlAverage is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FinalFriendlyControlAverage));
        }

        if (FinalFriendlyPosture is { } friendly && !Enum.IsDefined(friendly))
            throw new ArgumentOutOfRangeException(nameof(FinalFriendlyPosture));
        if (FinalHostilePosture is { } hostile && !Enum.IsDefined(hostile))
            throw new ArgumentOutOfRangeException(nameof(FinalHostilePosture));
    }
}
