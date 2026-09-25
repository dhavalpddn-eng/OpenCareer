using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Fleet;

public sealed record AirframeStoreRecord(
    Airframe Airframe,
    AirframeCondition Condition,
    long Revision,
    DateTimeOffset SavedAt)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Airframe);
        ArgumentNullException.ThrowIfNull(Condition);
        Airframe.AirframeId.Validate();
        if (Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(Revision));
        if (SavedAt == default || SavedAt < Airframe.CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(SavedAt));
    }
}

/// <summary>
/// Authoritative physical-airframe persistence, independent of model-level Fleet reservations.
/// Callers must retain the explicit identity; no lookup by simulator title or automatic creation is offered.
/// </summary>
public interface IAirframeStore
{
    Task<AirframeStoreRecord?> FindAsync(AirframeId airframeId, CancellationToken cancellationToken = default);

    Task<AirframeStoreRecord> CreateAsync(
        Airframe airframe,
        AirframeCondition condition,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Updates condition only. Identity is immutable; a stale/replayed revision fails without writing.</summary>
    Task<AirframeStoreRecord> UpdateConditionAsync(
        Airframe airframe,
        AirframeCondition condition,
        long expectedRevision,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default);
}

public sealed class AirframeConcurrencyException(string message) : InvalidOperationException(message);
