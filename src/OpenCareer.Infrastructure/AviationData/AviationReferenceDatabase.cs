using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.AviationData;

public sealed record AirportReference(long Id, string Ident, string Name, string Type, double? Latitude, double? Longitude,
    int? ElevationFeet, string? IcaoCode, string? IataCode, string? CountryCode, string? RegionCode, bool ScheduledService);

public sealed record RunwayReference(long Id, string AirportIdent, int? LengthFeet, int? WidthFeet, string? Surface,
    bool Lighted, bool Closed, string? LowEnd, string? HighEnd);

public sealed class AviationReferenceDatabase(string databasePath)
{
    // This read-only adapter never contacts OurAirports. It is safe to use during ordinary gameplay.
    public AirportReference? FindAirport(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, ident, name, type, latitude_deg, longitude_deg, elevation_ft,
                   icao_code, iata_code, iso_country, iso_region, scheduled_service
            FROM oa_airports
            WHERE ident = $code OR icao_code = $code OR iata_code = $code
            ORDER BY CASE WHEN ident = $code THEN 0 WHEN icao_code = $code THEN 1 ELSE 2 END, id
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$code", code.Trim().ToUpperInvariant());
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new AirportReference(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), NullableDouble(row, 4), NullableDouble(row, 5),
            NullableInt(row, 6), NullableString(row, 7), NullableString(row, 8), NullableString(row, 9), NullableString(row, 10), row.GetString(11) == "yes");
    }

    public IReadOnlyList<RunwayReference> GetRunways(string airportIdent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(airportIdent);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, airport_ident, length_ft, width_ft, surface, lighted, closed, le_ident, he_ident FROM oa_runways WHERE airport_ident = $ident ORDER BY id";
        command.Parameters.AddWithValue("$ident", airportIdent.Trim().ToUpperInvariant());
        using var row = command.ExecuteReader();
        var runways = new List<RunwayReference>();
        while (row.Read())
            runways.Add(new RunwayReference(row.GetInt64(0), row.GetString(1), NullableInt(row, 2), NullableInt(row, 3), NullableString(row, 4),
                NullableInt(row, 5) == 1, NullableInt(row, 6) == 1, NullableString(row, 7), NullableString(row, 8)));
        return runways;
    }

    private SqliteConnection Open()
    {
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Aviation reference database has not been imported.", databasePath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        return connection;
    }

    private static int? NullableInt(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetInt32(index);
    private static double? NullableDouble(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetDouble(index);
    private static string? NullableString(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetString(index);
}
