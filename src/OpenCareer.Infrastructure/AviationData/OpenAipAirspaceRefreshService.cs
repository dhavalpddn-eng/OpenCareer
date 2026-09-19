using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OpenCareer.Infrastructure.AviationData;

public sealed class OpenAipAirspaceRefreshService(HttpClient httpClient)
{
    private const int PageSize = 500;
    private sealed record Airspace(string Id, string Name, int? Type, int? IcaoClass, bool? ByNotam,
        bool? OnDemand, bool? OnRequest, string Geometry, string? LowerLimit, string? UpperLimit,
        string? Hours, double MinLongitude, double MinLatitude, double MaxLongitude, double MaxLatitude);

    public async Task<int> RefreshCountryAsync(string databasePath, string countryCode, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var country = countryCode?.Trim().ToUpperInvariant();
        if (country is null || country.Length != 2 || !country.All(c => c is >= 'A' and <= 'Z'))
            throw new ArgumentException("Country must be a two-letter ISO code.", nameof(countryCode));

        var airspaces = new List<Airspace>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int? totalCount = null;
        for (var page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page > 1000) throw new InvalidDataException("OpenAIP pagination exceeded the allowed range.");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.core.openaip.net/api/airspaces?country={country}&page={page}&limit={PageSize}");
            request.Headers.Add("x-openaip-api-key", apiKey);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException("OpenAIP rejected the API key or its access permissions.");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.GetProperty("page").GetInt32() != page || root.GetProperty("limit").GetInt32() != PageSize)
                throw new InvalidDataException("Unexpected OpenAIP pagination response.");
            var currentTotal = root.GetProperty("totalCount").GetInt32();
            var totalPages = root.GetProperty("totalPages").GetInt32();
            if (currentTotal < 0 || totalPages < 0 || (totalCount is not null && totalCount != currentTotal))
                throw new InvalidDataException("OpenAIP total count changed during refresh.");
            totalCount = currentTotal;
            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var parsed = Parse(item);
                if (!ids.Add(parsed.Id)) throw new InvalidDataException("Duplicate OpenAIP airspace ID across pages.");
                airspaces.Add(parsed);
            }
            if (page >= totalPages || currentTotal == 0) break;
        }
        if (airspaces.Count != totalCount)
            throw new InvalidDataException("OpenAIP returned an incomplete airspace listing; existing data was preserved.");

        var parent = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(parent);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS oaip_airspaces (
                    country TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, type INTEGER,
                    icao_class INTEGER, by_notam INTEGER, on_demand INTEGER,
                    on_request INTEGER, geometry_json TEXT NOT NULL, lower_limit_json TEXT,
                    upper_limit_json TEXT, hours_json TEXT,
                    min_lon REAL NOT NULL, min_lat REAL NOT NULL, max_lon REAL NOT NULL, max_lat REAL NOT NULL,
                    PRIMARY KEY (country, id));
                CREATE INDEX IF NOT EXISTS ix_oaip_airspaces_bounds ON oaip_airspaces (country, min_lon, max_lon, min_lat, max_lat);
                CREATE TABLE IF NOT EXISTS oaip_imports (
                    country TEXT PRIMARY KEY, imported_utc TEXT NOT NULL, source TEXT NOT NULL);
                """;
            schema.ExecuteNonQuery();
        }
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM oaip_airspaces WHERE country = $country";
            delete.Parameters.AddWithValue("$country", country);
            delete.ExecuteNonQuery();
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO oaip_airspaces (country, id, name, type, icao_class, by_notam, on_demand,
                    on_request, geometry_json, lower_limit_json, upper_limit_json, hours_json,
                    min_lon, min_lat, max_lon, max_lat)
                VALUES ($country, $id, $name, $type, $class, $notam, $demand, $request, $geometry,
                    $lower, $upper, $hours, $minLon, $minLat, $maxLon, $maxLat)
                """;
            foreach (var name in new[] { "$country", "$id", "$name", "$type", "$class", "$notam", "$demand", "$request", "$geometry", "$lower", "$upper", "$hours", "$minLon", "$minLat", "$maxLon", "$maxLat" })
                insert.Parameters.Add(new SqliteParameter(name, DBNull.Value));
            foreach (var item in airspaces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                insert.Parameters["$country"].Value = country;
                insert.Parameters["$id"].Value = item.Id;
                insert.Parameters["$name"].Value = item.Name;
                insert.Parameters["$type"].Value = (object?)item.Type ?? DBNull.Value;
                insert.Parameters["$class"].Value = (object?)item.IcaoClass ?? DBNull.Value;
                insert.Parameters["$notam"].Value = item.ByNotam.HasValue ? (item.ByNotam.Value ? 1 : 0) : DBNull.Value;
                insert.Parameters["$demand"].Value = item.OnDemand.HasValue ? (item.OnDemand.Value ? 1 : 0) : DBNull.Value;
                insert.Parameters["$request"].Value = item.OnRequest.HasValue ? (item.OnRequest.Value ? 1 : 0) : DBNull.Value;
                insert.Parameters["$geometry"].Value = item.Geometry;
                insert.Parameters["$lower"].Value = (object?)item.LowerLimit ?? DBNull.Value;
                insert.Parameters["$upper"].Value = (object?)item.UpperLimit ?? DBNull.Value;
                insert.Parameters["$hours"].Value = (object?)item.Hours ?? DBNull.Value;
                insert.Parameters["$minLon"].Value = item.MinLongitude;
                insert.Parameters["$minLat"].Value = item.MinLatitude;
                insert.Parameters["$maxLon"].Value = item.MaxLongitude;
                insert.Parameters["$maxLat"].Value = item.MaxLatitude;
                insert.ExecuteNonQuery();
            }
        }
        using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = """
                INSERT INTO oaip_imports (country, imported_utc, source) VALUES ($country, $time, 'OpenAIP Core API')
                ON CONFLICT(country) DO UPDATE SET imported_utc=excluded.imported_utc, source=excluded.source
                """;
            meta.Parameters.AddWithValue("$country", country);
            meta.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            meta.ExecuteNonQuery();
        }
        transaction.Commit();
        return airspaces.Count;
    }

    private static Airspace Parse(JsonElement item)
    {
        var geometry = item.GetProperty("geometry");
        if (geometry.GetProperty("type").GetString() != "Polygon")
            throw new InvalidDataException("Unsupported OpenAIP airspace geometry.");
        var bounds = new Bounds();
        foreach (var ring in geometry.GetProperty("coordinates").EnumerateArray())
            foreach (var point in ring.EnumerateArray())
            {
                if (point.GetArrayLength() < 2) throw new InvalidDataException("Invalid airspace coordinate.");
                var lon = point[0].GetDouble();
                var lat = point[1].GetDouble();
                if (!double.IsFinite(lon) || !double.IsFinite(lat) || lon is < -180 or > 180 || lat is < -90 or > 90)
                    throw new InvalidDataException("Invalid airspace coordinate.");
                bounds.Include(lon, lat);
            }
        if (!bounds.HasPoints) throw new InvalidDataException("Airspace has no coordinates.");
        var id = item.GetProperty("_id").GetString();
        var name = item.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("Airspace ID or name is missing.");
        return new Airspace(id, name, OptionalInt(item, "type"), OptionalInt(item, "icaoClass"),
            OptionalBool(item, "byNotam"), OptionalBool(item, "onDemand"), OptionalBool(item, "onRequest"),
            geometry.GetRawText(), OptionalJson(item, "lowerLimit"), OptionalJson(item, "upperLimit"),
            OptionalJson(item, "hoursOfOperation"), bounds.MinLon, bounds.MinLat, bounds.MaxLon, bounds.MaxLat);
    }

    private static int? OptionalInt(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    private static bool? OptionalBool(JsonElement item, string key) => item.TryGetProperty(key, out var value) ? value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    } : null;
    private static string? OptionalJson(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetRawText() : null;

    private sealed class Bounds
    {
        public double MinLon { get; private set; } = double.PositiveInfinity;
        public double MinLat { get; private set; } = double.PositiveInfinity;
        public double MaxLon { get; private set; } = double.NegativeInfinity;
        public double MaxLat { get; private set; } = double.NegativeInfinity;
        public bool HasPoints => double.IsFinite(MinLon);

        public void Include(double lon, double lat)
        {
            MinLon = Math.Min(MinLon, lon);
            MinLat = Math.Min(MinLat, lat);
            MaxLon = Math.Max(MaxLon, lon);
            MaxLat = Math.Max(MaxLat, lat);
        }
    }
}
