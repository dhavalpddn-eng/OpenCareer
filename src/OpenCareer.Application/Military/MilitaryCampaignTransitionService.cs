using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

public interface IConflictTheaterCatalog
{
    IReadOnlyList<ConflictTheaterTemplate> GetCandidates();
}

public sealed class DefaultConflictTheaterCatalog : IConflictTheaterCatalog
{
    private static readonly IReadOnlyList<ConflictTheaterTemplate> Candidates =
        Array.AsReadOnly(new[]
    {
        new(
            "FICTIONAL-HIGHLANDS",
            new GeoPoint(39.0, -106.0),
            RadiusNauticalMiles: 120,
            FriendlyGroundUnits: 8,
            HostileGroundUnits: 8,
            FriendlyAirUnits: 4,
            HostileAirUnits: 4),
        new(
            "FICTIONAL-COAST",
            new GeoPoint(36.0, -121.5),
            RadiusNauticalMiles: 110,
            FriendlyGroundUnits: 7,
            HostileGroundUnits: 7,
            FriendlyAirUnits: 4,
            HostileAirUnits: 4),
        new(
            "FICTIONAL-DESERT",
            new GeoPoint(33.5, -111.0),
            RadiusNauticalMiles: 135,
            FriendlyGroundUnits: 9,
            HostileGroundUnits: 9,
            FriendlyAirUnits: 5,
            HostileAirUnits: 5),
        new(
            "FICTIONAL-PLAINS",
            new GeoPoint(35.5, -98.0),
            RadiusNauticalMiles: 125,
            FriendlyGroundUnits: 8,
            HostileGroundUnits: 8,
            FriendlyAirUnits: 4,
            HostileAirUnits: 4)
    });

    public IReadOnlyList<ConflictTheaterTemplate> GetCandidates() =>
        Candidates;
}

public sealed record MilitarySuccessorOperationOffer(
    string SourceCampaignId,
    long SourceRevision,
    ConflictCampaignSuccessorOffer PlannedOffer)
{
    public string CampaignId => PlannedOffer.CampaignId;

    public string TheaterId => PlannedOffer.Theater.TheaterId;

    public string OperationName => PlannedOffer.Identity.OperationName;

    public ConflictFactionIdentity FriendlyFaction =>
        PlannedOffer.Identity.FriendlyFaction;

    public ConflictFactionIdentity HostileFaction =>
        PlannedOffer.Identity.HostileFaction;

    public DateTimeOffset AvailableAt =>
        PlannedOffer.AvailableAt;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceCampaignId);

        if (SourceRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(SourceRevision));

        ArgumentNullException.ThrowIfNull(PlannedOffer);
        PlannedOffer.Validate();
    }
}

public sealed class MilitaryCampaignTransitionService
{
    private readonly ConflictCampaignRuntimeState _runtime;
    private readonly ConflictCampaignCoordinator _campaigns;
    private readonly IConflictTheaterCatalog _theaters;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _acceptGate = new(1, 1);
    private readonly object _decisionSync = new();

    private (string CampaignId, long Revision)? _declinedSource;

    public MilitaryCampaignTransitionService(
        ConflictCampaignRuntimeState runtime,
        ConflictCampaignCoordinator campaigns,
        IConflictTheaterCatalog theaters,
        TimeProvider timeProvider)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _campaigns = campaigns ?? throw new ArgumentNullException(nameof(campaigns));
        _theaters = theaters ?? throw new ArgumentNullException(nameof(theaters));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public MilitarySuccessorOperationOffer? GetCurrentOffer()
    {
        ConflictCampaignStoreRecord? current = _runtime.Current;

        if (current is null || !current.Checkpoint.CampaignState.IsTerminal)
            return null;

        current.Validate();

        lock (_decisionSync)
        {
            if (_declinedSource is { } declined
                && string.Equals(
                    declined.CampaignId,
                    current.Checkpoint.CampaignId,
                    StringComparison.Ordinal)
                && declined.Revision == current.Revision)
            {
                return null;
            }
        }

        IReadOnlyList<ConflictTheaterTemplate> candidates =
            _theaters.GetCandidates();

        if (candidates.Count == 0)
            return null;

        DateTimeOffset availableAt = _timeProvider.GetUtcNow();
        if (availableAt < current.Checkpoint.SavedAt)
            availableAt = current.Checkpoint.SavedAt;

        ConflictCampaignSuccessorOffer planned =
            ConflictCampaignTransitionPlanner.Plan(
                current,
                candidates,
                availableAt);

        var offer = new MilitarySuccessorOperationOffer(
            current.Checkpoint.CampaignId,
            current.Revision,
            planned);

        offer.Validate();
        return offer;
    }

    public bool Decline(MilitarySuccessorOperationOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate();

        ConflictCampaignStoreRecord? current = _runtime.Current;
        if (!MatchesCurrentSource(current, offer))
            return false;

        lock (_decisionSync)
        {
            _declinedSource = (
                offer.SourceCampaignId,
                offer.SourceRevision);
        }

        return true;
    }

    public bool Reconsider()
    {
        ConflictCampaignStoreRecord? current = _runtime.Current;
        if (current is null)
            return false;

        lock (_decisionSync)
        {
            if (_declinedSource is not { } declined
                || !string.Equals(
                    declined.CampaignId,
                    current.Checkpoint.CampaignId,
                    StringComparison.Ordinal)
                || declined.Revision != current.Revision)
            {
                return false;
            }

            _declinedSource = null;
            return true;
        }
    }

    public async Task<ConflictCampaignStoreRecord> AcceptAsync(
        MilitarySuccessorOperationOffer offer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        offer.Validate();

        await _acceptGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            ConflictCampaignStoreRecord current =
                _runtime.Current
                ?? throw new InvalidOperationException(
                    "No military campaign is loaded.");

            if (!MatchesCurrentSource(current, offer))
            {
                throw new ConflictCampaignConcurrencyException(
                    "The successor-operation offer is stale because the current campaign changed.");
            }

            if (!current.Checkpoint.CampaignState.IsTerminal)
            {
                throw new InvalidOperationException(
                    "A successor operation can only be accepted after the current campaign ends.");
            }

            ConflictCampaignSuccessorOffer expected =
                ConflictCampaignTransitionPlanner.Plan(
                    current,
                    _theaters.GetCandidates(),
                    offer.AvailableAt);

            if (expected != offer.PlannedOffer)
            {
                throw new InvalidOperationException(
                    "The successor-operation offer no longer matches the current theater catalog.");
            }

            ConflictCampaignStoreRecord successor =
                await _campaigns
                    .CreateSuccessorAsync(
                        current,
                        offer.PlannedOffer,
                        cancellationToken)
                    .ConfigureAwait(false);

            _runtime.Replace(successor);

            lock (_decisionSync)
                _declinedSource = null;

            return successor;
        }
        finally
        {
            _acceptGate.Release();
        }
    }

    private static bool MatchesCurrentSource(
        ConflictCampaignStoreRecord? current,
        MilitarySuccessorOperationOffer offer) =>
        current is not null
        && current.Revision == offer.SourceRevision
        && string.Equals(
            current.Checkpoint.CampaignId,
            offer.SourceCampaignId,
            StringComparison.Ordinal);
}
