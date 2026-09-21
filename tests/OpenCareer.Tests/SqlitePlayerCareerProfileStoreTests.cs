using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqlitePlayerCareerProfileStoreTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 2, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("51fb4227-62e2-4e48-a09d-cf59760ac28b");

    [Fact]
    public async Task RoundTripRecoversProfileAcrossStoreRestart()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);
            PlayerCareerProfile profile =
                PlayerCareerProfile.Start(
                    CareerId,
                    "krme",
                    Epoch);

            PlayerCareerProfileStoreRecord saved =
                await store.SaveAsync(
                    profile,
                    expectedRevision: null,
                    savedAt: Epoch.AddMinutes(1));

            Assert.Equal(1, saved.Revision);
            Assert.Equal("KRME", saved.Profile.Location.HomeAirportIcao);
            Assert.Equal("KRME", saved.Profile.Location.CurrentAirportIcao);

            var restartedStore = CreateStore(options);
            PlayerCareerProfileStoreRecord? recovered =
                await restartedStore.LoadAsync();

            AssertEquivalent(saved, recovered);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task UpdateUsesOptimisticRevisionAndPreservesCareerIdentity()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);
            PlayerCareerProfile profile =
                PlayerCareerProfile.Start(
                    CareerId,
                    "KRME",
                    Epoch);

            PlayerCareerProfileStoreRecord first =
                await store.SaveAsync(
                    profile,
                    expectedRevision: null,
                    savedAt: Epoch.AddMinutes(1));

            CareerLocation arrived = profile.Location with
            {
                CurrentAirportIcao = "KALB",
                UpdatedAt = Epoch.AddHours(1),
                Connections = profile.Location.Connections.Add("KALB"),
                AppliedTravelContracts =
                    ImmutableHashSet<Guid>.Empty.Add(
                        Guid.Parse("f2a9a1be-a054-46a4-bec1-3941597ccbbd"))
            };
            arrived.Validate();

            PlayerCareerProfile updated =
                profile with { Location = arrived };
            updated.Validate();

            PlayerCareerProfileStoreRecord second =
                await store.SaveAsync(
                    updated,
                    expectedRevision: first.Revision,
                    savedAt: Epoch.AddHours(1));

            Assert.Equal(2, second.Revision);
            Assert.Equal(CareerId, second.Profile.CareerId);
            Assert.Equal("KALB", second.Profile.Location.CurrentAirportIcao);

            await Assert.ThrowsAsync<PlayerCareerProfileConcurrencyException>(
                () => store.SaveAsync(
                    profile,
                    expectedRevision: first.Revision,
                    savedAt: Epoch.AddHours(2)));

            AssertEquivalent(second, await store.LoadAsync());
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task DifferentCareerIdentityCannotOverwriteExistingProfile()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);

            PlayerCareerProfileStoreRecord first =
                await store.SaveAsync(
                    PlayerCareerProfile.Start(
                        CareerId,
                        "KRME",
                        Epoch),
                    expectedRevision: null,
                    savedAt: Epoch.AddMinutes(1));

            PlayerCareerProfile other =
                PlayerCareerProfile.Start(
                    Guid.Parse("31ddf5c1-431f-49a5-b5bd-1fe10b8bd388"),
                    "KDFW",
                    Epoch);

            await Assert.ThrowsAsync<PlayerCareerProfileConcurrencyException>(
                () => store.SaveAsync(
                    other,
                    expectedRevision: first.Revision,
                    savedAt: Epoch.AddHours(1)));

            AssertEquivalent(first, await store.LoadAsync());
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task InvalidSaveTimestampIsRejectedWithoutCreatingProfile()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);
            PlayerCareerProfile profile =
                PlayerCareerProfile.Start(
                    CareerId,
                    "KRME",
                    Epoch);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => store.SaveAsync(
                    profile,
                    expectedRevision: null,
                    savedAt: Epoch.AddMinutes(-1)));

            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task EmptyDatabaseReturnsNoProfile()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options = new OpenCareerDatabaseOptions(
                Path.Combine(directory, "opencareer.db"));

            Assert.Null(await CreateStore(options).LoadAsync());
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static void AssertEquivalent(
        PlayerCareerProfileStoreRecord expected,
        PlayerCareerProfileStoreRecord? actualRecord)
    {
        PlayerCareerProfileStoreRecord actual =
            Assert.IsType<PlayerCareerProfileStoreRecord>(actualRecord);

        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.SavedAt, actual.SavedAt);
        Assert.Equal(expected.Profile.CareerId, actual.Profile.CareerId);
        Assert.Equal(expected.Profile.CreatedAt, actual.Profile.CreatedAt);
        Assert.Equal(
            expected.Profile.Location.HomeAirportIcao,
            actual.Profile.Location.HomeAirportIcao);
        Assert.Equal(
            expected.Profile.Location.CurrentAirportIcao,
            actual.Profile.Location.CurrentAirportIcao);
        Assert.Equal(
            expected.Profile.Location.UpdatedAt,
            actual.Profile.Location.UpdatedAt);
        Assert.True(
            expected.Profile.Location.Connections.SetEquals(
                actual.Profile.Location.Connections));
        Assert.True(
            expected.Profile.Location.AppliedTravelContracts.SetEquals(
                actual.Profile.Location.AppliedTravelContracts));
    }

    private static SqlitePlayerCareerProfileStore CreateStore(
        OpenCareerDatabaseOptions options) =>
        new(
            options,
            NullLogger<SqlitePlayerCareerProfileStore>.Instance);

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(
        string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Cleanup must not make a passing SQLite assertion platform-specific.
        }
    }
}
