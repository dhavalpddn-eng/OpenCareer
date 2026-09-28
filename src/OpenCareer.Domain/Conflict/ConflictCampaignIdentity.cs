using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Conflict;

public sealed record ConflictFactionIdentity(
    string FactionId,
    string DisplayName,
    string ShortCode,
    ConflictSide Side)
{
    public ConflictFactionOperationalPosture Posture { get; init; } =
        ConflictFactionOperationalPosture.Defensive;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ShortCode);

        if (Side == ConflictSide.Neutral)
        {
            throw new ArgumentException(
                "Campaign belligerent identity cannot use the neutral side.",
                nameof(Side));
        }

        if (!Enum.IsDefined(Posture))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Posture),
                "Faction operational posture is not supported.");
        }

        if (ShortCode.Length is < 2 or > 6
            || ShortCode.Any(character =>
                !IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "Faction short code must contain 2-6 ASCII letters or digits.",
                nameof(ShortCode));
        }
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or >= '0' and <= '9';
}

public sealed record ConflictCampaignIdentity(
    string OperationId,
    string OperationName,
    ConflictFactionIdentity FriendlyFaction,
    ConflictFactionIdentity HostileFaction)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationName);
        ArgumentNullException.ThrowIfNull(FriendlyFaction);
        ArgumentNullException.ThrowIfNull(HostileFaction);

        FriendlyFaction.Validate();
        HostileFaction.Validate();

        if (FriendlyFaction.Side != ConflictSide.Friendly)
            throw new ArgumentException("Friendly faction must use the friendly side.");

        if (HostileFaction.Side != ConflictSide.Hostile)
            throw new ArgumentException("Hostile faction must use the hostile side.");

        if (string.Equals(
            FriendlyFaction.FactionId,
            HostileFaction.FactionId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Friendly and hostile factions must have distinct IDs.");
        }

        if (string.Equals(
            FriendlyFaction.DisplayName,
            HostileFaction.DisplayName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Friendly and hostile factions must have distinct names.");
        }
    }
}

public static class ConflictCampaignIdentityGenerator
{
    private sealed record FactionName(
        string Name,
        string Code);

    private static readonly FactionName[] Factions =
    {
        new("Aster Coalition", "AST"),
        new("Greyhaven Compact", "GHC"),
        new("Highfield Accord", "HFA"),
        new("Bluecrest Union", "BCU"),
        new("Stonebridge League", "SBL"),
        new("Evermark Assembly", "EMA"),
        new("Northlake Coalition", "NLC"),
        new("Sable Ridge Directorate", "SRD"),
        new("Brightwater Compact", "BWC"),
        new("Westmere Accord", "WMA")
    };

    private static readonly string[] OperationAdjectives =
    {
        "Copper",
        "Silver",
        "Quiet",
        "Northern",
        "Steady",
        "Blue",
        "Golden",
        "Resolute",
        "Clear",
        "Distant"
    };

    private static readonly string[] OperationNouns =
    {
        "Beacon",
        "Compass",
        "Harbor",
        "Horizon",
        "Lattice",
        "Sentinel",
        "Passage",
        "Lantern",
        "Waypoint",
        "Bridge"
    };

    public static ConflictCampaignIdentity Create(
        string campaignId,
        ConflictWorldState world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ConflictValidation.Validate(world);

        DeterministicRandom random = DeterministicSeed.CreateStream(
            world.TheaterSeed,
            $"conflict-identity:{campaignId}:{world.TheaterId}");

        int friendlyIndex = random.NextInt(0, Factions.Length);
        int hostileIndex = random.NextInt(0, Factions.Length - 1);
        if (hostileIndex >= friendlyIndex)
            hostileIndex++;

        FactionName friendly = Factions[friendlyIndex];
        FactionName hostile = Factions[hostileIndex];

        ConflictFactionOperationalPosture friendlyPosture =
            NextPosture(random);

        ConflictFactionOperationalPosture hostilePosture =
            NextPosture(random);

        string operationName =
            $"Operation {OperationAdjectives[random.NextInt(0, OperationAdjectives.Length)]} " +
            OperationNouns[random.NextInt(0, OperationNouns.Length)];

        var identity = new ConflictCampaignIdentity(
            OperationId: $"operation:{campaignId}",
            operationName,
            new ConflictFactionIdentity(
                $"faction:{friendly.Code.ToLowerInvariant()}",
                friendly.Name,
                friendly.Code,
                ConflictSide.Friendly)
            {
                Posture = friendlyPosture
            },
            new ConflictFactionIdentity(
                $"faction:{hostile.Code.ToLowerInvariant()}",
                hostile.Name,
                hostile.Code,
                ConflictSide.Hostile)
            {
                Posture = hostilePosture
            });

        identity.Validate();
        return identity;
    }

    private static ConflictFactionOperationalPosture NextPosture(
        DeterministicRandom random)
    {
        ConflictFactionOperationalPosture[] values =
            Enum.GetValues<ConflictFactionOperationalPosture>();

        return values[random.NextInt(0, values.Length)];
    }
}
