using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

/// <summary>
/// Read-only drill-down projection for one archived military operation.
/// All conflict facts are copied from persisted history through the existing
/// presentation builder; no terminal outcome or faction posture is simulated here.
/// </summary>
public sealed record CompletedMilitaryOperationDetailProjection(
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

public static class CompletedMilitaryOperationDetailProjectionBuilder
{
    public static CompletedMilitaryOperationDetailProjection Build(
        ConflictCampaignHistoryEntry entry)
    {
        CompletedMilitaryOperationPresentation presentation =
            CompletedMilitaryOperationPresentationBuilder.Build(entry);

        return new CompletedMilitaryOperationDetailProjection(
            presentation.CampaignId,
            presentation.OperationName,
            presentation.TheaterId,
            presentation.Outcome,
            presentation.FinalPhase,
            presentation.FinalFriendlyControlAverage,
            presentation.EndedAt,
            presentation.FriendlyFactionCode,
            presentation.FriendlyFactionName,
            presentation.FriendlyPosture,
            presentation.HostileFactionCode,
            presentation.HostileFactionName,
            presentation.HostilePosture);
    }
}
