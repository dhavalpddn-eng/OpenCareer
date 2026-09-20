namespace OpenCareer.Infrastructure.Persistence;

public sealed record OpenCareerDatabaseOptions
{
    public OpenCareerDatabaseOptions(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));

        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }
}
