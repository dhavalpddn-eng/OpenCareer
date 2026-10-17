using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class JobBoardStateTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReconcileKeepsUnexpiredOffersAndOnlyFillsOpenSlots()
    {
        JobMarketOfferDraft first =
            Offer(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Epoch,
                Epoch.AddHours(8),
                "KALB");
        JobMarketOfferDraft expiring =
            Offer(
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Epoch,
                Epoch.AddHours(2),
                "KBOS");

        JobBoardState board =
            JobBoardState.Empty("krme", Epoch)
                .Reconcile(
                    Epoch,
                    2,
                    [first, expiring]);

        JobMarketOfferDraft replacement =
            Offer(
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Epoch.AddHours(2),
                Epoch.AddHours(9),
                "KPHL");

        board = board.Reconcile(
            Epoch.AddHours(2),
            2,
            [replacement]);

        Assert.Equal(2, board.Offers.Length);
        Assert.Contains(board.Offers, x => x.OfferId == first.OfferId);
        Assert.DoesNotContain(board.Offers, x => x.OfferId == expiring.OfferId);
        Assert.Contains(board.Offers, x => x.OfferId == replacement.OfferId);
        Assert.Contains(expiring.OfferId, board.RetiredOfferIds);
    }

    [Fact]
    public void RetiredOfferCannotBeReintroduced()
    {
        JobMarketOfferDraft offer =
            Offer(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Epoch,
                Epoch.AddHours(8),
                "KALB");

        JobBoardState board =
            JobBoardState.Empty("KRME", Epoch)
                .Reconcile(Epoch, 1, [offer])
                .Retire(
                    offer.OfferId,
                    Epoch.AddMinutes(15))
                .Reconcile(
                    Epoch.AddMinutes(20),
                    1,
                    [offer]);

        Assert.Empty(board.Offers);
        Assert.Contains(offer.OfferId, board.RetiredOfferIds);
    }

    [Fact]
    public void ForceReplaceRetiresExistingBoard()
    {
        JobMarketOfferDraft oldOffer =
            Offer(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Epoch,
                Epoch.AddHours(12),
                "KALB");

        JobMarketOfferDraft replacement =
            Offer(
                Guid.Parse("30000000-0000-0000-0000-000000000002"),
                Epoch.AddHours(1),
                Epoch.AddHours(4),
                "KIAD") with
            {
                ServiceTrack = ServiceTrack.GovernmentContract,
                Kind = ContractKind.Evacuation,
                Scenario = JobScenarioKind.HumanitarianAirlift
            };

        JobBoardState board =
            JobBoardState.Empty("KRME", Epoch)
                .Reconcile(Epoch, 1, [oldOffer])
                .Reconcile(
                    Epoch.AddHours(1),
                    1,
                    [replacement],
                    forceReplace: true);

        JobMarketOfferDraft remaining = Assert.Single(board.Offers);
        Assert.Equal(replacement.OfferId, remaining.OfferId);
        Assert.Contains(oldOffer.OfferId, board.RetiredOfferIds);
    }

    [Fact]
    public void LowerTargetDoesNotDeleteStillValidOffers()
    {
        JobMarketOfferDraft first =
            Offer(
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                Epoch,
                Epoch.AddHours(8),
                "KALB");
        JobMarketOfferDraft second =
            Offer(
                Guid.Parse("40000000-0000-0000-0000-000000000002"),
                Epoch,
                Epoch.AddHours(8),
                "KBOS");

        JobBoardState board =
            JobBoardState.Empty("KRME", Epoch)
                .Reconcile(Epoch, 2, [first, second])
                .Reconcile(
                    Epoch.AddHours(1),
                    1,
                    Array.Empty<JobMarketOfferDraft>());

        Assert.Equal(2, board.Offers.Length);
    }

    [Fact]
    public void InvalidOfferIdentityOrRouteIsRejected()
    {
        JobBoardState board =
            JobBoardState.Empty("KRME", Epoch);

        JobMarketOfferDraft invalidIdentity =
            Offer(
                Guid.Empty,
                Epoch,
                Epoch.AddHours(2),
                "KALB");

        Assert.Throws<ArgumentException>(
            () => board.Reconcile(
                Epoch,
                1,
                [invalidIdentity]));

        JobMarketOfferDraft wrongOrigin =
            Offer(
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Epoch,
                Epoch.AddHours(2),
                "KALB") with
            {
                OriginIcao = "KSYR"
            };

        JobBoardState unchanged =
            board.Reconcile(
                Epoch,
                1,
                [wrongOrigin]);

        Assert.Empty(unchanged.Offers);
    }

    private static JobMarketOfferDraft Offer(
        Guid id,
        DateTimeOffset offeredAt,
        DateTimeOffset expiresAt,
        string destination) =>
        new(
            id,
            ServiceTrack.CivilianEmployment,
            ContractKind.Cargo,
            JobScenarioKind.Standard,
            "KRME",
            destination,
            100,
            2.5,
            offeredAt,
            expiresAt,
            false,
            0,
            0,
            1);
}
