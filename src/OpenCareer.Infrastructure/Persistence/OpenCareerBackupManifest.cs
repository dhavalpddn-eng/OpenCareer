namespace OpenCareer.Infrastructure.Persistence;

public sealed record OpenCareerBackupManifest(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    string AppDataRoot,
    string[] IncludedFiles,
    bool SqliteSnapshotIncluded,
    string Note)
{
    public const int CurrentSchemaVersion = 2;
    public const string ExpectedAppDataRoot = "OpenCareer";
    public const string DatabaseEntryName = "opencareer.db";
    public const string ManifestEntryName = "backup-manifest.json";

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"OpenCareer backup schema {SchemaVersion} is not supported.");
        }

        if (!string.Equals(
            AppDataRoot,
            ExpectedAppDataRoot,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Backup archive is not an OpenCareer application-data backup.");
        }

        ArgumentNullException.ThrowIfNull(IncludedFiles);
        ArgumentNullException.ThrowIfNull(Note);

        if (IncludedFiles.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Backup manifest contains an invalid file entry.");

        if (IncludedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != IncludedFiles.Length)
        {
            throw new InvalidDataException(
                "Backup manifest contains duplicate file entries.");
        }

        bool manifestClaimsDatabase = IncludedFiles.Contains(
            DatabaseEntryName,
            StringComparer.OrdinalIgnoreCase);

        if (SqliteSnapshotIncluded != manifestClaimsDatabase)
        {
            throw new InvalidDataException(
                "Backup manifest SQLite flag does not match its included-files list.");
        }
    }
}
