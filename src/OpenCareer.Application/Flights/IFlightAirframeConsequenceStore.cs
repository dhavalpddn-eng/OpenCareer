using System.Text.Json.Serialization;
using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed record FlightAirframeApplication(
    [property: JsonRequired] FlightAirframeConsequence Consequence,
    [property: JsonRequired] AirframeStoreRecord Before,
    [property: JsonRequired] AirframeStoreRecord After,
    [property: JsonRequired] DateTimeOffset AppliedAt)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Consequence);
        ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After);
        Consequence.Validate(); Before.Validate(); After.Validate();
        if (Before.Airframe != After.Airframe || Before.Airframe.AirframeId != Consequence.Summary.AirframeId
            || Before.Airframe.CanonicalAircraftId != Consequence.Summary.CanonicalAircraftId
            || After.Condition != Consequence.ApplyTo(Before.Condition)
            || After.Revision != checked(Before.Revision + (Consequence.ApplyCondition ? 1 : 0))
            || AppliedAt < Consequence.Summary.EndedAt || AppliedAt < Before.SavedAt
            || After.SavedAt != (Consequence.ApplyCondition ? AppliedAt : Before.SavedAt))
            throw new InvalidDataException("Airframe consequence application does not match its identity, condition or revision evidence.");
    }
}

public sealed record FlightAirframeApplyResult(FlightAirframeApplication Application, bool WasNewlyApplied);

public interface IFlightAirframeConsequenceStore
{
    Task<FlightAirframeApplication?> FindBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<FlightAirframeApplyResult> ApplyAsync(FlightAirframeConsequence consequence, AirframeStoreRecord expected,
        DateTimeOffset appliedAt, CancellationToken cancellationToken = default);
}
