using Microsoft.Data.Sqlite;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Persistence;

public sealed partial class SqliteAirframeStore : IAirframeServiceStateStore
{
    public async Task<AirframeServiceState?> ReadServiceStateAsync(AirframeId airframeId, CancellationToken cancellationToken = default)
    {
        airframeId.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadServiceStateAsync(connection, null, airframeId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AirframeServiceState?> ReadServiceStateAsync(SqliteConnection connection,
        SqliteTransaction? transaction, AirframeId id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT airframe_id, schedule_id, schedule_version, total_airborne_ticks, last_inspection_airborne_ticks,
                next_inspection_airborne_ticks, usage_origin, revision, updated_at_utc_ticks
            FROM airframe_service_state WHERE airframe_id=$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.GetValue(0) is not string || reader.GetValue(1) is not string
            || Enumerable.Range(2, 7).Any(i => reader.GetValue(i) is not long))
            throw new InvalidDataException("Invalid airframe service state column types.");
        if (reader.GetString(0) != id.ToString()) throw new InvalidDataException("Service state physical identity mismatch.");
        var result = new AirframeServiceState(id, reader.GetString(1), checked((int)reader.GetInt64(2)),
            TimeSpan.FromTicks(reader.GetInt64(3)), TimeSpan.FromTicks(reader.GetInt64(4)), TimeSpan.FromTicks(reader.GetInt64(5)),
            (AirframeUsageOrigin)checked((int)reader.GetInt64(6)), reader.GetInt64(7), new(reader.GetInt64(8), TimeSpan.Zero));
        result.Validate();
        return result;
    }

    private static async Task SaveServiceStateAsync(SqliteConnection connection, SqliteTransaction transaction,
        AirframeServiceState state, long? expectedRevision, CancellationToken cancellationToken)
    {
        state.Validate();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedRevision is null ? """
            INSERT INTO airframe_service_state (airframe_id, schedule_id, schedule_version, total_airborne_ticks,
                last_inspection_airborne_ticks, next_inspection_airborne_ticks, usage_origin, revision, updated_at_utc_ticks)
            VALUES ($id, $schedule, $version, $total, $last, $next, $origin, $revision, $updated);
            """ : """
            UPDATE airframe_service_state SET total_airborne_ticks=$total, last_inspection_airborne_ticks=$last,
                next_inspection_airborne_ticks=$next, revision=$revision, updated_at_utc_ticks=$updated
            WHERE airframe_id=$id AND schedule_id=$schedule AND schedule_version=$version
                AND usage_origin=$origin AND revision=$expected;
            """;
        command.Parameters.AddWithValue("$id", state.AirframeId.ToString());
        command.Parameters.AddWithValue("$schedule", state.ScheduleId);
        command.Parameters.AddWithValue("$version", state.ScheduleVersion);
        command.Parameters.AddWithValue("$total", state.TotalTrackedAirborneTime.Ticks);
        command.Parameters.AddWithValue("$last", state.LastInspectionAtTrackedAirborneTime.Ticks);
        command.Parameters.AddWithValue("$next", state.NextInspectionDueAtTrackedAirborneTime.Ticks);
        command.Parameters.AddWithValue("$origin", (int)state.UsageOrigin);
        command.Parameters.AddWithValue("$revision", state.Revision);
        command.Parameters.AddWithValue("$updated", state.UpdatedAt.UtcTicks);
        if (expectedRevision is { } revision) command.Parameters.AddWithValue("$expected", revision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new AirframeConcurrencyException("Airframe service revision changed before save.");
    }
}
