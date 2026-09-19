using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.AviationData;

public sealed record FaaAirportReference(string SiteNumber, string SiteType, string FaaId, string? IcaoId,
    string Name, string? City, string? State, double? Latitude, double? Longitude,
    double? ElevationFeet, string? FacilityUse, bool? MilitaryLanding, bool? JointUse);

public sealed record FaaRunwayReference(string Id, int? LengthFeet, int? WidthFeet, string? Surface,
    string? Condition, string? Lights);

public sealed record FaaMilitaryRoutePoint(int Sequence, double? Latitude, double? Longitude, string? Segment);

public sealed class FaaNasrReferenceDatabase(string databasePath)
{
    public FaaAirportReference? FindAirport(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SITE_NO, SITE_TYPE_CODE, ARPT_ID, ICAO_ID, ARPT_NAME, CITY, STATE_CODE,
                   LAT_DECIMAL, LONG_DECIMAL, ELEV, FACILITY_USE_CODE, MIL_LNDG_FLAG, JOINT_USE_FLAG
            FROM faa_airports WHERE ICAO_ID = $code OR ARPT_ID = $code
            ORDER BY CASE WHEN ICAO_ID = $code THEN 0 ELSE 1 END, SITE_NO LIMIT 1
            """;
        command.Parameters.AddWithValue("$code", identifier.Trim().ToUpperInvariant());
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new FaaAirportReference(row.GetString(0), row.GetString(1), row.GetString(2), Text(row, 3),
            row.GetString(4), Text(row, 5), Text(row, 6), Real(row, 7), Real(row, 8), Real(row, 9),
            Text(row, 10), Flag(row, 11), Flag(row, 12));
    }

    public IReadOnlyList<FaaRunwayReference> GetRunways(FaaAirportReference airport)
    {
        ArgumentNullException.ThrowIfNull(airport);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RWY_ID, RWY_LEN, RWY_WIDTH, SURFACE_TYPE_CODE, COND, RWY_LGT_CODE
            FROM faa_runways WHERE SITE_NO = $site AND SITE_TYPE_CODE = $type ORDER BY RWY_ID
            """;
        command.Parameters.AddWithValue("$site", airport.SiteNumber);
        command.Parameters.AddWithValue("$type", airport.SiteType);
        using var row = command.ExecuteReader();
        var result = new List<FaaRunwayReference>();
        while (row.Read()) result.Add(new FaaRunwayReference(row.GetString(0), Number(row, 1), Number(row, 2),
            Text(row, 3), Text(row, 4), Text(row, 5)));
        return result;
    }

    public IReadOnlyList<FaaMilitaryRoutePoint> GetMilitaryRoutePoints(string routeType, string routeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ROUTE_PT_SEQ, LAT_DECIMAL, LONG_DECIMAL, SEGMENT_TEXT
            FROM faa_military_route_points WHERE ROUTE_TYPE_CODE = $type AND ROUTE_ID = $id
            ORDER BY ROUTE_PT_SEQ
            """;
        command.Parameters.AddWithValue("$type", routeType.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$id", routeId.Trim().ToUpperInvariant());
        using var row = command.ExecuteReader();
        var result = new List<FaaMilitaryRoutePoint>();
        while (row.Read()) result.Add(new FaaMilitaryRoutePoint(row.GetInt32(0), Real(row, 1), Real(row, 2), Text(row, 3)));
        return result;
    }

    private SqliteConnection Open()
    {
        if (!File.Exists(databasePath)) throw new FileNotFoundException("FAA reference data has not been imported.", databasePath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        return connection;
    }

    private static string? Text(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetString(index);
    private static int? Number(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetInt32(index);
    private static double? Real(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetDouble(index);
    private static bool? Flag(SqliteDataReader row, int index) => Text(row, index) switch { "Y" => true, "N" => false, _ => null };
}
