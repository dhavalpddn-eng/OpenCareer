using System.Collections.Immutable;

namespace OpenCareer.Domain.Careers;

/// <summary>Travel history expands local access without moving the home base or stored aircraft.</summary>
public sealed record CareerLocation(
    string HomeAirportIcao,
    string CurrentAirportIcao,
    DateTimeOffset UpdatedAt,
    ImmutableHashSet<string> Connections,
    ImmutableHashSet<Guid> AppliedTravelContracts)
{
    public static CareerLocation Start(string homeAirportIcao, DateTimeOffset time)
    {
        var airport = Normalize(homeAirportIcao);
        return new(airport, airport, time, ImmutableHashSet.Create(airport), ImmutableHashSet<Guid>.Empty);
    }

    public JobContract AcceptLocalJob(JobContract job, ContractDispatchContext context)
    {
        RequireLocal(job.OriginIcao);
        return job.Accept(context);
    }

    public JobContract StartLocalJob(JobContract job, ContractDispatchContext context)
    {
        RequireLocal(job.OriginIcao);
        return job.Start(context);
    }

    public CareerLocation ApplyCompletedTravel(JobContract job)
    {
        job.Validate();
        if (job.Status != ContractStatus.Completed || job.CompletedAt is null)
            throw new InvalidOperationException("Travel requires a completed, verified contract.");
        if (AppliedTravelContracts.Contains(job.ContractId)) return this;
        RequireLocal(job.OriginIcao);
        if (job.CompletedAt < UpdatedAt)
            throw new InvalidOperationException("Travel cannot move career time backwards.");
        var destination = Normalize(job.DestinationIcao);
        return this with
        {
            CurrentAirportIcao = destination,
            UpdatedAt = job.CompletedAt.Value,
            Connections = Connections.Add(destination),
            AppliedTravelContracts = AppliedTravelContracts.Add(job.ContractId)
        };
    }

    // Eligibility only: purchase still requires a local inventory quote and atomic ledger settlement.
    public bool CanRequestStorage(string airportIcao) => Connections.Contains(Normalize(airportIcao));

    private void RequireLocal(string origin)
    {
        if (Normalize(origin) != CurrentAirportIcao)
            throw new InvalidOperationException("Travel to the job origin before accepting or starting local work.");
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var result = value.Trim().ToUpperInvariant();
        if (result.Length != 4 || result.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("A four-letter ICAO airport identifier is required.");
        return result;
    }
}
