using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Military;
using OpenCareer.Domain.Military;
using OpenCareer.Infrastructure.Persistence;

namespace OpenCareer.Tests;

public sealed class SqliteMilitaryCareerProfileStoreTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 19, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RoundTripRecoversProfileAcrossStoreRestart()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options =
                new OpenCareerDatabaseOptions(
                    Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);

            var career = new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.Logistics
                    | MilitaryQualification.Patrol,
                Trust: 0.68,
                SuccessfulOperations: 7,
                FailedOperations: 2);

            MilitaryCareerProfileStoreRecord saved =
                await store.SaveAsync(
                    career,
                    expectedRevision: null,
                    savedAt: Epoch);

            Assert.Equal(1, saved.Revision);
            Assert.Equal(career, saved.Career);
            Assert.Equal(Epoch, saved.SavedAt);

            var restartedStore = CreateStore(options);
            MilitaryCareerProfileStoreRecord? recovered =
                await restartedStore.LoadAsync();

            Assert.NotNull(recovered);
            Assert.Equal(saved, recovered);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task UpdateUsesOptimisticRevisionAndRejectsStaleWriter()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options =
                new OpenCareerDatabaseOptions(
                    Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);

            var initial = new MilitaryCareerState(
                MilitaryAffiliation.GovernmentContractor,
                MilitaryQualification.MilitaryFlight,
                Trust: 0.40,
                SuccessfulOperations: 1,
                FailedOperations: 0);

            MilitaryCareerProfileStoreRecord first =
                await store.SaveAsync(
                    initial,
                    expectedRevision: null,
                    savedAt: Epoch);

            MilitaryCareerState updatedCareer = initial with
            {
                Trust = 0.55,
                SuccessfulOperations = 2
            };

            MilitaryCareerProfileStoreRecord second =
                await store.SaveAsync(
                    updatedCareer,
                    expectedRevision: first.Revision,
                    savedAt: Epoch.AddMinutes(5));

            Assert.Equal(2, second.Revision);
            Assert.Equal(updatedCareer, second.Career);

            await Assert.ThrowsAsync<MilitaryCareerProfileConcurrencyException>(
                () => store.SaveAsync(
                    initial,
                    expectedRevision: first.Revision,
                    savedAt: Epoch.AddMinutes(10)));

            Assert.Equal(
                second,
                await store.LoadAsync());
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task DuplicateCreateIsRejectedWithoutOverwritingProfile()
    {
        string directory = CreateTempDirectory();

        try
        {
            var options =
                new OpenCareerDatabaseOptions(
                    Path.Combine(directory, "opencareer.db"));
            var store = CreateStore(options);

            var original = new MilitaryCareerState(
                MilitaryAffiliation.ActiveDuty,
                MilitaryQualification.MilitaryFlight
                    | MilitaryQualification.Intercept,
                Trust: 0.75,
                SuccessfulOperations: 5,
                FailedOperations: 1);

            MilitaryCareerProfileStoreRecord saved =
                await store.SaveAsync(
                    original,
                    expectedRevision: null,
                    savedAt: Epoch);

            var replacement = new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight,
                Trust: 0.10,
                SuccessfulOperations: 0,
                FailedOperations: 0);

            await Assert.ThrowsAsync<MilitaryCareerProfileConcurrencyException>(
                () => store.SaveAsync(
                    replacement,
                    expectedRevision: null,
                    savedAt: Epoch.AddHours(1)));

            Assert.Equal(
                saved,
                await store.LoadAsync());
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
            var options =
                new OpenCareerDatabaseOptions(
                    Path.Combine(directory, "opencareer.db"));

            Assert.Null(
                await CreateStore(options).LoadAsync());
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static SqliteMilitaryCareerProfileStore CreateStore(
        OpenCareerDatabaseOptions options) =>
        new(
            options,
            NullLogger<SqliteMilitaryCareerProfileStore>.Instance);

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
