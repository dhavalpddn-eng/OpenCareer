using System.Collections.Immutable;

namespace OpenCareer.Domain.Careers;

/// <summary>
/// Persistent airport board state. Offers survive generation buckets until their own expiry/consumption.
/// Major world transitions may explicitly force a replacement, but normal refresh never wipes valid jobs.
/// </summary>
public sealed record JobBoardState(
    string AirportIcao,
    DateTimeOffset UpdatedAt,
    ImmutableArray<JobMarketOfferDraft> Offers,
    ImmutableHashSet<Guid> RetiredOfferIds)
{
    public static JobBoardState Empty(string airportIcao, DateTimeOffset time) => new(
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
            throw new InvalidOperationException("Job board time cannot move backwards.");
        if (targetVisibleOffers is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(targetVisibleOffers));

        var retired = RetiredOfferIds;
        var survivors = ImmutableArray.CreateBuilder<JobMarketOfferDraft>();

        foreach (var offer in Offers)
        {
            if (forceReplace || offer.ExpiresAt <= time)
            {
                retired = retired.Add(offer.OfferId);
                continue;
            }

            survivors.Add(offer);
        }

        var activeIds = survivors.Select(x => x.OfferId).ToHashSet();
        if (survivors.Count < targetVisibleOffers)
        {
            foreach (var candidate in candidateOffers)
            {
                ValidateOffer(candidate);
                if (candidate.OriginIcao != AirportIcao
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

    public JobBoardState Retire(Guid offerId, DateTimeOffset time)
    {
        Validate();
        if (offerId == Guid.Empty)
            throw new ArgumentException("Offer identity is required.", nameof(offerId));
        if (time < UpdatedAt)
            throw new InvalidOperationException("Job board time cannot move backwards.");

        var offers = Offers.Where(x => x.OfferId != offerId).ToImmutableArray();
        var existed = offers.Length != Offers.Length;
        return this with
        {
            UpdatedAt = time,
            Offers = offers,
            RetiredOfferIds = existed ? RetiredOfferIds.Add(offerId) : RetiredOfferIds
        };
    }

    public void Validate()
    {
        if (JobMarketIcao.Normalize(AirportIcao) != AirportIcao)
            throw new ArgumentException("Airport ICAO must be normalized.", nameof(AirportIcao));
        if (Offers.IsDefault)
            throw new ArgumentException("Offers collection must be initialized.", nameof(Offers));
        if (RetiredOfferIds is null)
            throw new ArgumentNullException(nameof(RetiredOfferIds));

        var activeIds = new HashSet<Guid>();
        foreach (var offer in Offers)
        {
            ValidateOffer(offer);
            if (offer.OriginIcao != AirportIcao)
                throw new ArgumentException("All board offers must originate at the board airport.", nameof(Offers));
            if (!activeIds.Add(offer.OfferId))
                throw new ArgumentException("Duplicate active offer identity.", nameof(Offers));
            if (RetiredOfferIds.Contains(offer.OfferId))
                throw new ArgumentException("An offer cannot be active and retired simultaneously.", nameof(Offers));
        }
    }

    private static void ValidateOffer(JobMarketOfferDraft offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (offer.OfferId == Guid.Empty
            || string.IsNullOrWhiteSpace(offer.OriginIcao)
            || string.IsNullOrWhiteSpace(offer.DestinationIcao)
            || offer.ExpiresAt <= offer.OfferedAt
            || !double.IsFinite(offer.DistanceNm)
            || offer.DistanceNm < 0
            || !double.IsFinite(offer.MarketSelectionWeight)
            || offer.MarketSelectionWeight < 0)
        {
            throw new ArgumentException("Invalid job-market offer.", nameof(offer));
        }
    }
}
