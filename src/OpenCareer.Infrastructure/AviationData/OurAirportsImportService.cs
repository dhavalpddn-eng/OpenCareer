using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

namespace OpenCareer.Infrastructure.AviationData;

public sealed class OurAirportsImportService
{
    private sealed record Column(string Name, string Type = "TEXT");
    private sealed record Dataset(string File, string Table, Column[] Columns);

    private static readonly Dataset[] Datasets =
    [
        new("countries.csv", "oa_countries", [new("id", "INTEGER"), new("code"), new("name"), new("continent")]),
        new("regions.csv", "oa_regions", [new("id", "INTEGER"), new("code"), new("local_code"), new("name"), new("continent"), new("iso_country")]),
        new("airports.csv", "oa_airports", [new("id", "INTEGER"), new("ident"), new("type"), new("name"), new("latitude_deg", "REAL"), new("longitude_deg", "REAL"), new("elevation_ft", "INTEGER"), new("continent"), new("iso_country"), new("iso_region"), new("municipality"), new("scheduled_service"), new("gps_code"), new("icao_code"), new("iata_code"), new("local_code")]),
        new("runways.csv", "oa_runways", [new("id", "INTEGER"), new("airport_ident"), new("length_ft", "INTEGER"), new("width_ft", "INTEGER"), new("surface"), new("lighted", "INTEGER"), new("closed", "INTEGER"), new("le_ident"), new("le_heading_degT", "REAL"), new("le_displaced_threshold_ft", "INTEGER"), new("he_ident"), new("he_heading_degT", "REAL"), new("he_displaced_threshold_ft", "INTEGER")]),
        new("airport-frequencies.csv", "oa_frequencies", [new("id", "INTEGER"), new("airport_ident"), new("type"), new("description"), new("frequency_mhz", "REAL")]),
        new("navaids.csv", "oa_navaids", [new("id", "INTEGER"), new("ident"), new("name"), new("type"), new("frequency_khz", "INTEGER"), new("latitude_deg", "REAL"), new("longitude_deg", "REAL"), new("iso_country"), new("associated_airport")])
    ];

    public static IReadOnlyList<string> RequiredFiles { get; } = Array.AsReadOnly(Datasets.Select(x => x.File).ToArray());

    public IReadOnlyDictionary<string, int> Import(string sourceDirectory, string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        foreach (var dataset in Datasets)
            if (!File.Exists(Path.Combine(sourceDirectory, dataset.File)))
                throw new FileNotFoundException($"Missing OurAirports dataset: {dataset.File}", Path.Combine(sourceDirectory, dataset.File));

        var parent = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(parent);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var dataset in Datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var create = connection.CreateCommand();
            create.Transaction = transaction;
            create.CommandText = $"CREATE TABLE IF NOT EXISTS {dataset.Table} ({string.Join(", ", dataset.Columns.Select(c => c.Name + " " + c.Type))}, PRIMARY KEY (id))";
            create.ExecuteNonQuery();
            using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM {dataset.Table}";
            clear.ExecuteNonQuery();
            counts.Add(dataset.File, ImportFile(Path.Combine(sourceDirectory, dataset.File), dataset, connection, transaction, cancellationToken));
        }

        foreach (var (table, column) in new[] { ("oa_airports", "ident"), ("oa_airports", "icao_code"), ("oa_airports", "iata_code"), ("oa_runways", "airport_ident"), ("oa_frequencies", "airport_ident"), ("oa_navaids", "associated_airport"), ("oa_regions", "code"), ("oa_countries", "code") })
        {
            using var index = connection.CreateCommand();
            index.Transaction = transaction;
            index.CommandText = $"CREATE INDEX IF NOT EXISTS ix_{table}_{column} ON {table} ({column})";
            index.ExecuteNonQuery();
        }

        using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = "CREATE TABLE IF NOT EXISTS oa_import (id INTEGER PRIMARY KEY CHECK(id=1), source TEXT NOT NULL, imported_utc TEXT NOT NULL)";
            meta.ExecuteNonQuery();
            meta.CommandText = "INSERT INTO oa_import (id, source, imported_utc) VALUES (1, 'OurAirports', $time) ON CONFLICT(id) DO UPDATE SET imported_utc=excluded.imported_utc";
            meta.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            meta.ExecuteNonQuery();
        }
        transaction.Commit();
        return counts;
    }

    private static int ImportFile(string path, Dataset dataset, SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException($"Empty CSV: {path}");
        var positions = dataset.Columns.Select(c => Array.IndexOf(headers, c.Name)).ToArray();
        if (positions.Any(p => p < 0))
            throw new InvalidDataException($"Required column missing from {dataset.File}: {string.Join(", ", dataset.Columns.Where((_, i) => positions[i] < 0).Select(c => c.Name))}");

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {dataset.Table} ({string.Join(", ", dataset.Columns.Select(c => c.Name))}) VALUES ({string.Join(", ", dataset.Columns.Select((_, i) => "$v" + i))})";
        for (var i = 0; i < dataset.Columns.Length; i++) insert.Parameters.Add(new SqliteParameter("$v" + i, DBNull.Value));
        var count = 0;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = parser.ReadFields() ?? throw new InvalidDataException($"Invalid row in {dataset.File}");
            if (row.Length != headers.Length) throw new InvalidDataException($"Column count mismatch in {dataset.File} at line {parser.LineNumber}");
            for (var i = 0; i < positions.Length; i++)
            {
                var value = row[positions[i]];
                var column = dataset.Columns[i];
                insert.Parameters[i].Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : column.Type switch
                {
                    "INTEGER" => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    "REAL" => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
                    _ => value
                };
            }
            if (insert.Parameters[0].Value is DBNull) throw new InvalidDataException($"Missing ID in {dataset.File}");
            insert.ExecuteNonQuery();
            count++;
        }
        if (count == 0) throw new InvalidDataException($"No records in {dataset.File}");
        return count;
    }
}
