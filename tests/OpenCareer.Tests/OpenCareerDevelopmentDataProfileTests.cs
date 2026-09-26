using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Aircraft;
using OpenCareer.Infrastructure.Flights;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class OpenCareerDevelopmentDataProfileTests
{
    [Fact]
    public void DefaultLaunchUsesOnlyNormalDataRoot()
    {
        using var root = new TemporaryDirectory();

        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve([], root.Path);

        Assert.Equal(OpenCareerDataProfile.Normal, profile.Paths.Profile);
        Assert.False(profile.ResetRequested);
        Assert.Equal(
            System.IO.Path.Combine(root.Path, "OpenCareer", "opencareer.db"),
            profile.Paths.DatabaseFile);
        Assert.DoesNotContain("LiveTests", profile.Paths.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KJFKProfileUsesFixedSiblingRootAndNeverNormalDatabase()
    {
        using var root = new TemporaryDirectory();

        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);

        Assert.Equal(OpenCareerDataProfile.KjfkLiveTest, profile.Paths.Profile);
        Assert.True(profile.Paths.IsDevelopmentLiveTest);
        Assert.Equal(
            System.IO.Path.Combine(root.Path, "OpenCareer.LiveTests", "KJFK", "opencareer.db"),
            profile.Paths.DatabaseFile);
        Assert.NotEqual(
            System.IO.Path.Combine(root.Path, "OpenCareer", "opencareer.db"),
            profile.Paths.DatabaseFile);
    }

    [Fact]
    public async Task CleanResetRemovesAllStaleTestStateAndPreservesNormalData()
    {
        using var root = new TemporaryDirectory();
        var normal = new OpenCareerDataPaths(System.IO.Path.Combine(root.Path, "OpenCareer"));
        normal.EnsureDirectories();
        await File.WriteAllTextAsync(normal.SettingsFile, "normal-settings");
        await CreateStateDatabaseAsync(normal.DatabaseFile, "normal-contract");

        OpenCareerDataProfileSelection retained =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        retained.Prepare();
        await File.WriteAllTextAsync(retained.Paths.SettingsFile, "test-settings");
        await File.WriteAllTextAsync(retained.Paths.TutorialPreferencesFile, "test-tutorial");
        await File.WriteAllTextAsync(retained.Paths.PendingDatabaseRestoreFile, "stale-restore");
        await File.WriteAllTextAsync(retained.Paths.LogFile, "stale-log");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(retained.Paths.BackupsFolder, "stale.zip"),
            "stale-backup");
        await CreateStateDatabaseAsync(retained.Paths.DatabaseFile, "stale-contract");

        OpenCareerDataProfileSelection clean =
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path);
        clean.Prepare();

        Assert.True(Directory.Exists(clean.Paths.Root));
        Assert.True(File.Exists(clean.Paths.ProfileMarkerFile));
        Assert.False(File.Exists(clean.Paths.DatabaseFile));
        Assert.False(File.Exists(clean.Paths.SettingsFile));
        Assert.False(File.Exists(clean.Paths.TutorialPreferencesFile));
        Assert.False(File.Exists(clean.Paths.PendingDatabaseRestoreFile));
        Assert.False(File.Exists(clean.Paths.LogFile));
        Assert.Empty(Directory.EnumerateFiles(clean.Paths.BackupsFolder));
        Assert.Equal("normal-settings", await File.ReadAllTextAsync(normal.SettingsFile));
        Assert.Equal("normal-contract", await ReadStateAsync(normal.DatabaseFile));
    }

    [Fact]
    public async Task LaunchWithoutResetPreservesRestartRecoveryState()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection first =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        first.Prepare();
        await CreateStateDatabaseAsync(first.Paths.DatabaseFile, "recover-me");

        OpenCareerDataProfileSelection restarted =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        restarted.Prepare();

        Assert.Equal(first.Paths.Root, restarted.Paths.Root);
        Assert.Equal("recover-me", await ReadStateAsync(restarted.Paths.DatabaseFile));
    }

    [Fact]
    public async Task ProductionRecoveryStateIsRetainedUntilAnExplicitCleanReset()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection first =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        first.Prepare();

        Guid contractId = Guid.Parse("b6100000-0000-0000-0000-000000000001");
        Guid sessionId = Guid.Parse("b6100000-0000-0000-0000-000000000002");
        const string aircraftId = "msfs-title:Cessna 172 Skyhawk";
        const string reservationId = "contract:b6100000-0000-0000-0000-000000000001:aircraft-v1";
        DateTimeOffset now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        await SeedProductionRecoveryStateAsync(
            first.Paths.DatabaseFile,
            contractId,
            sessionId,
            aircraftId,
            reservationId,
            now);

        OpenCareerDataProfileSelection recoveryLaunch =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        recoveryLaunch.Prepare();

        await AssertProductionRecoveryStateAsync(
            recoveryLaunch.Paths.DatabaseFile,
            contractId,
            sessionId,
            aircraftId,
            reservationId,
            expectedPresent: true);

        // A reset launch is a new application process; release this test
        // process's SQLite pools to model that process boundary faithfully.
        SqliteConnection.ClearAllPools();

        OpenCareerDataProfileSelection cleanLaunch =
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path);
        cleanLaunch.Prepare();

        Assert.False(File.Exists(cleanLaunch.Paths.DatabaseFile));
        await AssertProductionRecoveryStateAsync(
            cleanLaunch.Paths.DatabaseFile,
            contractId,
            sessionId,
            aircraftId,
            reservationId,
            expectedPresent: false);
    }

    [Fact]
    public void ResetWithoutExactDevelopmentProfileFailsClosed()
    {
        using var root = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ResetArgument],
                root.Path));

        Assert.Throws<ArgumentException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument + "=normal"],
                root.Path));
    }

    [Fact]
    public void MissingOrCorruptDevelopmentMarkerRefusesReset()
    {
        using var root = new TemporaryDirectory();
        OpenCareerDataProfileSelection profile =
            OpenCareerDataProfileSelection.Resolve(
                [OpenCareerDataProfileSelection.ProfileArgument],
                root.Path);
        Directory.CreateDirectory(profile.Paths.Root);
        File.WriteAllText(profile.Paths.DatabaseFile, "do-not-delete-without-marker");

        Assert.Throws<InvalidDataException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path).Prepare());
        Assert.True(File.Exists(profile.Paths.DatabaseFile));

        File.WriteAllText(profile.Paths.ProfileMarkerFile, "not-an-opencareer-profile");
        Assert.Throws<InvalidDataException>(() =>
            OpenCareerDataProfileSelection.Resolve(
                [
                    OpenCareerDataProfileSelection.ProfileArgument,
                    OpenCareerDataProfileSelection.ResetArgument
                ],
                root.Path).Prepare());
        Assert.True(File.Exists(profile.Paths.DatabaseFile));
    }

    [Fact]
    public void LiveTestScriptDefaultsToCleanProfileAndSupportsRecoveryMode()
    {
        string script = File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "UiContracts", "run-kjfk-live-test.ps1"));

        Assert.Contains("--development-kjfk-live-test", script, StringComparison.Ordinal);
        Assert.Contains("--reset-development-kjfk-live-test", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$PreserveState", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $PreserveState)", script, StringComparison.Ordinal);
        Assert.Contains("OpenCareer.LiveTests\\KJFK", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CreateStateDatabaseAsync(string path, string value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE persisted_test_state (value TEXT NOT NULL);" +
            "INSERT INTO persisted_test_state (value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadStateAsync(string path)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM persisted_test_state LIMIT 1;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SeedProductionRecoveryStateAsync(
        string databasePath,
        Guid contractId,
        Guid sessionId,
        string aircraftId,
        string reservationId,
        DateTimeOffset now)
    {
        var options = new OpenCareerDatabaseOptions(databasePath);
        var contracts = new SqliteJobContractStore(
            options,
            NullLogger<SqliteJobContractStore>.Instance);
        JobContract offered = CreateContract(contractId, now);
        await contracts.CreateJobContractAsync(offered);

        JobContract accepted = offered with
        {
            Status = ContractStatus.Accepted,
            AcceptedAt = now.AddMinutes(1)
        };
        accepted.Validate();
        await contracts.UpdateJobContractAsync(accepted, expectedVersion: 0);

        JobContract inProgress = accepted with
        {
            Status = ContractStatus.InProgress,
            StartedAt = now.AddMinutes(2)
        };
        inProgress.Validate();
        await contracts.UpdateJobContractAsync(inProgress, expectedVersion: 1);

        var reservations = new SqliteAircraftAvailabilityStore(databasePath);
        Assert.Equal(
            AircraftReservationAcquireResult.Acquired,
            await reservations.TryReserveAsync(aircraftId, reservationId));

        var checkpoints = new SqliteFlightSessionCheckpointStore(databasePath);
        await checkpoints.SaveAsync(
            FlightSession.Start(
                now.AddMinutes(2),
                contractId,
                sessionId));

        var boards = new SqliteJobBoardStateStore(
            options,
            NullLogger<SqliteJobBoardStateStore>.Instance);
        await boards.SaveAsync(JobBoardState.Empty("KJFK", now));
    }

    private static async Task AssertProductionRecoveryStateAsync(
        string databasePath,
        Guid contractId,
        Guid sessionId,
        string aircraftId,
        string reservationId,
        bool expectedPresent)
    {
        var options = new OpenCareerDatabaseOptions(databasePath);
        var contracts = new SqliteJobContractStore(
            options,
            NullLogger<SqliteJobContractStore>.Instance);
        var reservations = new SqliteAircraftAvailabilityStore(databasePath);
        var checkpoints = new SqliteFlightSessionCheckpointStore(databasePath);
        var boards = new SqliteJobBoardStateStore(
            options,
            NullLogger<SqliteJobBoardStateStore>.Instance);

        PersistedJobContract? contract = await contracts.ReadJobContractAsync(contractId);
        AircraftReservationOwnership? reservation =
            await reservations.FindByReservationIdAsync(reservationId);
        FlightSession? checkpoint = await checkpoints.LoadAsync();
        JobBoardState? board = await boards.GetAsync("KJFK");

        if (expectedPresent)
        {
            Assert.NotNull(contract);
            Assert.Equal(ContractStatus.InProgress, contract.Contract.Status);
            Assert.Equal(aircraftId, reservation?.CanonicalAircraftId);
            Assert.Equal(sessionId, checkpoint?.SessionId);
            Assert.NotNull(board);
            return;
        }

        Assert.Null(contract);
        Assert.Null(reservation);
        Assert.Null(checkpoint);
        Assert.Null(board);
    }

    private static JobContract CreateContract(Guid contractId, DateTimeOffset offeredAt) =>
        new(
            ContractId: contractId,
            EmployerId: null,
            Kind: ContractKind.Other,
            ServiceTrack: ServiceTrack.IndependentContract,
            OriginIcao: "KJFK",
            DestinationIcao: "KJFK",
            Compensation: new ContractCompensation(
                CompensationModel.MissionFee,
                GrossCustomerRevenue: 0m,
                PilotCompensation: 0m,
                EmployerCoversFuel: true,
                EmployerCoversMaintenance: true,
                EmployerCoversAirportFees: true),
            OfferedAt: offeredAt,
            MustStartBy: offeredAt.AddHours(1),
            MustCompleteBy: offeredAt.AddHours(2),
            AircraftRequirements: new AircraftMissionRequirements(
                AircraftCapability.None,
                AircraftAccess.Civilian,
                MinimumPayloadPounds: 0,
                MinimumRangeNauticalMiles: 0,
                MinimumSeats: 0),
            ReputationReward: 0,
            ReputationPenalty: 0,
            MarketId: "development:kjfk-live-test");

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "OpenCareer.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
