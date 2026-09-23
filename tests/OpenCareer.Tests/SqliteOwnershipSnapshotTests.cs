using Microsoft.Data.Sqlite;
using OpenCareer.Application.Ownership;
using OpenCareer.Infrastructure.Ownership;

namespace OpenCareer.Tests;

public sealed class SqliteOwnershipSnapshotTests
{
    [Fact]
    public async Task CareerWithoutOwnershipAccountLoadsEmptySnapshot()
    {
        string path =
            Path.Combine(
                Path.GetTempPath(),
                $"opencareer-empty-ownership-{Guid.NewGuid():N}.db");

        try
        {
            var store =
                new SqliteOwnershipStore(
                    path);

            await store.InitializeAsync();

            OwnershipSnapshot snapshot =
                await store.LoadSnapshotAsync(
                    "career-with-zero-ownership");

            Assert.Equal(
                "career-with-zero-ownership",
                snapshot.Account.CareerId);
            Assert.Equal(
                0m,
                snapshot.Account.CashBalance);
            Assert.Empty(snapshot.Aircraft);
            Assert.Empty(snapshot.Loans);
            Assert.Empty(snapshot.InsurancePolicies);
            Assert.Empty(snapshot.StorageLeases);
            Assert.Empty(snapshot.MaintenanceStates);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static void TryDelete(
        string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
