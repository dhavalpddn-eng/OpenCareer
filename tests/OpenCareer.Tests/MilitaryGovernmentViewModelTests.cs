using OpenCareer.App.ViewModels;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryGovernmentViewModelTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RefreshWithoutCampaignShowsSafeEmptyState()
    {
        ConflictCampaignRuntimeState runtime = CreateRuntime();
        MilitaryGovernmentViewModel viewModel =
            CreateViewModel(runtime);

        viewModel.Refresh();

        Assert.False(viewModel.HasCampaign);
        Assert.False(viewModel.HasSuccessorOffer);
        Assert.Equal("No active operation", viewModel.OperationName);
        Assert.Empty(viewModel.SupportRequests);
        Assert.Empty(viewModel.Objectives);
        Assert.Contains(
            "No military campaign is active",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshProjectsPersistedCampaignForUi()
    {
        ConflictCampaignRuntimeState runtime = CreateRuntime();
        ConflictCampaignCheckpoint checkpoint =
            CreateCheckpoint("ui-campaign");

        runtime.Replace(
            new ConflictCampaignStoreRecord(
                1,
                checkpoint));

        MilitaryGovernmentViewModel viewModel =
            CreateViewModel(runtime);

        viewModel.Refresh();

        ConflictCampaignIdentity identity =
            checkpoint.CampaignState.Identity!;

        Assert.True(viewModel.HasCampaign);
        Assert.False(viewModel.HasSuccessorOffer);
        Assert.Contains(
            "Ongoing",
            viewModel.CampaignStateText,
            StringComparison.Ordinal);
        Assert.Equal(
            identity.OperationName,
            viewModel.OperationName);
        Assert.Contains(
            identity.FriendlyFaction.DisplayName,
            viewModel.FriendlyFactionText,
            StringComparison.Ordinal);
        Assert.Contains(
            MilitaryGovernmentViewModel.FormatWords(
                identity.FriendlyFaction.Posture.ToString()),
            viewModel.FriendlyPostureText,
            StringComparison.Ordinal);
        Assert.Contains(
            identity.HostileFaction.DisplayName,
            viewModel.HostileFactionText,
            StringComparison.Ordinal);
        Assert.Contains(
            "replacement reserve",
            viewModel.FriendlyReserveText,
            StringComparison.Ordinal);
        Assert.Equal(
            checkpoint.CampaignState.Objectives.Length,
            viewModel.Objectives.Count);
        Assert.Contains(
            "OpenCareer remains authoritative",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FormatWordsSeparatesEnumStyleNames()
    {
        Assert.Equal(
            "Logistics Focused",
            MilitaryGovernmentViewModel.FormatWords(
                "LogisticsFocused"));
        Assert.Equal(
            "Close Air Support",
            MilitaryGovernmentViewModel.FormatWords(
                "CloseAirSupport"));
    }

    private static MilitaryGovernmentViewModel CreateViewModel(
        ConflictCampaignRuntimeState runtime)
    {
        var store = new MemoryStore();

        return new MilitaryGovernmentViewModel(
            runtime,
            new MilitaryCampaignTransitionService(
                runtime,
                new ConflictCampaignCoordinator(store),
                new StaticCatalog(
                    new[]
                    {
                        new ConflictTheaterTemplate(
                            "FICTIONAL-NEXT",
                            new GeoPoint(36, -101),
                            RadiusNauticalMiles: 100,
                            FriendlyGroundUnits: 5,
                            HostileGroundUnits: 5,
                            FriendlyAirUnits: 2,
                            HostileAirUnits: 2)
                    }),
                new FixedTimeProvider(Epoch.AddHours(2))));
    }

    private static ConflictCampaignRuntimeState CreateRuntime() =>
        new(new EmptyRecoverySource());

    private static ConflictCampaignCheckpoint CreateCheckpoint(
        string campaignId)
    {
        var template = new ConflictTheaterTemplate(
            "FICTIONAL-UI",
            new GeoPoint(35, -97),
            RadiusNauticalMiles: 100,
            FriendlyGroundUnits: 6,
            HostileGroundUnits: 6,
            FriendlyAirUnits: 3,
            HostileAirUnits: 3);

        ConflictWorldState world =
            ConflictTheaterGenerator.Generate(
                template,
                theaterSeed: 0xC0FFEEUL,
                Epoch);

        return ConflictCampaignCheckpoint.Create(
            campaignId,
            world,
            MilitaryCareerState.Civilian,
            PlayerCombatState.Undamaged,
            combatSupportMissions: null,
            areaSupportMissions: null,
            airOperationMissions: null,
            savedAt: Epoch);
    }

    private sealed class EmptyRecoverySource
        : IConflictCampaignRecoverySource
    {
        public Task<ConflictCampaignStoreRecord?> LoadMostRecentlySavedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConflictCampaignStoreRecord?>(null);
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticCatalog(
        IReadOnlyList<ConflictTheaterTemplate> candidates)
        : IConflictTheaterCatalog
    {
        public IReadOnlyList<ConflictTheaterTemplate> GetCandidates() =>
            candidates;
    }

    private sealed class MemoryStore : IConflictCampaignStore
    {
        public Task<ConflictCampaignStoreRecord?> LoadAsync(
            string campaignId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConflictCampaignStoreRecord?>(null);

        public Task<ConflictCampaignStoreRecord> SaveAsync(
            ConflictCampaignCheckpoint checkpoint,
            long? expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ConflictCampaignStoreRecord(
                    expectedRevision is long revision
                        ? checked(revision + 1)
                        : 1,
                    checkpoint));
    }
}
