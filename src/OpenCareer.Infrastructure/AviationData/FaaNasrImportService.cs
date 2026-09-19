using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

namespace OpenCareer.Infrastructure.AviationData;

public sealed record FaaNasrImportResult(DateOnly EffectiveDate, IReadOnlyDictionary<string, int> Counts);

public sealed class FaaNasrImportService
{
    private sealed record Column(string Name, string Type = "TEXT");
    private sealed record Dataset(string File, string Table, Column[] Columns);

    private static readonly Dataset[] Datasets =
    [
        new("APT_BASE.csv", "faa_airports", [new("EFF_DATE"), new("SITE_NO"), new("SITE_TYPE_CODE"), new("ARPT_ID"), new("ICAO_ID"), new("ARPT_NAME"), new("CITY"), new("STATE_CODE"), new("COUNTRY_CODE"), new("LAT_DECIMAL", "REAL"), new("LONG_DECIMAL", "REAL"), new("ELEV", "REAL"), new("FACILITY_USE_CODE"), new("MIL_LNDG_FLAG"), new("JOINT_USE_FLAG"), new("ARPT_STATUS")]),
        new("APT_RWY.csv", "faa_runways", [new("EFF_DATE"), new("SITE_NO"), new("SITE_TYPE_CODE"), new("ARPT_ID"), new("RWY_ID"), new("RWY_LEN", "INTEGER"), new("RWY_WIDTH", "INTEGER"), new("SURFACE_TYPE_CODE"), new("COND"), new("RWY_LGT_CODE")]),
        new("APT_RWY_END.csv", "faa_runway_ends", [new("EFF_DATE"), new("SITE_NO"), new("SITE_TYPE_CODE"), new("RWY_ID"), new("RWY_END_ID"), new("TRUE_ALIGNMENT", "REAL"), new("ILS_TYPE"), new("LAT_DECIMAL", "REAL"), new("LONG_DECIMAL", "REAL"), new("DISPLACED_THR_LEN", "INTEGER"), new("TKOF_DIST_AVBL", "INTEGER"), new("LNDG_DIST_AVBL", "INTEGER")]),
        new("FIX_BASE.csv", "faa_fixes", [new("EFF_DATE"), new("FIX_ID"), new("ICAO_REGION_CODE"), new("STATE_CODE"), new("COUNTRY_CODE"), new("LAT_DECIMAL", "REAL"), new("LONG_DECIMAL", "REAL"), new("FIX_USE_CODE")]),
        new("CLS_ARSP.csv", "faa_airspace_classes", [new("EFF_DATE"), new("SITE_NO"), new("SITE_TYPE_CODE"), new("ARPT_ID"), new("CLASS_B_AIRSPACE"), new("CLASS_C_AIRSPACE"), new("CLASS_D_AIRSPACE"), new("CLASS_E_AIRSPACE"), new("AIRSPACE_HRS"), new("REMARK")]),
        new("MIL_OPS.csv", "faa_military_ops", [new("EFF_DATE"), new("SITE_NO"), new("SITE_TYPE_CODE"), new("ARPT_ID"), new("MIL_OPS_OPER_CODE"), new("MIL_OPS_CALL"), new("MIL_OPS_HRS"), new("REMARK")]),
        new("MTR_BASE.csv", "faa_military_routes", [new("EFF_DATE"), new("ROUTE_TYPE_CODE"), new("ROUTE_ID"), new("ARTCC"), new("TIME_OF_USE")]),
        new("MTR_PT.csv", "faa_military_route_points", [new("EFF_DATE"), new("ROUTE_TYPE_CODE"), new("ROUTE_ID"), new("ROUTE_PT_SEQ", "INTEGER"), new("ROUTE_PT_ID"), new("NEXT_ROUTE_PT_ID"), new("SEGMENT_TEXT"), new("LAT_DECIMAL", "REAL"), new("LONG_DECIMAL", "REAL")])
    ];

