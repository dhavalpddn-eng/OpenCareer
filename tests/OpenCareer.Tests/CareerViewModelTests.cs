using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class CareerViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingProfileShowsOnboardingState()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Contains(
            "starting airport",
            viewModel.StatusText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartCareerPersistsProfileAndUpdatesRuntime()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        viewModel.HomeAirportIcao =
            "kdfw";

        Assert.True(viewModel.CanStartCareer);

        await viewModel.StartCareerAsync();

        PlayerCareerProfileStoreRecord current =
            Assert.IsType<PlayerCareerProfileStoreRecord>(
                runtime.Current);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(
            "KDFW",
            current.Profile.Location.HomeAirportIcao);
        Assert.Equal(
            "KDFW",
            current.Profile.Location.CurrentAirportIcao);
        Assert.Equal(
            Now,
            current.Profile.CreatedAt);
        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Contains(
            "Open Jobs",
            viewModel.StatusText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidAirportDoesNotCreateProfile()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        viewModel.HomeAirportIcao =
            "DF1";

        await viewModel.StartCareerAsync();

        Assert.Equal(0, store.SaveCount);
        Assert.Null(runtime.Current);
        Assert.Contains(
            "four-letter airport ICAO",
            viewModel.StatusText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedCareerCreateRemainsRetryableWithoutTechnicalDetails()
    {
        var store = new FakeProfileStore
        {
            NextSaveFailure = new IOException("Sensitive profile persistence failure.")
        };
        var runtime = new PlayerCareerRuntimeState(store);
        CareerViewModel viewModel = ViewModel(store, runtime);

        await viewModel.InitializeAsync();
        viewModel.HomeAirportIcao = "KDFW";

        await viewModel.StartCareerAsync();

        Assert.False(viewModel.HasCareer);
        Assert.True(viewModel.CanStartCareer);
        Assert.Null(runtime.Current);
        Assert.Contains("Retry Start Career", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive profile persistence failure", viewModel.StatusText, StringComparison.Ordinal);

        await viewModel.StartCareerAsync();

        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task FailedPostCreateProjectionKeepsSavedCareerRecoverableWithoutTechnicalDetails()
    {
        var store = new FakeProfileStore();
        var runtime = new PlayerCareerRuntimeState(store);
        var roadmap = new FakeQualificationRoadmapSource(
            new IOException("Sensitive qualification projection failure."));
        var viewModel = new CareerViewModel(
            new PlayerCareerOnboardingCoordinator(store, runtime),
            runtime,
            new FixedTimeProvider(Now),
            roadmap,
            new FakeStudentTrainingAccess(
                new(
                    StudentTrainingOperationReadinessState.NoCareer,
                    "Complete career onboarding before starting Student training.")),
            new FakeStudentTrainingAccess(
                new(
                    StudentTrainingOperationReadinessState.NoCareer,
                    "Complete career onboarding before starting Student training.")));

        await viewModel.InitializeAsync();
        viewModel.HomeAirportIcao = "KDFW";

        await viewModel.StartCareerAsync();

        PlayerCareerProfileStoreRecord current =
            Assert.IsType<PlayerCareerProfileStoreRecord>(runtime.Current);
        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Equal("KDFW", current.Profile.Location.CurrentAirportIcao);
        Assert.Contains("profile is saved", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reopen Career", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive qualification projection failure", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedProfileLoadBlocksOnboardingUntilAuthorityReloadSucceeds()
    {
        var store = new FakeProfileStore
        {
            NextLoadFailure = new IOException("Sensitive profile load failure.")
        };
        var runtime = new PlayerCareerRuntimeState(store);
        CareerViewModel viewModel = ViewModel(store, runtime);
        viewModel.HomeAirportIcao = "KDFW";

        IOException failure =
            await Assert.ThrowsAsync<IOException>(
                () => viewModel.InitializeAsync());
        viewModel.ReportInitializationFailure(failure);

        Assert.False(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.False(viewModel.CanEditHomeAirport);
        Assert.Contains("Reopen Career", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive profile load failure", viewModel.StatusText, StringComparison.Ordinal);

        await viewModel.StartCareerAsync();

        Assert.Equal(0, store.SaveCount);
        Assert.Contains("authority is unavailable", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanStartCareer);
        Assert.True(viewModel.CanEditHomeAirport);
    }

    [Fact]
    public async Task FailedCareerRefreshClearsStaleTrainingStartAndRetainsProfileRecovery()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("b8100000-0000-0000-0000-000000000005"),
                "KAFW",
                Now.AddHours(-1));
        var store = new FakeProfileStore(
            new PlayerCareerProfileStoreRecord(1, existing, Now));
        var runtime = new PlayerCareerRuntimeState(store);
        var training = new FakeStudentTrainingAccess(
            new(
                StudentTrainingOperationReadinessState.Ready,
                "Ready for verified Student training.",
                AirportIcao: "KAFW",
                AircraftDisplayName: "Cessna 172"));
        var viewModel = new CareerViewModel(
            new PlayerCareerOnboardingCoordinator(store, runtime),
            runtime,
            new FixedTimeProvider(Now),
            new CareerQualificationRoadmapSource(runtime),
            training,
            training);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.CanStartStudentTraining);

        training.NextReadFailure = new IOException("Sensitive readiness refresh failure.");
        IOException failure =
            await Assert.ThrowsAsync<IOException>(
                () => viewModel.InitializeAsync());
        viewModel.ReportInitializationFailure(failure);

        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.False(viewModel.CanEditHomeAirport);
        Assert.False(viewModel.CanStartStudentTraining);
        Assert.Contains("CURRENT KAFW", viewModel.LocationText, StringComparison.Ordinal);
        Assert.Contains("profile at KAFW is active", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reopen Career", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive readiness refresh failure", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive readiness refresh failure", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);

        await viewModel.StartStudentTrainingAsync();

        Assert.Equal(0, training.StartCount);
    }

    [Fact]
    public async Task ExistingCareerCannotBeCreatedAgain()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("35ec9ffb-df03-4130-90df-e93cc82c8264"),
                "KAFW",
                Now.AddHours(-1));

        var store =
            new FakeProfileStore(
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        2,
                    existing,
                    SavedAt:
                        Now.AddMinutes(-30)));

        var runtime =
            new PlayerCareerRuntimeState(
                store);

        var viewModel =
            ViewModel(
                store,
                runtime);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCareer);
        Assert.False(viewModel.CanStartCareer);
        Assert.Equal(
            "KAFW",
            viewModel.HomeAirportIcao);
        Assert.Contains(
            "CURRENT KAFW",
            viewModel.LocationText,
            StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ExistingCareerProjectsTruthfulQualificationRoadmapWithoutWrites()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("f6dad84c-5ad1-4718-bad9-134a318ebfb4"),
                "KAFW",
                Now.AddHours(-1)) with
            {
                Qualifications =
                    new PilotQualificationState(
                        PilotLicenseLevel.Private,
                        System.Collections.Immutable.ImmutableHashSet.Create(
                            PilotRating.AirplaneSingleEngineLand))
            };
        var store =
            new FakeProfileStore(
                new PlayerCareerProfileStoreRecord(
                    3,
                    existing,
                    Now.AddMinutes(-30)));
        var runtime = new PlayerCareerRuntimeState(store);
        CareerViewModel viewModel = ViewModel(store, runtime);

        await viewModel.InitializeAsync();

        Assert.Equal("Private pilot", viewModel.CurrentLicenseText);
        Assert.Equal("Airplane single-engine land", viewModel.EarnedRatingsText);
        Assert.Contains("Commercial pilot", viewModel.NextQualificationsText, StringComparison.Ordinal);
        Assert.Contains("authoritative qualification-specific", viewModel.QualificationPolicyText, StringComparison.Ordinal);
        Assert.Equal(7, viewModel.QualificationRoadmapItems.Count);
        Assert.Equal("EARNED", viewModel.QualificationRoadmapItems[1].Status);
        Assert.Equal("AVAILABLE", viewModel.QualificationRoadmapItems[2].Status);
        Assert.Equal("LOCKED", viewModel.QualificationRoadmapItems[3].Status);
        Assert.Contains("unmet", viewModel.QualificationRoadmapItems[3].Prerequisites, StringComparison.Ordinal);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ReadyStudentTrainingProjectsBriefingAndStartsOnlyThroughAction()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("b8100000-0000-0000-0000-000000000001"),
                "KAFW",
                Now.AddHours(-1));
        var store = new FakeProfileStore(
            new PlayerCareerProfileStoreRecord(1, existing, Now));
        var runtime = new PlayerCareerRuntimeState(store);
        var training = new FakeStudentTrainingAccess(
            new(
                StudentTrainingOperationReadinessState.Ready,
                "Ready for verified Student training.",
                AirportIcao: "KAFW",
                AircraftDisplayName: "Cessna 172"));
        var viewModel = new CareerViewModel(
            new PlayerCareerOnboardingCoordinator(store, runtime),
            runtime,
            new FixedTimeProvider(Now),
            new CareerQualificationRoadmapSource(runtime),
            training,
            training);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanStartStudentTraining);
        Assert.Equal("KAFW → KAFW · LOCAL CIRCUIT", viewModel.StudentTrainingRouteText);
        Assert.Contains("Cessna 172", viewModel.StudentTrainingAircraftText, StringComparison.Ordinal);

        await viewModel.StartStudentTrainingAsync();

        Assert.Equal(1, training.StartCount);
        Assert.False(viewModel.CanStartStudentTraining);
        Assert.Contains("active", viewModel.StudentTrainingStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BlockedStudentTrainingCannotInvokeStartAction()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("b8100000-0000-0000-0000-000000000002"),
                "KAFW",
                Now.AddHours(-1));
        var store = new FakeProfileStore(
            new PlayerCareerProfileStoreRecord(1, existing, Now));
        var runtime = new PlayerCareerRuntimeState(store);
        var training = new FakeStudentTrainingAccess(
            new(
                StudentTrainingOperationReadinessState.AircraftIdentityUnavailable,
                "Load an aircraft before training.",
                AirportIcao: "KAFW"));
        var viewModel = new CareerViewModel(
            new PlayerCareerOnboardingCoordinator(store, runtime),
            runtime,
            new FixedTimeProvider(Now),
            new CareerQualificationRoadmapSource(runtime),
            training,
            training);

        await viewModel.InitializeAsync();
        await viewModel.StartStudentTrainingAsync();

        Assert.False(viewModel.CanStartStudentTraining);
        Assert.Equal(0, training.StartCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task FailedStudentTrainingStartReprojectsCreatedSessionWithoutTechnicalDetails()
    {
        (CareerViewModel viewModel, FakeStudentTrainingAccess training) =
            await ReadyStudentTrainingViewModelAsync();
        training.BecomeActiveBeforeStartFailure = true;
        training.NextStartFailure = new IOException("Sensitive training persistence failure.");

        await viewModel.StartStudentTrainingAsync();

        Assert.Equal(1, training.StartCount);
        Assert.False(viewModel.CanStartStudentTraining);
        Assert.Contains("authoritative training flight is active", viewModel.StudentTrainingStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open Current Flight", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive training persistence failure", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedStudentTrainingStartRemainsRetryableWhenAuthorityIsReady()
    {
        (CareerViewModel viewModel, FakeStudentTrainingAccess training) =
            await ReadyStudentTrainingViewModelAsync();
        training.NextStartFailure = new IOException("Sensitive training start failure.");

        await viewModel.StartStudentTrainingAsync();

        Assert.True(viewModel.CanStartStudentTraining);
        Assert.Contains("retry Start Student Training", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StudentTrainingStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive training start failure", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);

        await viewModel.StartStudentTrainingAsync();
        Assert.Equal(2, training.StartCount);
        Assert.False(viewModel.CanStartStudentTraining);
    }

    [Fact]
    public async Task FailedStudentTrainingStartAndReadinessRefreshFailClosed()
    {
        (CareerViewModel viewModel, FakeStudentTrainingAccess training) =
            await ReadyStudentTrainingViewModelAsync();
        training.NextStartFailure = new IOException("Sensitive training start failure.");
        training.NextReadFailure = new IOException("Sensitive training readiness failure.");

        await viewModel.StartStudentTrainingAsync();

        Assert.Equal(1, training.StartCount);
        Assert.False(viewModel.CanStartStudentTraining);
        Assert.Contains("Reopen Career", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StudentTrainingStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sensitive training start failure", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive training readiness failure", viewModel.StudentTrainingStatusText, StringComparison.Ordinal);
    }

    private static async Task<(CareerViewModel ViewModel, FakeStudentTrainingAccess Training)>
        ReadyStudentTrainingViewModelAsync()
    {
        PlayerCareerProfile existing =
            PlayerCareerProfile.Start(
                Guid.Parse("b8100000-0000-0000-0000-000000000004"),
                "KAFW",
                Now.AddHours(-1));
        var store = new FakeProfileStore(
            new PlayerCareerProfileStoreRecord(1, existing, Now));
        var runtime = new PlayerCareerRuntimeState(store);
        var training = new FakeStudentTrainingAccess(
            new(
                StudentTrainingOperationReadinessState.Ready,
                "Ready for verified Student training.",
                AirportIcao: "KAFW",
                AircraftDisplayName: "Cessna 172"));
        var viewModel = new CareerViewModel(
            new PlayerCareerOnboardingCoordinator(store, runtime),
            runtime,
            new FixedTimeProvider(Now),
            new CareerQualificationRoadmapSource(runtime),
            training,
            training);

        await viewModel.InitializeAsync();
        return (viewModel, training);
    }

    private static CareerViewModel ViewModel(
        FakeProfileStore store,
        PlayerCareerRuntimeState runtime) =>
        new(
            new PlayerCareerOnboardingCoordinator(
                store,
                runtime),
            runtime,
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }

    private sealed class FakeQualificationRoadmapSource(
        Exception failure) : ICareerQualificationRoadmapSource
    {
        public Task<CareerQualificationRoadmap?> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<CareerQualificationRoadmap?>(failure);
        }
    }

    private sealed class FakeStudentTrainingAccess(
        StudentTrainingOperationReadiness readiness)
        : IStudentTrainingOperationReadinessSource,
          IStudentTrainingOperationStartAction
    {
        private StudentTrainingOperationReadiness _readiness = readiness;

        public int StartCount { get; private set; }
        public bool BecomeActiveBeforeStartFailure { get; set; }
        public Exception? NextStartFailure { get; set; }
        public Exception? NextReadFailure { get; set; }

        public Task<StudentTrainingOperationReadiness> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextReadFailure is { } failure)
            {
                NextReadFailure = null;
                return Task.FromException<StudentTrainingOperationReadiness>(failure);
            }

            return Task.FromResult(_readiness);
        }

        public Task<StudentTrainingOperationStartResult> StartAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_readiness.CanStart)
                throw new InvalidOperationException(_readiness.Detail);

            StartCount++;
            Guid operationId =
                Guid.Parse("b8100000-0000-0000-0000-000000000003");
            var plan = new FlightSessionPlan(
                _readiness.AirportIcao,
                _readiness.AirportIcao,
                SourceProvider: StudentTrainingOperationService.SourceProvider,
                ExpectedCanonicalAircraftId: "msfs-title:c172");
            FlightSession session = FlightSession.Start(
                Now,
                sessionId: operationId,
                plan: plan);
            if (BecomeActiveBeforeStartFailure)
            {
                ApplyActiveReadiness(operationId);
            }

            if (NextStartFailure is { } failure)
            {
                NextStartFailure = null;
                return Task.FromException<StudentTrainingOperationStartResult>(failure);
            }

            ApplyActiveReadiness(operationId);

            return Task.FromResult(
                new StudentTrainingOperationStartResult(
                    StudentTrainingOperationService.CreateOperationIdentity(operationId),
                    session,
                    _readiness.AircraftDisplayName ?? "Cessna 172"));
        }

        private void ApplyActiveReadiness(Guid operationId) =>
            _readiness = new(
                StudentTrainingOperationReadinessState.Active,
                "Student training is active.",
                operationId,
                _readiness.AirportIcao,
                _readiness.AircraftDisplayName);
    }

    private sealed class FakeProfileStore
        : IPlayerCareerProfileStore
    {
        private PlayerCareerProfileStoreRecord? _record;

        public FakeProfileStore(
            PlayerCareerProfileStoreRecord? record = null)
        {
            _record = record;
        }

        public int SaveCount { get; private set; }
        public Exception? NextLoadFailure { get; set; }
        public Exception? NextSaveFailure { get; set; }

        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NextLoadFailure is { } failure)
            {
                NextLoadFailure = null;
                return Task.FromException<PlayerCareerProfileStoreRecord?>(failure);
            }

            return Task.FromResult(_record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NextSaveFailure is { } failure)
            {
                NextSaveFailure = null;
                return Task.FromException<PlayerCareerProfileStoreRecord>(failure);
            }

            if (_record is not null
                || expectedRevision is not null)
            {
                throw new PlayerCareerProfileConcurrencyException(
                    "Player career profile already exists.");
            }

            SaveCount++;

            var saved =
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        1,
                    profile,
                    savedAt);

            saved.Validate();
            _record = saved;

            return Task.FromResult(saved);
        }
    }
}
