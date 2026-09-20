using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

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
        Assert.Empty(viewModel.CompletedOperations);
        Assert.Contains("No archived operations", viewModel.CompletedOperationStatusText);
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

    [Fact]
    public void HistoryUsesArchivedValuesAndStableNewestFirstOrderWithoutMutatingCampaign()
    {
        var runtime = CreateRuntime();
        var store = new MemoryStore();
        var archived = ConflictCampaignHistoryEntry.FromCheckpoint(CompletedRecord().Checkpoint);
        var older = archived with
        {
            CampaignId = "older",
            Identity = archived.Identity with { OperationId = "operation:older" },
            EndedAt = Epoch.AddHours(-2)
        };
        var tiedZ = archived with
        {
            CampaignId = "z-tied",
            Identity = archived.Identity with { OperationId = "operation:z-tied" },
            EndedAt = Epoch.AddHours(-1)
        };
        var tiedA = archived with
        {
            CampaignId = "a-tied",
            Identity = archived.Identity with { OperationId = "operation:a-tied" },
            Outcome = ConflictCampaignOutcome.Ceasefire,
            FinalPhase = ConflictCampaignPhase.HostilePressure,
            FinalFriendlyControlAverage = 0.25,
            EndedAt = Epoch.AddHours(-1)
        };
        ConflictCampaignHistoryEntry[] history = [older, tiedZ, tiedA];
        var checkpoint = CreateCheckpoint("current-campaign") with { History = history };
        var record = new ConflictCampaignStoreRecord(1, checkpoint);
        runtime.Replace(record);
        var viewModel = CreateViewModel(runtime, store);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        viewModel.Refresh();
        viewModel.Refresh();

        Assert.Equal(new[] { "a-tied", "z-tied", "older" },
            viewModel.CompletedOperations.Select(item => item.CampaignId));
        var first = viewModel.CompletedOperations[0];
        Assert.Equal(tiedA.Identity.OperationName, first.OperationName);
        Assert.Equal($"Theater {tiedA.TheaterId}", first.TheaterText);
        Assert.Equal("Ceasefire", first.OutcomeText);
        Assert.Equal($"Final phase: Hostile Pressure • {0.25:P0} friendly control", first.FinalStateText);
        Assert.Equal($"Ended {tiedA.EndedAt.LocalDateTime:g}", first.EndedText);
        Assert.Contains(tiedA.Identity.FriendlyFaction.DisplayName, first.FriendlyFactionText);
        Assert.Contains(tiedA.Identity.HostileFaction.DisplayName, first.HostileFactionText);
        Assert.Contains(MilitaryGovernmentViewModel.FormatWords(tiedA.Identity.FriendlyFaction.Posture.ToString()), first.FriendlyFactionText);
        Assert.Contains(MilitaryGovernmentViewModel.FormatWords(tiedA.Identity.HostileFaction.Posture.ToString()), first.HostileFactionText);
        Assert.Contains("3 archived operation(s)", viewModel.CompletedOperationStatusText);
        Assert.Contains(nameof(viewModel.CompletedOperations), notifications);
        Assert.Contains(nameof(viewModel.CompletedOperationStatusText), notifications);
        Assert.Same(record, runtime.Current);
        Assert.Equal(new[] { "older", "z-tied", "a-tied" }, history.Select(entry => entry.CampaignId));
        Assert.Equal(0, store.SaveAttempts);
    }

    [Fact]
    public void SwitchingToCampaignWithoutHistoryClearsArchivedRows()
    {
        var runtime = CreateRuntime();
        var archive = ConflictCampaignHistoryEntry.FromCheckpoint(CompletedRecord().Checkpoint);
        runtime.Replace(new ConflictCampaignStoreRecord(1,
            CreateCheckpoint("successor") with { History = [archive] }));
        var viewModel = CreateViewModel(runtime);
        viewModel.Refresh();
        Assert.Single(viewModel.CompletedOperations);

        runtime.Replace(CompletedRecord());
        viewModel.Refresh();

        // A terminal current campaign is not an archived predecessor yet.
        Assert.Empty(viewModel.CompletedOperations);
        Assert.Contains("No archived operations", viewModel.CompletedOperationStatusText);
        Assert.True(viewModel.HasSuccessorOffer);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task SaveFailureRemainsVisibleAndCanBeRetried(string failure)
    {
        var runtime = CreateRuntime();
        var completed = CompletedRecord();
        runtime.Replace(completed);
        var store = new MemoryStore
        {
            BeforeSave = _ => Task.FromException(failure switch
            {
                "sqlite" => new SqliteException("private database detail", 5),
                "io" => new IOException("private path"),
                _ => new UnauthorizedAccessException("private path")
            })
        };
        var viewModel = CreateViewModel(runtime, store);
        viewModel.Refresh();
        string offeredName = viewModel.SuccessorOperationName;

        await viewModel.AcceptSuccessorAsync();
        string failureMessage = viewModel.StatusMessage;
        viewModel.Refresh(); // The production page polls every two seconds.

        Assert.Same(completed, runtime.Current);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanAcceptSuccessor);
        Assert.Equal(failureMessage, viewModel.StatusMessage);
        Assert.Contains("could not be saved", failureMessage);
        Assert.DoesNotContain("private", failureMessage);
        Assert.Equal(offeredName, viewModel.SuccessorOperationName);
        Assert.Empty(store.Saved);

        store.BeforeSave = null;
        await viewModel.AcceptSuccessorAsync();
        viewModel.Refresh();

        Assert.Equal(offeredName, viewModel.OperationName);
        Assert.Contains("accepted and activated", viewModel.StatusMessage);
        Assert.False(viewModel.CanAcceptSuccessor);
        Assert.Single(store.Saved);
        Assert.Single(runtime.Current!.Checkpoint.History);
    }

    [Fact]
    public async Task PendingAcceptanceBlocksRepeatedActionsAndSavesOnce()
    {
        var runtime = CreateRuntime();
        runtime.Replace(CompletedRecord());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore { BeforeSave = _ => release.Task };
        var viewModel = CreateViewModel(runtime, store);
        viewModel.Refresh();

        Task acceptance = viewModel.AcceptSuccessorAsync();
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanAcceptSuccessor);
        Assert.False(viewModel.CanDeclineSuccessor);
        Assert.False(viewModel.CanReconsiderSuccessor);
        viewModel.Refresh();
        viewModel.DeclineSuccessor();
        viewModel.ReconsiderSuccessor();
        await viewModel.AcceptSuccessorAsync();
        Assert.Equal(1, store.SaveAttempts);

        release.SetResult();
        await acceptance;
        await viewModel.AcceptSuccessorAsync();

        Assert.False(viewModel.IsBusy);
        Assert.Equal(1, store.SaveAttempts);
        Assert.Single(runtime.Current!.Checkpoint.History);
    }

    [Fact]
    public async Task CancelledSavePreservesCampaignAndAllowsRetry()
    {
        var runtime = CreateRuntime();
        var completed = CompletedRecord();
        runtime.Replace(completed);
        var store = new MemoryStore
        {
            BeforeSave = token => Task.Delay(Timeout.Infinite, token)
        };
        var viewModel = CreateViewModel(runtime, store);
        viewModel.Refresh();
        using var cancellation = new CancellationTokenSource();

        Task acceptance = viewModel.AcceptSuccessorAsync(cancellation.Token);
        cancellation.Cancel();
        await acceptance;
        viewModel.Refresh();

        Assert.Same(completed, runtime.Current);
        Assert.Empty(store.Saved);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanAcceptSuccessor);
        Assert.Contains("cancelled", viewModel.StatusMessage);
        store.BeforeSave = null;
        await viewModel.AcceptSuccessorAsync();
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task StaleAcceptanceRefreshesEntireCampaignWithoutSaving()
    {
        var runtime = CreateRuntime();
        runtime.Replace(CompletedRecord());
        var store = new MemoryStore();
        var viewModel = CreateViewModel(runtime, store);
        viewModel.Refresh();
        var active = new ConflictCampaignStoreRecord(1, CreateCheckpoint("new-active"));
        runtime.Replace(active);

        await viewModel.AcceptSuccessorAsync();

        Assert.Same(active, runtime.Current);
        Assert.Equal(active.Checkpoint.CampaignState.Identity!.OperationName, viewModel.OperationName);
        Assert.Contains("Ongoing", viewModel.CampaignStateText);
        Assert.False(viewModel.HasSuccessorOffer);
        Assert.False(viewModel.CanAcceptSuccessor);
        Assert.Contains("could not be accepted", viewModel.StatusMessage);
        Assert.Equal(0, store.SaveAttempts);

        runtime.Replace(new ConflictCampaignStoreRecord(2, active.Checkpoint));
        viewModel.Refresh();
        Assert.Contains("Campaign state is live", viewModel.StatusMessage);
    }

    [Fact]
    public void DeclineAndReconsiderPreserveOfferWithoutSaving()
    {
        var runtime = CreateRuntime();
        var completed = CompletedRecord();
        runtime.Replace(completed);
        var store = new MemoryStore();
        var viewModel = CreateViewModel(runtime, store);
        viewModel.Refresh();
        string offeredName = viewModel.SuccessorOperationName;

        viewModel.DeclineSuccessor();
        viewModel.Refresh();
        Assert.True(viewModel.CanReconsiderSuccessor);
        Assert.False(viewModel.CanAcceptSuccessor);
        Assert.Contains("deferred", viewModel.StatusMessage);

        viewModel.ReconsiderSuccessor();
        Assert.Equal(offeredName, viewModel.SuccessorOperationName);
        Assert.True(viewModel.CanAcceptSuccessor);
        Assert.False(viewModel.CanReconsiderSuccessor);
        Assert.Same(completed, runtime.Current);
        Assert.Equal(0, store.SaveAttempts);
    }

    [Fact]
    public async Task AcceptedSuccessorRecoversFromSqliteWithoutReplayingTransition()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"military-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var options = new OpenCareerDatabaseOptions(Path.Combine(directory, "campaign.db"));
        try
        {
            var store = new SqliteConflictCampaignStore(options, NullLogger<SqliteConflictCampaignStore>.Instance);
            var completed = await store.SaveAsync(CompletedRecord().Checkpoint, null);
            var runtime = CreateRuntime();
            runtime.Replace(completed);
            var viewModel = CreateViewModel(runtime, store);
            viewModel.Refresh();
            string offeredName = viewModel.SuccessorOperationName;
            await viewModel.AcceptSuccessorAsync();

            var displayedArchive = Assert.Single(viewModel.CompletedOperations);
            Assert.Equal(completed.Checkpoint.CampaignId, displayedArchive.CampaignId);

            var recoveredStore = new SqliteConflictCampaignStore(options, NullLogger<SqliteConflictCampaignStore>.Instance);
            var recoveredRuntime = new ConflictCampaignRuntimeState(recoveredStore);
            await recoveredRuntime.InitializeAsync();
            var recoveredViewModel = CreateViewModel(recoveredRuntime, recoveredStore);
            recoveredViewModel.Refresh();
            await recoveredViewModel.AcceptSuccessorAsync();

            Assert.Equal(offeredName, recoveredViewModel.OperationName);
            Assert.False(recoveredViewModel.HasSuccessorOffer);
            Assert.Equal(runtime.Current!.Checkpoint.CampaignId, recoveredRuntime.Current!.Checkpoint.CampaignId);
            Assert.Equal(1, recoveredRuntime.Current.Revision);
            Assert.Single(recoveredRuntime.Current.Checkpoint.History);
            Assert.Equal(completed.Checkpoint.MilitaryCareer, recoveredRuntime.Current.Checkpoint.MilitaryCareer);
            Assert.Equal(completed.Checkpoint.PlayerCombatState, recoveredRuntime.Current.Checkpoint.PlayerCombatState);
            var recoveredArchive = Assert.Single(recoveredViewModel.CompletedOperations);
            Assert.Equal(displayedArchive.CampaignId, recoveredArchive.CampaignId);
            Assert.Equal(displayedArchive.OperationName, recoveredArchive.OperationName);
            Assert.Equal(displayedArchive.FriendlyFactionText, recoveredArchive.FriendlyFactionText);
            Assert.Equal(displayedArchive.HostileFactionText, recoveredArchive.HostileFactionText);
            Assert.Equal(displayedArchive.OutcomeText, recoveredArchive.OutcomeText);
            Assert.Equal(displayedArchive.FinalStateText, recoveredArchive.FinalStateText);
            Assert.Equal(displayedArchive.EndedText, recoveredArchive.EndedText);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ConflictCampaignStoreRecord CompletedRecord()
    {
        var checkpoint = CreateCheckpoint("completed-ui");
        return new ConflictCampaignStoreRecord(1, checkpoint with
        {
            CampaignState = checkpoint.CampaignState with
            {
                Outcome = ConflictCampaignOutcome.Victory,
                Objectives = Array.Empty<ConflictStrategicObjective>()
            }
        });
    }

    private static MilitaryGovernmentViewModel CreateViewModel(
        ConflictCampaignRuntimeState runtime,
        IConflictCampaignStore? store = null)
    {
        store ??= new MemoryStore();

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
        public Func<CancellationToken, Task>? BeforeSave { get; set; }
        public int SaveAttempts { get; private set; }
        public List<ConflictCampaignCheckpoint> Saved { get; } = new();

        public Task<ConflictCampaignStoreRecord?> LoadAsync(
            string campaignId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConflictCampaignStoreRecord?>(null);

        public async Task<ConflictCampaignStoreRecord> SaveAsync(
            ConflictCampaignCheckpoint checkpoint,
            long? expectedRevision,
            CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (BeforeSave is not null)
                await BeforeSave(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            checkpoint.Validate();
            Saved.Add(checkpoint);
            return new ConflictCampaignStoreRecord(
                    expectedRevision is long revision
                        ? checked(revision + 1)
                        : 1,
                    checkpoint);
        }
    }
}