    public FaaNasrImportResult Import(string archivePath, string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        using var outer = ZipFile.OpenRead(archivePath);
        var nested = outer.Entries.Where(e => e.FullName.StartsWith("CSV_Data/", StringComparison.OrdinalIgnoreCase)
            && e.Name.EndsWith("_CSV.zip", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nested.Length != 1 || nested[0].Length > 100_000_000)
            throw new InvalidDataException("Expected one FAA NASR CSV archive inside the subscription ZIP.");
        using var buffer = new MemoryStream();
        using (var source = nested[0].Open()) source.CopyTo(buffer);
        buffer.Position = 0;
        using var csvArchive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        var entries = Datasets.ToDictionary(d => d.File, d => csvArchive.GetEntry(d.File)
            ?? throw new InvalidDataException($"Missing NASR dataset: {d.File}"));
        if (entries.Values.Any(e => e.Length > 100_000_000))
            throw new InvalidDataException("NASR dataset exceeds the import limit.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        DateOnly? effectiveDate = null;
        foreach (var dataset in Datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = $"CREATE TABLE IF NOT EXISTS {dataset.Table} (row_id INTEGER PRIMARY KEY, {string.Join(", ", dataset.Columns.Select(c => c.Name + " " + c.Type))})";
            schema.ExecuteNonQuery();
            using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM {dataset.Table}";
            clear.ExecuteNonQuery();
            counts.Add(dataset.File, ImportFile(entries[dataset.File], dataset, connection, transaction, ref effectiveDate, cancellationToken));
        }

        foreach (var (table, columns) in new[]
        {
            ("faa_airports", "ICAO_ID"), ("faa_airports", "ARPT_ID"), ("faa_airports", "SITE_NO, SITE_TYPE_CODE"),
            ("faa_runways", "SITE_NO, SITE_TYPE_CODE"), ("faa_runway_ends", "SITE_NO, SITE_TYPE_CODE, RWY_ID"),
            ("faa_fixes", "FIX_ID"), ("faa_military_ops", "SITE_NO, SITE_TYPE_CODE"),
            ("faa_military_routes", "ROUTE_TYPE_CODE, ROUTE_ID"), ("faa_military_route_points", "ROUTE_TYPE_CODE, ROUTE_ID, ROUTE_PT_SEQ")
        })
        {
            using var index = connection.CreateCommand();
            index.Transaction = transaction;
            index.CommandText = $"CREATE INDEX IF NOT EXISTS ix_{table}_{columns.Replace(", ", "_")} ON {table} ({columns})";
            index.ExecuteNonQuery();
        }
        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = """
                CREATE TABLE IF NOT EXISTS faa_import (
                    id INTEGER PRIMARY KEY CHECK(id = 1), effective_date TEXT NOT NULL,
                    imported_utc TEXT NOT NULL, source_file TEXT NOT NULL)
                """;
            metadata.ExecuteNonQuery();
            metadata.CommandText = """
                INSERT INTO faa_import (id, effective_date, imported_utc, source_file)
                VALUES (1, $date, $time, $source)
                ON CONFLICT(id) DO UPDATE SET effective_date=excluded.effective_date,
                    imported_utc=excluded.imported_utc, source_file=excluded.source_file
                """;
            metadata.Parameters.AddWithValue("$date", effectiveDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            metadata.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            metadata.Parameters.AddWithValue("$source", Path.GetFileName(archivePath));
            metadata.ExecuteNonQuery();
        }
        transaction.Commit();
        return new FaaNasrImportResult(effectiveDate!.Value, counts);
    }

    private static int ImportFile(ZipArchiveEntry entry, Dataset dataset, SqliteConnection connection,
        SqliteTransaction transaction, ref DateOnly? effectiveDate, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException($"Empty NASR file: {entry.Name}");
        var indices = dataset.Columns.Select(c => Array.IndexOf(headers, c.Name)).ToArray();
        if (indices.Any(i => i < 0))
            throw new InvalidDataException($"NASR column missing from {entry.Name}: {string.Join(", ", dataset.Columns.Where((_, i) => indices[i] < 0).Select(c => c.Name))}");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {dataset.Table} ({string.Join(", ", dataset.Columns.Select(c => c.Name))}) VALUES ({string.Join(", ", dataset.Columns.Select((_, i) => "$v" + i))})";
        for (var i = 0; i < dataset.Columns.Length; i++) insert.Parameters.Add(new SqliteParameter("$v" + i, DBNull.Value));
        var count = 0;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? throw new InvalidDataException($"Invalid NASR row in {entry.Name}");
            if (fields.Length != headers.Length)
                throw new InvalidDataException($"Column count mismatch in {entry.Name} at line {parser.LineNumber}");
            var dateText = fields[indices[0]];
            if (!DateOnly.TryParseExact(dateText, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rowDate)
                || (effectiveDate.HasValue && effectiveDate.Value != rowDate))
                throw new InvalidDataException($"NASR effective dates differ in {entry.Name}");
            effectiveDate ??= rowDate;
            for (var i = 0; i < indices.Length; i++)
            {
                var text = fields[indices[i]];
                insert.Parameters[i].Value = string.IsNullOrWhiteSpace(text) ? DBNull.Value : dataset.Columns[i].Type switch
                {
                    "INTEGER" => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    "REAL" => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                    _ => text
                };
            }
            insert.ExecuteNonQuery();
            count++;
        }
        if (count == 0) throw new InvalidDataException($"No NASR records in {entry.Name}");
        return count;
    }
}
