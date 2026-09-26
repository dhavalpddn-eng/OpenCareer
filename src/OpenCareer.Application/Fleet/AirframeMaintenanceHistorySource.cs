using System.Collections.Immutable;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Fleet;

/// <summary>Condition description only; AvailableForDispatch does not replace other dispatch gates.</summary>
public enum AirframeServiceability { AvailableForDispatch, Grounded }
public enum AirframeMaintenanceReadStatus { Available, NotFound }

/// <summary>Projection of the retained application, never a decision made using today's calibration.</summary>
public sealed record AirframeMaintenanceHistoryEntry(FlightAirframeApplication Application)
{
    public Guid SessionId => Application.Consequence.Summary.SessionId;
    public Guid? ContractId => Application.Consequence.Summary.ContractId;
    public AirframeId AirframeId => Application.Consequence.Summary.AirframeId;
    public DateTimeOffset OccurredAt => Application.Consequence.Summary.EndedAt;
    public DateTimeOffset AppliedAt => Application.AppliedAt;
    public FlightSessionStatus TerminalStatus => Application.Consequence.Summary.TerminalStatus;
    public FlightDamageSeverity? Severity => Application.Consequence.Severity;
    public string Rationale => Application.Consequence.Rationale;
    public FlightLandingContactEvidence? StrongestContact => Application.Consequence.StrongestContact;
    public int BounceCount => Application.Consequence.Summary.BounceCount;
    public TimeSpan AirborneTime => Application.Consequence.Summary.AirborneTime;
    public AirframeCondition ConditionBefore => Application.Before.Condition;
    public AirframeCondition ConditionAfter => Application.After.Condition;
    public long BeforeRevision => Application.Before.Revision;
    public long AfterRevision => Application.After.Revision;
    // A revision can advance even when wear is saturated or there was no new usage/damage.
    public bool ConditionChanged => ConditionBefore != ConditionAfter;
}

public sealed record AirframeMaintenanceSnapshot(
    AirframeStoreRecord Current,
    ImmutableList<AirframeMaintenanceHistoryEntry> History,
    FlightAirframeHistoryCursor? Next)
{
    public Airframe Airframe => Current.Airframe;
    public AirframeCondition Condition => Current.Condition;
    public long Revision => Current.Revision;
    public DateTimeOffset SavedAt => Current.SavedAt;
    public AirframeServiceability Serviceability => Condition.RequiresGrounding
        ? AirframeServiceability.Grounded : AirframeServiceability.AvailableForDispatch;
}

public sealed record AirframeMaintenanceReadResult(
    AirframeId AirframeId,
    AirframeMaintenanceReadStatus Status,
    AirframeMaintenanceSnapshot? Snapshot);

/// <summary>Read-only condition/history authority for an explicitly requested physical airframe.</summary>
public sealed class AirframeMaintenanceHistorySource(IAirframeStore airframes, IFlightAirframeConsequenceStore consequences)
{
    public async Task<AirframeMaintenanceReadResult> ReadAsync(
        FlightAirframeHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var current = await airframes.FindAsync(query.AirframeId, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(query.AirframeId, AirframeMaintenanceReadStatus.NotFound, null);
        current.Validate();
        if (current.Airframe.AirframeId != query.AirframeId)
            throw new InvalidDataException("Maintenance snapshot returned a different physical airframe.");

        var page = await consequences.ReadHistoryAsync(query, cancellationToken).ConfigureAwait(false);
        // The stores need not share a transaction. Fail closed rather than combine an older
        // condition with history written concurrently; the caller can request a fresh snapshot.
        if (current != await airframes.FindAsync(query.AirframeId, cancellationToken).ConfigureAwait(false))
            throw new AirframeConcurrencyException("Airframe condition changed during maintenance read; refresh the snapshot.");

        foreach (var application in page.Entries)
        {
            application.Validate();
            if (application.Before.Airframe != current.Airframe
                || application.After.Revision > current.Revision
                || application.After.Revision == current.Revision && application.After != current)
                throw new InvalidDataException("Retained maintenance history does not match the current physical airframe/model/revision.");
        }
        return new(query.AirframeId, AirframeMaintenanceReadStatus.Available,
            new(current, page.Entries.Select(a => new AirframeMaintenanceHistoryEntry(a)).ToImmutableList(), page.Next));
    }
}
