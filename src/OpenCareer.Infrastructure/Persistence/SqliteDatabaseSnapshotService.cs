using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.Persistence;

public sealed class SqliteDatabaseSnapshotService
{
    private readonly OpenCareerDatabaseOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteDatabaseSnapshotService(OpenCareerDatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task CreateSnapshotAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string sourcePath = Path.GetFullPath(_options.DatabasePath);
        string targetPath = Path.GetFullPath(destinationPath);

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Snapshot destination must differ from the live database.", nameof(destinationPath));

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "OpenCareer database does not exist yet.",
                    sourcePath);
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            await using var source = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = sourcePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = false
                }.ToString());

            await using var destination = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = targetPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false
                }.ToString());

            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);

            source.BackupDatabase(destination);

            cancellationToken.ThrowIfCancellationRequested();
            await ValidateSnapshotAsync(targetPath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task ValidateSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        string fullPath = Path.GetFullPath(snapshotPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "SQLite snapshot does not exist.",
                fullPath);
        }

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";

        object? result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(
            Convert.ToString(
                result,
                System.Globalization.CultureInfo.InvariantCulture),
            "ok",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "SQLite backup snapshot failed integrity validation.");
        }
    }
}
