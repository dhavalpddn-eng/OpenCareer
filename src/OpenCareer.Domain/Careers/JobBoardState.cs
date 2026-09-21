using System.Collections.Immutable;

namespace OpenCareer.Domain.Careers;

public sealed record JobBoardState(
    string AirportIcao,
    DateTimeOffset UpdatedAt,
    ImmutableArray<JobMarketOfferDraft> Offers,
    ImmutableHashSet<Guid> RetiredOfferIds)
{
    public static JobBoardState Empty(
        string airportIcao,
        DateTimeOffset time) =>
        new(
            JobMarketIcao.Normalize(airportIcao),
            time,
            ImmutableArray<JobMarketOfferDraft>.Empty,
            ImmutableHashSet<Guid>.Empty);

    public JobBoardState Reconcile(
        DateTimeOffset time,
        int targetVisibleOffers,
        IEnumerable<JobMarketOfferDraft> candidateOffers,
        bool forceReplace = false)
    {
        ArgumentNullException.ThrowIfNull(candidateOffers);
        Validate();

        if (time < UpdatedAt)
            throw new InvalidOperationException(
                "Job board time cannot move backwards.");

        if (targetVisibleOffers is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(targetVisibleOffers));

        ImmutableHashSet<Guid> retired = RetiredOfferIds;
        var survivors = ImmutableArray.CreateBuilder<JobMarketOfferDraft>();

        foreach (JobMarketOfferDraft offer in Offers)
        {
            if (forceReplace || offer.ExpiresAt <= time)
            {
                retired = retired.Add(offer.OfferId);
                continue;
            }

            survivors.Add(offer);
        }

        HashSet<Guid> activeIds =
            survivors.Select(x => x.OfferId).ToHashSet();

        if (survivors.Count < targetVisibleOffers)
        {
            foreach (JobMarketOfferDraft candidate in candidateOffers)
            {
                ValidateOffer(candidate);

                if (!string.Equals(
                        candidate.OriginIcao,
                        AirportIcao,
                        StringComparison.Ordinal)
                    || candidate.OfferedAt > time
                    || candidate.ExpiresAt <= time
                    || retired.Contains(candidate.OfferId)
                    || activeIds.Contains(candidate.OfferId))
                {
                    continue;
                }

                survivors.Add(candidate);
                activeIds.Add(candidate.OfferId);

                if (survivors.Count >= targetVisibleOffers)
                    break;
            }
        }

        return this with
        {
            UpdatedAt = time,
            Offers = survivors.ToImmutable(),
            RetiredOfferIds = retired
        };
    }

    public JobBoardState Retire(
        Guid offerId,
        DateTimeOffset time)
    {
        Validate();

        if (offerId == Guid.Empty)
            throw new ArgumentException(
                "Offer identity is required.",
                nameof(offerId));

        if (time < UpdatedAt)
            throw new InvalidOperationException(
                "Job board time cannot move backwards.");

        ImmutableArray<JobMarketOfferDraft> offers =
            Offers
                .Where(x => x.OfferId != offerId)
                .ToImmutableArray();

        bool existed = offers.Length != Offers.Length;

        return this with
        {
            UpdatedAt = time,
            Offers = offers,
            RetiredOfferIds =
                existed
                    ? RetiredOfferIds.Add(offerId)
                    : RetiredOfferIds
        };
    }

    public void Validate()
    {
        if (!string.Equals(
                JobMarketIcao.Normalize(AirportIcao),
                AirportIcao,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Airport ICAO must be normalized.",
                nameof(AirportIcao));
        }

        if (Offers.IsDefault)
            throw new ArgumentException(
                "Offers collection must be initialized.",
                nameof(Offers));

        ArgumentNullException.ThrowIfNull(RetiredOfferIds);

        var activeIds = new HashSet<Guid>();
        foreach (JobMarketOfferDraft offer in Offers)
        {
            ValidateOffer(offer);

            if (!string.Equals(
                    offer.OriginIcao,
                    AirportIcao,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "All board offers must originate at the board airport.",
                    nameof(Offers));
            }

            if (!activeIds.Add(offer.OfferId))
                throw new ArgumentException(
                    "Duplicate active offer identity.",
                    nameof(Offers));

            if (RetiredOfferIds.Contains(offer.OfferId))
                throw new ArgumentException(
                    "An offer cannot be active and retired simultaneously.",
                    nameof(Offers));
        }
    }

    private static void ValidateOffer(
        JobMarketOfferDraft offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        string origin = JobMarketIcao.Normalize(offer.OriginIcao);
        string destination = JobMarketIcao.Normalize(offer.DestinationIcao);

        if (!string.Equals(origin, offer.OriginIcao, StringComparison.Ordinal)
            || !string.Equals(destination, offer.DestinationIcao, StringComparison.Ordinal)
            || offer.OfferId == Guid.Empty
            || !Enum.IsDefined(offer.ServiceTrack)
            || !Enum.IsDefined(offer.Kind)
            || !Enum.IsDefined(offer.Scenario)
            || offer.ExpiresAt <= offer.OfferedAt
            || !double.IsFinite(offer.DistanceNm)
            || offer.DistanceNm < 0
            || (offer.EstimatedFlightHours is { } hours
                && (!double.IsFinite(hours) || hours < 0))
            || !double.IsFinite(offer.RouteStrength)
            || offer.RouteStrength is < 0 or > 1
            || !double.IsFinite(offer.RelationshipStrength)
            || offer.RelationshipStrength is < 0 or > 1
            || !double.IsFinite(offer.MarketSelectionWeight)
            || offer.MarketSelectionWeight < 0)
        {
            throw new ArgumentException(
                "Invalid job-market offer.",
                nameof(offer));
        }
    }
}
