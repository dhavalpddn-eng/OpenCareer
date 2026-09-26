using System.Collections.Immutable;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Flights;

/// <summary>Exclusive cursor in descending UTC application time / ordinal D-format SessionId order.</summary>
public sealed record FlightAirframeHistoryCursor(DateTimeOffset AppliedAt, Guid SessionId)
{
    public void Validate()
    {
        if (AppliedAt == default || SessionId == Guid.Empty)
            throw new ArgumentException("History cursor requires application time and session identity.");
    }
}

public sealed record FlightAirframeHistoryQuery(
    AirframeId AirframeId,
    int Limit = 100,
    FlightAirframeHistoryCursor? Before = null)
{
    public void Validate()
    {
        AirframeId.Validate();
        if (Limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(Limit));
        Before?.Validate();
    }
}

/// <summary>Immutable retained applications, newest first. Next is null when this read reached the end.</summary>
public sealed record FlightAirframeHistoryPage(
    ImmutableList<FlightAirframeApplication> Entries,
    FlightAirframeHistoryCursor? Next);
