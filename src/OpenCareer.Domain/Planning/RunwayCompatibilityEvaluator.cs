using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;

namespace OpenCareer.Domain.Planning;

public enum DispatchFeasibilityStatus
{
    Feasible = 0,
    Infeasible = 1,
    InsufficientData = 2
}

public enum DispatchEndpoint
{
    Origin = 0,
    Destination = 1
}

public enum DispatchFeasibilityReason
{
    AircraftNotFound = 0,
    AircraftCapabilityDataIncomplete,
    AircraftRunwayPerformanceUnknown,
    AirportNotFound,
    NoRunwayData,
    AircraftTakeoffLengthUnknown,
    AircraftLandingLengthUnknown,
    AircraftRunwayWidthUnknown,
    AircraftSurfaceSupportUnknown,
    RunwayClosed,
    RunwayLengthUnknown,
    RunwayWidthUnknown,
    RunwaySurfaceUnknown,
    RunwayTooShort,
    RunwayTooNarrow,
    RunwaySurfaceUnsupported
}

public sealed record DispatchFeasibilityIssue(
    DispatchFeasibilityReason Reason,
    DispatchEndpoint? Endpoint = null,
    string? AirportIcao = null,
    string? RunwayIdentifier = null,
    double? RequiredFeet = null,
    double? AvailableFeet = null,
    AircraftRegistryField? AircraftField = null);

public sealed record DispatchFeasibilityResult
{
    private DispatchFeasibilityResult(
        DispatchFeasibilityStatus status,
        string? originRunwayIdentifier,
        string? destinationRunwayIdentifier,
        IReadOnlyList<DispatchFeasibilityIssue> issues)
    {
        Status = status;
        OriginRunwayIdentifier = originRunwayIdentifier;
        DestinationRunwayIdentifier = destinationRunwayIdentifier;
        Issues = issues;
    }

    public DispatchFeasibilityStatus Status { get; }
    public string? OriginRunwayIdentifier { get; }
    public string? DestinationRunwayIdentifier { get; }
    public IReadOnlyList<DispatchFeasibilityIssue> Issues { get; }

    public static DispatchFeasibilityResult Create(
        DispatchFeasibilityStatus status,
        string? originRunwayIdentifier,
        string? destinationRunwayIdentifier,
        IEnumerable<DispatchFeasibilityIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        DispatchFeasibilityIssue[] orderedIssues = issues
            .Distinct()
            .OrderBy(static issue => issue.Endpoint.HasValue ? (int)issue.Endpoint.Value : -1)
            .ThenBy(static issue => issue.AirportIcao, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static issue => issue.RunwayIdentifier, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static issue => issue.Reason)
            .ThenBy(static issue => issue.AircraftField)
            .ThenBy(static issue => issue.RequiredFeet)
            .ThenBy(static issue => issue.AvailableFeet)
            .ToArray();

        if (status == DispatchFeasibilityStatus.Feasible && orderedIssues.Length != 0)
            throw new ArgumentException("A feasible result cannot contain blocking issues.", nameof(issues));

        if (status != DispatchFeasibilityStatus.Feasible && orderedIssues.Length == 0)
            throw new ArgumentException("A non-feasible result requires at least one issue.", nameof(issues));

        return new(status, originRunwayIdentifier, destinationRunwayIdentifier, orderedIssues);
    }
}

/// <summary>
/// Pure baseline runway compatibility. Licensing, ratings, career level,
/// authorization, weather and operation-specific weight performance are separate
/// dispatch gates and cannot override a physical incompatibility reported here.
/// </summary>
public static class RunwayCompatibilityEvaluator
{
    public static DispatchFeasibilityResult Evaluate(
        AircraftRegistryRecord aircraft,
        AirportRecord origin,
        AirportRecord destination)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        aircraft.Validate();
        origin.Validate();
        destination.Validate();

        if (aircraft.RunwayPerformance is null)
        {
            return DispatchFeasibilityResult.Create(
                DispatchFeasibilityStatus.InsufficientData,
                null,
                null,
                [new(DispatchFeasibilityReason.AircraftRunwayPerformanceUnknown)]);
        }

