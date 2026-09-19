using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.AviationData;

public sealed record OpenAipAirspaceReference(string Id, string Name, int? Type, int? IcaoClass,
    bool? ByNotam, bool? OnDemand, bool? OnRequest, string GeometryGeoJson,
    string? LowerLimitJson, string? UpperLimitJson, string? HoursJson);

public sealed class OpenAipAirspaceDatabase(string databasePath)
{
    public const string Attribution = "Airspace data © OpenAIP contributors — https://www.openaip.net (CC BY-NC 4.0)";

    // Bounding-box matches are candidates for display; they do not establish clearance or an active restriction.
    public IReadOnlyList<OpenAipAirspaceReference> FindBoundsCandidates(string countryCode,
        double minLongitude, double minLatitude, double maxLongitude, double maxLatitude)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        if (minLongitude > maxLongitude || minLatitude > maxLatitude ||
            !double.IsFinite(minLongitude) || !double.IsFinite(minLatitude) ||
            !double.IsFinite(maxLongitude) || !double.IsFinite(maxLatitude))
            throw new ArgumentOutOfRangeException(nameof(minLongitude), "Invalid bounds.");
        if (!File.Exists(databasePath)) return [];
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        using (var table = connection.CreateCommand())
        {
            table.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'oaip_airspaces'";
            if (table.ExecuteScalar() is null) return [];
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, type, icao_class, by_notam, on_demand, on_request,
                   geometry_json, lower_limit_json, upper_limit_json, hours_json
            FROM oaip_airspaces
            WHERE country = $country AND min_lon <= $maxLon AND max_lon >= $minLon
              AND min_lat <= $maxLat AND max_lat >= $minLat
            ORDER BY name, id
            """;
        command.Parameters.AddWithValue("$country", countryCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$minLon", minLongitude);
        command.Parameters.AddWithValue("$minLat", minLatitude);
        command.Parameters.AddWithValue("$maxLon", maxLongitude);
        command.Parameters.AddWithValue("$maxLat", maxLatitude);
        using var row = command.ExecuteReader();
        var result = new List<OpenAipAirspaceReference>();
        while (row.Read())
            result.Add(new OpenAipAirspaceReference(row.GetString(0), row.GetString(1),
                row.IsDBNull(2) ? null : row.GetInt32(2), row.IsDBNull(3) ? null : row.GetInt32(3),
                row.IsDBNull(4) ? null : row.GetInt32(4) == 1,
                row.IsDBNull(5) ? null : row.GetInt32(5) == 1,
                row.IsDBNull(6) ? null : row.GetInt32(6) == 1,
                row.GetString(7), row.IsDBNull(8) ? null : row.GetString(8),
                row.IsDBNull(9) ? null : row.GetString(9), row.IsDBNull(10) ? null : row.GetString(10)));
        return result;
    }
}
