using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryCampaignTransitionServiceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OngoingCampaignHasNoSuccessorOffer()
    {
        var store = new MemoryStore();
        var runtime = Runtime();
        runtime.Replace(
            new ConflictCampaignStoreRecord(
                1,
                Checkpoint(
                    "ongoing-campaign",
                    ConflictCampaignOutcome.Ongoing)));

        var service = Service(
            runtime,
            store,
            new[]
            {
                Template(
                    "FICTIONAL-SUCCESSOR-A",
                    36,
                    -101)
            });

        Assert.Null(service.GetCurrentOffer());
    }

    [Fact]
    public void TerminalCampaignProducesUiReadyOffer()
    {
        var store = new MemoryStore();
        var runtime = Runtime();
        runtime.Replace(
            new ConflictCampaignStoreRecord(
                4,
                Checkpoint(
                    "completed-root",
                    ConflictCampaignOutcome.Victory)));

        var service = Service(
            runtime,
            store,
            new[]
            {
                Template(
                    "FICTIONAL-SUCCESSOR-A",
                    36,
                    -101),
                Template(
                    "FICTIONAL-SUCCESSOR-B",
                    40,
                    -108)
            });

        MilitarySuccessorOperationOffer? offer =
            service.GetCurrentOffer();

        Assert.NotNull(offer);
        Assert.Equal(
            "completed-root",
            offer!.SourceCampaignId);
        Assert.Equal(4, offer.SourceRevision);
        Assert.StartsWith(
            "completed-root-op-",
            offer.CampaignId);
        Assert.StartsWith(
            "Operation ",
            offer.OperationName);
        Assert.False(
            string.IsNullOrWhiteSpace(
                offer.FriendlyFaction.DisplayName));
        Assert.False(
            string.IsNullOrWhiteSpace(
                offer.HostileFaction.DisplayName));
    }

    [Fact]
    public void DeclineHidesOfferForCurrentRuntimeRevision()
    {
        var store = new MemoryStore();
        var runtime = Runtime();
        runtime.Replace(
            new ConflictCampaignStoreRecord(
                2,
                Checkpoint(
                    "decline-root",
                    ConflictCampaignOutcome.Stalemate)));

        var service = Service(
            runtime,
            store,
            new[]
            {
                Template(
                    "FICTIONAL-SUCCESSOR-A",
                    36,
                    -101)
            });

        MilitarySuccessorOperationOffer offer =
            Assert.IsType<MilitarySuccessorOperationOffer>(
                service.GetCurrentOffer());

        Assert.True(service.Decline(offer));
        Assert.Null(service.GetCurrentOffer());

        Assert.True(service.Reconsider());

        MilitarySuccessorOperationOffer reconsidered =
            Assert.IsType<MilitarySuccessorOperationOffer>(
                service.GetCurrentOffer());

        Assert.Equal(
            offer.CampaignId,
            reconsidered.CampaignId);
        Assert.Equal(
            offer.OperationName,
            reconsidered.OperationName);
    }

    [Fact]
    public async Task AcceptCreatesSuccessorAndReplacesRuntimeState()
    {
        var store = new MemoryStore();
        var completed = new ConflictCampaignStoreRecord(
            3,
            Checkpoint(
                "accept-root",
                ConflictCampaignOutcome.Ceasefire));

        await store.SaveAsync(
            completed.Checkpoint,
            expectedRevision: null);

        var runtime = Runtime();
        runtime.Replace(completed);

        var service = Service(
            runtime,
            store,
            new[]
            {
                Template(
                    "FICTIONAL-SUCCESSOR-A",
                    36,
                    -101),
                Template(
                    "FICTIONAL-SUCCESSOR-B",
                    40,
                    -108)
            });

        MilitarySuccessorOperationOffer offer =
            Assert.IsType<MilitarySuccessorOperationOffer>(
                service.GetCurrentOffer());

        ConflictCampaignStoreRecord successor =
            await service.AcceptAsync(offer);

        Assert.Same(successor, runtime.Current);
        Assert.Equal(
            offer.CampaignId,
            successor.Checkpoint.CampaignId);
        Assert.Equal(
            offer.TheaterId,
            successor.Checkpoint.World.TheaterId);
        Assert.Equal(
            offer.OperationName,
            successor.Checkpoint.CampaignState.Identity!.OperationName);
        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            successor.Checkpoint.CampaignState.Outcome);
        Assert.Single(successor.Checkpoint.History);
        Assert.Equal(
            completed.Checkpoint.CampaignId,
            successor.Checkpoint.History[0].CampaignId);
        Assert.Null(service.GetCurrentOffer());
    }

    [Fact]
    public async Task StaleOfferIsRejectedAfterRuntimeCampaignChanges()
    {
        var store = new MemoryStore();
        var runtime = Runtime();

        runtime.Replace(
            new ConflictCampaignStoreRecord(
                5,
                Checkpoint(
                    "stale-root",
                    ConflictCampaignOutcome.Defeat)));

        var service = Service(
            runtime,
            store,
            new[]
            {
                Template(
                    "FICTIONAL-SUCCESSOR-A",
                    36,
                    -101)
            });

        MilitarySuccessorOperationOffer offer =
            Assert.IsType<MilitarySuccessorOperationOffer>(
                service.GetCurrentOffer());

        runtime.Replace(
            new ConflictCampaignStoreRecord(
                6,
                Checkpoint(
                    "different-root",
                    ConflictCampaignOutcome.Victory)));

        await Assert.ThrowsAsync<ConflictCampaignConcurrencyException>(
            () => service.AcceptAsync(offer));
    }

    [Fact]
    public void DecliningStaleOfferDoesNotHideCurrentCampaignOffer()
    {
        var store = new MemoryStore();
        var runtime = Runtime();

        runtime.Replace(
            new ConflictCampaignStoreRecord(
                2,
                Checkpoint(
                    "first-root",
                    ConflictCampaignOutcome.Victory)));

        var service = Service(
            runtime,
            store,
            new[]
            {
                Template(
                    "FICTIONAL-SUCCESSOR-A",
                    36,
                    -101)
            });

        MilitarySuccessorOperationOffer first =
            Assert.IsType<MilitarySuccessorOperationOffer>(
                service.GetCurrentOffer());

        runtime.Replace(
            new ConflictCampaignStoreRecord(
                3,
                Checkpoint(
                    "second-root",
                    ConflictCampaignOutcome.Defeat)));

        Assert.False(service.Decline(first));
        Assert.NotNull(service.GetCurrentOffer());
    }

    private static MilitaryCampaignTransitionService Service(
        ConflictCampaignRuntimeState runtime,
        IConflictCampaignStore store,
        IReadOnlyList<ConflictTheaterTemplate> candidates) =>
        new(
            runtime,
            new ConflictCampaignCoordinator(store),
            new StaticCatalog(candidates),
            new FixedTimeProvider(Epoch.AddHours(2)));

    private static ConflictCampaignRuntimeState Runtime() =>
        new(new EmptyRecoverySource());

    private static ConflictCampaignCheckpoint Checkpoint(
        string campaignId,
        ConflictCampaignOutcome outcome)
    {
        ConflictTheaterTemplate template =
            Template(
                $"THEATER-{campaignId}",
                35,
                -97);

        ConflictWorldState world =
            ConflictTheaterGenerator.Generate(
                template,
                theaterSeed: 0xA11CEUL,
                Epoch);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                campaignId,
                world);

        if (outcome != ConflictCampaignOutcome.Ongoing)
        {
            campaign = campaign with
            {
                Outcome = outcome,
                Objectives =
                    Array.Empty<ConflictStrategicObjective>()
            };
        }

        return ConflictCampaignCheckpoint.Create(
            campaignId,
            world,
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.Patrol,
                Trust: 0.70,
                SuccessfulOperations: 6,
                FailedOperations: 1),
            new PlayerCombatState(
                AirframeDamage: 0.05,
                PropulsionDamage: 0.02,
                SystemsDamage: 0.01),
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: Epoch,
            campaign);
    }

    private static ConflictTheaterTemplate Template(
        string theaterId,
        double latitude,
        double longitude) =>
        new(
            theaterId,
            new GeoPoint(latitude, longitude),
            RadiusNauticalMiles: 110,
            FriendlyGroundUnits: 6,
            HostileGroundUnits: 6,
            FriendlyAirUnits: 3,
            HostileAirUnits: 3);

    private sealed class StaticCatalog(
        IReadOnlyList<ConflictTheaterTemplate> candidates)
        : IConflictTheaterCatalog
    {
        public IReadOnlyList<ConflictTheaterTemplate> GetCandidates() =>
            candidates;
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            utcNow;
    }

    private sealed class EmptyRecoverySource
        : IConflictCampaignRecoverySource
    {
        public Task<ConflictCampaignStoreRecord?> LoadMostRecentlySavedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConflictCampaignStoreRecord?>(null);
    }

    private sealed class MemoryStore : IConflictCampaignStore
    {
        private readonly Dictionary<
            string,
            ConflictCampaignStoreRecord> _records =
            new(StringComparer.Ordinal);

        public Task<ConflictCampaignStoreRecord?> LoadAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _records.TryGetValue(
                campaignId,
                out ConflictCampaignStoreRecord? record);

            return Task.FromResult(record);
        }

        public Task<ConflictCampaignStoreRecord> SaveAsync(
            ConflictCampaignCheckpoint checkpoint,
            long? expectedRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkpoint.Validate();

            if (!_records.TryGetValue(
                checkpoint.CampaignId,
                out ConflictCampaignStoreRecord? current))
            {
                if (expectedRevision is not null)
                {
                    throw new ConflictCampaignConcurrencyException(
                        "Missing campaign.");
                }

                var created =
                    new ConflictCampaignStoreRecord(
                        1,
                        checkpoint);

                _records.Add(
                    checkpoint.CampaignId,
                    created);

                return Task.FromResult(created);
            }

            if (expectedRevision != current.Revision)
            {
                throw new ConflictCampaignConcurrencyException(
                    "Stale revision.");
            }

            var updated =
                new ConflictCampaignStoreRecord(
                    checked(current.Revision + 1),
                    checkpoint);

            _records[checkpoint.CampaignId] = updated;
            return Task.FromResult(updated);
        }
    }
}
