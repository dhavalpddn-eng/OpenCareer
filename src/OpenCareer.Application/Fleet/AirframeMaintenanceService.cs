namespace OpenCareer.Application.Fleet;

/// <summary>One action authority. SQLite owns the atomic condition/event commit.</summary>
public sealed class AirframeMaintenanceService(IAirframeStore airframes, IAirframeMaintenanceStore maintenance)
{
    public async Task<AirframeRepairResult> RepairDiscreteDamageAsync(
        AirframeRepairRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var retained = await maintenance.FindMaintenanceActionAsync(request.MaintenanceActionId, cancellationToken).ConfigureAwait(false);
        if (retained is not null) return retained.Replay(request);

        var current = await airframes.FindAsync(request.AirframeId, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(AirframeRepairStatus.NotFound, null, null, false);
        try
        {
            request.ValidateBefore(current);
            if (!current.Condition.HasDamage)
                return new(AirframeRepairStatus.NoRepairRequired, current, null, false);
            return await maintenance.RepairDiscreteDamageAsync(request, current, cancellationToken).ConfigureAwait(false);
        }
        catch (AirframeConcurrencyException)
        {
            // A concurrent identical action may commit between the initial lookup and condition read.
            // One bounded lookup resolves that replay; a genuinely stale distinct action still fails.
            retained = await maintenance.FindMaintenanceActionAsync(request.MaintenanceActionId, cancellationToken).ConfigureAwait(false);
            if (retained is not null) return retained.Replay(request);
            throw;
        }
    }
}
