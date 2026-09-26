using Microsoft.Extensions.Logging;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Persistence;

public sealed partial class SqliteAirframeStore
{
    public async Task<AirframeInspectionResult> PerformRoutineInspectionAsync(AirframeInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var retained = await ReadMaintenanceActionAsync(connection, transaction, request.MaintenanceActionId, cancellationToken).ConfigureAwait(false);
        if (retained is not null) return retained.Replay(request);
        var condition = await ReadAsync(connection, transaction, request.AirframeId, cancellationToken).ConfigureAwait(false);
        if (condition is null) return new(AirframeInspectionResultStatus.NotFound, null, null, null, false);
        var service = await ReadServiceStateAsync(connection, transaction, request.AirframeId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Physical airframe has no authoritative service state.");
        request.ValidateBefore(condition, service);
        if (service.InspectionStatus != AirframeInspectionStatus.InspectionDue)
            return new(AirframeInspectionResultStatus.InspectionNotDue, condition, service, null, false);

        request = request with { PerformedAt = request.PerformedAt.ToUniversalTime() };
        var after = service.Inspect(request.PerformedAt);
        var serviceEvent = new AirframeRoutineInspectionEvent(request, AirframeMaintenanceEventKind.RoutineInspection,
            condition, condition, AirframeRoutineInspectionEvent.InspectionRationale, service, after);
        serviceEvent.Validate();
        await SaveServiceStateAsync(connection, transaction, after, service.Revision, cancellationToken).ConfigureAwait(false);
        await InsertMaintenanceEventAsync(connection, transaction, serviceEvent, cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        _logger.LogInformation("Completed routine inspection for airframe {AirframeId}, action {MaintenanceActionId}, schedule {ScheduleId}/{ScheduleVersion}, service revision {BeforeRevision}->{AfterRevision}, next due at {NextDueTicks} tracked airborne ticks; condition unchanged.",
            request.AirframeId, request.MaintenanceActionId, after.ScheduleId, after.ScheduleVersion,
            service.Revision, after.Revision, after.NextInspectionDueAtTrackedAirborneTime.Ticks);
        return new(AirframeInspectionResultStatus.Inspected, condition, after, serviceEvent, true);
    }
}
