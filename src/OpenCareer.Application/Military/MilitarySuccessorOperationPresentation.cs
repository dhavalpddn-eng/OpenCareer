using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

public sealed record MilitarySuccessorOperationPresentation(
    string CampaignId,
    string OperationName,
    string TheaterId,
    string FriendlyFactionName,
    string FriendlyFactionCode,
    ConflictFactionOperationalPosture FriendlyPosture,
    string HostileFactionName,
    string HostileFactionCode,
    ConflictFactionOperationalPosture HostilePosture,
    DateTimeOffset AvailableAt);

public static class MilitarySuccessorOperationPresentationBuilder
{
    public static MilitarySuccessorOperationPresentation Build(
        MilitarySuccessorOperationOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate();

        return new MilitarySuccessorOperationPresentation(
            offer.CampaignId,
            offer.OperationName,
            offer.TheaterId,
            offer.FriendlyFaction.DisplayName,
            offer.FriendlyFaction.ShortCode,
            offer.FriendlyFaction.Posture,
            offer.HostileFaction.DisplayName,
            offer.HostileFaction.ShortCode,
            offer.HostileFaction.Posture,
            offer.AvailableAt);
    }
}