        EndpointEvaluation originResult = EvaluateEndpoint(
            aircraft.RunwayPerformance,
            origin,
            DispatchEndpoint.Origin);

        EndpointEvaluation destinationResult = EvaluateEndpoint(
            aircraft.RunwayPerformance,
            destination,
            DispatchEndpoint.Destination);

        DispatchFeasibilityStatus status =
            originResult.Status == DispatchFeasibilityStatus.Infeasible
            || destinationResult.Status == DispatchFeasibilityStatus.Infeasible
                ? DispatchFeasibilityStatus.Infeasible
                : originResult.Status == DispatchFeasibilityStatus.InsufficientData
                  || destinationResult.Status == DispatchFeasibilityStatus.InsufficientData
                    ? DispatchFeasibilityStatus.InsufficientData
                    : DispatchFeasibilityStatus.Feasible;

        return DispatchFeasibilityResult.Create(
            status,
            originResult.SelectedRunwayIdentifier,
            destinationResult.SelectedRunwayIdentifier,
            originResult.Issues.Concat(destinationResult.Issues));
    }

    private static EndpointEvaluation EvaluateEndpoint(
        AircraftRunwayPerformanceProfile performance,
        AirportRecord airport,
        DispatchEndpoint endpoint)
    {
        if (airport.Runways.Count == 0)
        {
            return new(
                DispatchFeasibilityStatus.InsufficientData,
                null,
                [new(
                    DispatchFeasibilityReason.NoRunwayData,
                    endpoint,
                    airport.Icao)]);
        }

        CandidateEvaluation[] candidates = airport.Runways
            .OrderBy(static runway => runway.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(runway => EvaluateRunway(performance, airport.Icao, runway, endpoint))
            .ToArray();

        CandidateEvaluation[] feasible = candidates
            .Where(static candidate => candidate.Status == DispatchFeasibilityStatus.Feasible)
            .ToArray();

        if (feasible.Length > 0)
        {
            RunwayRecord selected = feasible
                .Select(static candidate => candidate.Runway)
                .OrderByDescending(static runway => runway.UsableLengthFeet ?? 0)
                .ThenByDescending(static runway => runway.WidthFeet ?? 0)
                .ThenBy(static runway => runway.Identifier, StringComparer.OrdinalIgnoreCase)
                .First();

            return new(
                DispatchFeasibilityStatus.Feasible,
                selected.Identifier,
                Array.Empty<DispatchFeasibilityIssue>());
        }

        DispatchFeasibilityStatus status =
            candidates.Any(static candidate => candidate.Status == DispatchFeasibilityStatus.InsufficientData)
                ? DispatchFeasibilityStatus.InsufficientData
                : DispatchFeasibilityStatus.Infeasible;

        return new(
            status,
            null,
            candidates.SelectMany(static candidate => candidate.Issues).ToArray());
    }

    private static CandidateEvaluation EvaluateRunway(
        AircraftRunwayPerformanceProfile performance,
        string airportIcao,
        RunwayRecord runway,
        DispatchEndpoint endpoint)
    {
        if (runway.IsClosed)
        {
            return CandidateEvaluation.Infeasible(
                runway,
                new(
                    DispatchFeasibilityReason.RunwayClosed,
                    endpoint,
                    airportIcao,
                    runway.Identifier));
        }

        var definiteFailures = new List<DispatchFeasibilityIssue>();
        var unknowns = new List<DispatchFeasibilityIssue>();

        double? requiredLength = endpoint == DispatchEndpoint.Origin
            ? performance.MinimumTakeoffRunwayFeet
            : performance.MinimumLandingRunwayFeet;

        if (requiredLength is null)
        {
            unknowns.Add(new(
                endpoint == DispatchEndpoint.Origin
                    ? DispatchFeasibilityReason.AircraftTakeoffLengthUnknown
                    : DispatchFeasibilityReason.AircraftLandingLengthUnknown,
                endpoint,
                airportIcao));
        }
        else if (runway.UsableLengthFeet is null)
        {
            unknowns.Add(new(
                DispatchFeasibilityReason.RunwayLengthUnknown,
                endpoint,
                airportIcao,
                runway.Identifier,
                requiredLength));
        }
        else if (runway.UsableLengthFeet < requiredLength)
        {
            definiteFailures.Add(new(
                DispatchFeasibilityReason.RunwayTooShort,
                endpoint,
                airportIcao,
                runway.Identifier,
                requiredLength,
                runway.UsableLengthFeet));
        }

        if (performance.MinimumRunwayWidthFeet is null)
        {
            unknowns.Add(new(
                DispatchFeasibilityReason.AircraftRunwayWidthUnknown,
                endpoint,
                airportIcao));
        }
        else if (runway.WidthFeet is null)
        {
            unknowns.Add(new(
                DispatchFeasibilityReason.RunwayWidthUnknown,
                endpoint,
                airportIcao,
                runway.Identifier,
                performance.MinimumRunwayWidthFeet));
        }
        else if (runway.WidthFeet < performance.MinimumRunwayWidthFeet)
        {
            definiteFailures.Add(new(
                DispatchFeasibilityReason.RunwayTooNarrow,
                endpoint,
                airportIcao,
                runway.Identifier,
                performance.MinimumRunwayWidthFeet,
                runway.WidthFeet));
        }

        if (performance.SupportedSurfaces is null)
        {
            unknowns.Add(new(
                DispatchFeasibilityReason.AircraftSurfaceSupportUnknown,
                endpoint,
                airportIcao));
        }
        else if (MapSurface(runway.Surface) is not { } surface)
        {
            unknowns.Add(new(
                DispatchFeasibilityReason.RunwaySurfaceUnknown,
                endpoint,
                airportIcao,
                runway.Identifier));
        }
        else if ((performance.SupportedSurfaces.Value & surface) == 0)
        {
            definiteFailures.Add(new(
                DispatchFeasibilityReason.RunwaySurfaceUnsupported,
                endpoint,
                airportIcao,
                runway.Identifier));
        }

        if (definiteFailures.Count > 0)
            return new(DispatchFeasibilityStatus.Infeasible, runway, definiteFailures);

        if (unknowns.Count > 0)
            return new(DispatchFeasibilityStatus.InsufficientData, runway, unknowns);

        return new(
            DispatchFeasibilityStatus.Feasible,
            runway,
            Array.Empty<DispatchFeasibilityIssue>());
    }

    private static RunwaySurfaceSupport? MapSurface(RunwaySurface surface) =>
        surface switch
        {
            RunwaySurface.Unknown => null,
            RunwaySurface.Asphalt => RunwaySurfaceSupport.Asphalt,
            RunwaySurface.Concrete => RunwaySurfaceSupport.Concrete,
            RunwaySurface.Grass => RunwaySurfaceSupport.Grass,
            RunwaySurface.Gravel => RunwaySurfaceSupport.Gravel,
            RunwaySurface.Dirt => RunwaySurfaceSupport.Dirt,
            RunwaySurface.Water => RunwaySurfaceSupport.Water,
            RunwaySurface.SnowOrIce => RunwaySurfaceSupport.SnowOrIce,
            RunwaySurface.Other => RunwaySurfaceSupport.Other,
            _ => null
        };

    private sealed record EndpointEvaluation(
        DispatchFeasibilityStatus Status,
        string? SelectedRunwayIdentifier,
        IReadOnlyList<DispatchFeasibilityIssue> Issues);

    private sealed record CandidateEvaluation(
        DispatchFeasibilityStatus Status,
        RunwayRecord Runway,
        IReadOnlyList<DispatchFeasibilityIssue> Issues)
    {
        public static CandidateEvaluation Infeasible(
            RunwayRecord runway,
            DispatchFeasibilityIssue issue) =>
            new(
                DispatchFeasibilityStatus.Infeasible,
                runway,
                [issue]);
    }
}
