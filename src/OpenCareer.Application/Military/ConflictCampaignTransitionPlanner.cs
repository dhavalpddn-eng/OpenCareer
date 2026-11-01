using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Application.Military;

public sealed record ConflictCampaignSuccessorOffer(
    string CampaignId,
    ConflictTheaterTemplate Theater,
    ulong TheaterSeed,
    ConflictCampaignIdentity Identity,
    DateTimeOffset AvailableAt)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentNullException.ThrowIfNull(Theater);
        ArgumentNullException.ThrowIfNull(Identity);

        Theater.Validate();
        Identity.Validate();
    }
}

public static class ConflictCampaignTransitionPlanner
{
    public static ConflictCampaignSuccessorOffer Plan(
        ConflictCampaignStoreRecord completed,
        IEnumerable<ConflictTheaterTemplate> candidateTheaters,
        DateTimeOffset availableAt)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(candidateTheaters);
        completed.Validate();

        if (!completed.Checkpoint.CampaignState.IsTerminal)
        {
            throw new InvalidOperationException(
                "A successor operation can only be planned after the current campaign ends.");
        }

        if (availableAt < completed.Checkpoint.SavedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableAt),
                "A successor operation cannot be offered before the completed checkpoint.");
        }

        ConflictTheaterTemplate[] candidates =
            candidateTheaters.ToArray();

        if (candidates.Length == 0)
        {
            throw new ArgumentException(
                "At least one successor theater is required.",
                nameof(candidateTheaters));
        }

        foreach (ConflictTheaterTemplate candidate in candidates)
            candidate.Validate();

        if (candidates
            .Select(candidate => candidate.TheaterId)
            .Distinct(StringComparer.Ordinal)
            .Count() != candidates.Length)
        {
            throw new ArgumentException(
                "Successor theater IDs must be unique.",
                nameof(candidateTheaters));
        }

        ConflictTheaterTemplate[] eligible =
            candidates.Length > 1
                ? candidates
                    .Where(candidate =>
                        !string.Equals(
                            candidate.TheaterId,
                            completed.Checkpoint.World.TheaterId,
                            StringComparison.Ordinal))
                    .OrderBy(candidate => candidate.TheaterId, StringComparer.Ordinal)
                    .ToArray()
                : candidates;

        if (eligible.Length == 0)
        {
            eligible = candidates
                .OrderBy(candidate => candidate.TheaterId, StringComparer.Ordinal)
                .ToArray();
        }

        string rootCampaignId =
            completed.Checkpoint.History.Length > 0
                ? completed.Checkpoint.History[0].CampaignId
                : completed.Checkpoint.CampaignId;

        int operationSequence =
            checked(completed.Checkpoint.History.Length + 2);

        string campaignId =
            $"{rootCampaignId}-op-{operationSequence:D3}";

        DeterministicRandom random =
            DeterministicSeed.CreateStream(
                completed.Checkpoint.World.TheaterSeed,
                $"conflict-successor:{rootCampaignId}:{operationSequence}:{completed.Checkpoint.CampaignState.Outcome}");

        ConflictTheaterTemplate theater =
            eligible[random.NextInt(0, eligible.Length)];

        ulong theaterSeed = random.NextUInt64();

        ConflictWorldState previewWorld =
            ConflictTheaterGenerator.Generate(
                theater,
                theaterSeed,
                availableAt);

        ConflictCampaignIdentity identity =
            ConflictCampaignIdentityGenerator.Create(
                campaignId,
                previewWorld);

        var offer = new ConflictCampaignSuccessorOffer(
            campaignId,
            theater,
            theaterSeed,
            identity,
            availableAt);

        offer.Validate();
        return offer;
    }
}
