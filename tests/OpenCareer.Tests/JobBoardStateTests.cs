using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public class JobBoardStateTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NormalReconcileKeepsUnexpiredJobsAndOnlyFillsOpenSlots()
    {
        var board = JobBoardState.Empty("KRME", Epoch);
        var first = Offer(Guid.NewGuid(), Epoch, Epoch.AddHours(8), "KALB");
        var second = Offer(Guid.NewGuid(), Epoch, Epoch.AddHours(2), "KBOS");
        board = board.Reconcile(Epoch, 2, [first, second]);

        var replacement = Offer(Guid.NewGuid(), Epoch.AddHours(2), Epoch.AddHours(9), "KPHL");
        board = board.Reconcile(Epoch.AddHours(2), 2, [replacement]);

        Assert.Equal(2, board.Offers.Length);
        Assert.Contains(board.Offers, x => x.OfferId == first.OfferId);
        Assert.DoesNotContain(board.Offers, x => x.OfferId == second.OfferId);
        Assert.Contains(board.Offers, x => x.OfferId == replacement.OfferId);
        Assert.Contains(second.OfferId, board.RetiredOfferIds);
    }

    [Fact]
    public void ConsumedOfferCannotBeReintroducedBySameCandidateBatch()
    {
        var offer = Offer(Guid.NewGuid(), Epoch, Epoch.AddHours(8), "KALB");
        var board = JobBoardState.Empty("KRME", Epoch).Reconcile(Epoch, 1, [offer]);
        board = board.Retire(offer.OfferId, Epoch.AddMinutes(15));
        board = board.Reconcile(Epoch.AddMinutes(20), 1, [offer]);

        Assert.Empty(board.Offers);
        Assert.Contains(offer.OfferId, board.RetiredOfferIds);
    }

    [Fact]
    public void MajorEventCanExplicitlyReplaceTheBoard()
    {
        var oldOffer = Offer(Guid.NewGuid(), Epoch, Epoch.AddHours(12), "KALB");
        var emergency = Offer(Guid.NewGuid(), Epoch.AddHours(1), Epoch.AddHours(4), "KIAD") with
        {
            ServiceTrack = ServiceTrack.GovernmentContract,
            Kind = ContractKind.Evacuation,
            Scenario = JobScenarioKind.HumanitarianAirlift
        };
        var board = JobBoardState.Empty("KRME", Epoch).Reconcile(Epoch, 1, [oldOffer]);

        board = board.Reconcile(Epoch.AddHours(1), 1, [emergency], forceReplace: true);

        Assert.Single(board.Offers);
        Assert.Equal(emergency.OfferId, board.Offers[0].OfferId);
        Assert.Contains(oldOffer.OfferId, board.RetiredOfferIds);
    }

    [Fact]
    public void LowerTargetDoesNotDeleteStillValidJobs()
    {
        var first = Offer(Guid.NewGuid(), Epoch, Epoch.AddHours(8), "KALB");
        var second = Offer(Guid.NewGuid(), Epoch, Epoch.AddHours(8), "KBOS");
        var board = JobBoardState.Empty("KRME", Epoch).Reconcile(Epoch, 2, [first, second]);

        board = board.Reconcile(Epoch.AddHours(1), 1, Array.Empty<JobMarketOfferDraft>());

        Assert.Equal(2, board.Offers.Length);
    }

    private static JobMarketOfferDraft Offer(
        Guid id,
        DateTimeOffset offeredAt,
        DateTimeOffset expiresAt,
        string destination) => new(
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
