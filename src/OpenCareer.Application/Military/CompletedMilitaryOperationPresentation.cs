using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

/// <summary>
/// Stable application-layer presentation of an archived military operation.
/// Terminal faction postures are read from the persisted history entry and are
/// never recalculated from the outcome at presentation time.
/// </summary>
public sealed record CompletedMilitaryOperationPresentation(
    string CampaignId,
    string OperationName,
    string TheaterId,
    ConflictCampaignOutcome Outcome,
    ConflictCampaignPhase FinalPhase,
    double FinalFriendlyControlAverage,
    DateTimeOffset EndedAt,
    string FriendlyFactionCode,
    string FriendlyFactionName,
    ConflictFactionOperationalPosture FriendlyPosture,
    string HostileFactionCode,
    string HostileFactionName,
    ConflictFactionOperationalPosture HostilePosture);

public static class CompletedMilitaryOperationPresentationBuilder
{
    public static CompletedMilitaryOperationPresentation Build(
        ConflictCampaignHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();

        ConflictFactionOperationalPosture friendlyPosture =
            entry.FinalFriendlyPosture ?? entry.Identity.FriendlyFaction.Posture;
        ConflictFactionOperationalPosture hostilePosture =
            entry.FinalHostilePosture ?? entry.Identity.HostileFaction.Posture;

        return new CompletedMilitaryOperationPresentation(
            entry.CampaignId,
            entry.Identity.OperationName,
            entry.TheaterId,
            entry.Outcome,
            entry.FinalPhase,
            entry.FinalFriendlyControlAverage,
            entry.EndedAt,
            entry.Identity.FriendlyFaction.ShortCode,
            entry.Identity.FriendlyFaction.DisplayName,
            friendlyPosture,
            entry.Identity.HostileFaction.ShortCode,
            entry.Identity.HostileFaction.DisplayName,
            hostilePosture);
    }
}
