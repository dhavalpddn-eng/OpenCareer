namespace OpenCareer.Domain.Careers;

public enum RouteFamiliarity
{
    Untried,
    Discovered,
    Familiar,
    Established,
    Preferred,
    Core
}

/// <summary>
/// Repeated successful service turns a route into a meaningful career network connection.
/// A single flight discovers a route; strong market preference requires repeated service.
/// </summary>
public sealed record RouteExperience(
    string OriginIcao,
    string DestinationIcao,
    int SuccessfulFlights,
    int FailedFlights,
    int OnTimeFlights,
    DateTimeOffset? LastFlownAt = null)
{
    public RouteFamiliarity Familiarity => SuccessfulFlights switch
    {
        >= 25 => RouteFamiliarity.Core,
        >= 12 => RouteFamiliarity.Preferred,
        >= 5 => RouteFamiliarity.Established,
        >= 2 => RouteFamiliarity.Familiar,
        >= 1 => RouteFamiliarity.Discovered,
        _ => RouteFamiliarity.Untried
    };

    public double Strength => Familiarity switch
    {
        RouteFamiliarity.Untried => 0,
        RouteFamiliarity.Discovered => 0.10,
        RouteFamiliarity.Familiar => 0.25,
        RouteFamiliarity.Established => 0.50,
        RouteFamiliarity.Preferred => 0.75,
        RouteFamiliarity.Core => 1.00,
        _ => 0
    };

    public double CompletionRate => SuccessfulFlights + FailedFlights == 0
        ? 1
        : SuccessfulFlights / (double)(SuccessfulFlights + FailedFlights);

    public double OnTimeRate => SuccessfulFlights == 0 ? 1 : OnTimeFlights / (double)SuccessfulFlights;

    public RouteExperience RecordSuccess(DateTimeOffset time, bool onTime)
    {
        Validate();
        if (LastFlownAt is { } previous && time < previous)
            throw new InvalidOperationException("Route history cannot move backwards in time.");
        return this with
        {
            SuccessfulFlights = checked(SuccessfulFlights + 1),
            OnTimeFlights = checked(OnTimeFlights + (onTime ? 1 : 0)),
            LastFlownAt = time
        };
    }

    public RouteExperience RecordFailure(DateTimeOffset time)
    {
        Validate();
        if (LastFlownAt is { } previous && time < previous)
            throw new InvalidOperationException("Route history cannot move backwards in time.");
        return this with
        {
            FailedFlights = checked(FailedFlights + 1),
            LastFlownAt = time
        };
    }

    public void Validate()
    {
        _ = JobMarketIcao.Normalize(OriginIcao);
        _ = JobMarketIcao.Normalize(DestinationIcao);
        if (SuccessfulFlights < 0 || FailedFlights < 0 || OnTimeFlights < 0 || OnTimeFlights > SuccessfulFlights)
            throw new ArgumentOutOfRangeException(nameof(SuccessfulFlights));
    }
}
