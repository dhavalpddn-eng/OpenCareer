using OpenCareer.Application.Planning;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed record StandardPointToPointMissionPolicy(
    double DestinationRadiusNauticalMiles)
{
    public static StandardPointToPointMissionPolicy Default { get; } =
        new(2.0);

    public void Validate()
    {
        if (!double.IsFinite(DestinationRadiusNauticalMiles)
            || DestinationRadiusNauticalMiles <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DestinationRadiusNauticalMiles));
        }
    }
}

public sealed class StandardPointToPointMissionCompletionSource
    : ICareerJobMissionCompletionSource
{
    private readonly IAirportDataSource _airportData;
    private readonly StandardPointToPointMissionPolicy _policy;

    public StandardPointToPointMissionCompletionSource(
        IAirportDataSource airportData,
        StandardPointToPointMissionPolicy policy)
    {
        _airportData =
            airportData
            ?? throw new ArgumentNullException(nameof(airportData));
        _policy =
            policy
            ?? throw new ArgumentNullException(nameof(policy));

        _policy.Validate();
    }

    public async Task<CareerJobMissionCompletionEvidence?> ReadAsync(
        JobContract contract,
        FlightSession flightSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(flightSession);

        contract.Validate();

        if (contract.Kind
            is not ContractKind.Ferry
                and not ContractKind.Reposition)
        {
            return null;
        }

        if (contract.Status
            != ContractStatus.InProgress)
        {
            return null;
        }

        if (flightSession.ContractId
                != contract.ContractId
            || flightSession.Status
                != FlightSessionStatus.Active
            || flightSession.OperationState
                != FlightOperationState.Shutdown
            || flightSession.Tracking.State
                != FlightTrackingState.Parked
            || flightSession.Tracking.CrashReported
            || flightSession.Tracking.TakeoffCount <= 0
            || flightSession.Tracking.LandingEpisodeCount <= 0
            || flightSession.Milestones.ParkedAt is null
            || flightSession.Milestones.ShutdownAt is null)
        {
            return null;
        }

        FlightContinuityAnchor? anchor =
            flightSession.ContinuityAnchor;

        if (anchor is null)
            return null;

        anchor.Validate();

        if (!anchor.OnGround
            || anchor.Timestamp
                < flightSession.Milestones.ParkedAt.Value
            || anchor.Timestamp
                < flightSession.Milestones.ShutdownAt.Value)
        {
            return CreateUnverified(
                contract,
                flightSession,
                anchor.Timestamp);
        }

        AirportRecord? destination =
            await _airportData
                .FindAirportAsync(
                    contract.DestinationIcao,
                    cancellationToken)
                .ConfigureAwait(false);

        if (destination is null)
            return null;

        destination.Validate();

        if (!destination.HasPosition)
            return null;

        double distanceNauticalMiles =
            GreatCircleNauticalMiles(
                anchor.LatitudeDegrees,
                anchor.LongitudeDegrees,
                destination.LatitudeDegrees!.Value,
                destination.LongitudeDegrees!.Value);

        if (distanceNauticalMiles
            > _policy.DestinationRadiusNauticalMiles)
        {
            return CreateUnverified(
                contract,
                flightSession,
                anchor.Timestamp);
        }

        string actualArrival =
            destination.Icao
                .Trim()
                .ToUpperInvariant();

        return new(
            contract.ContractId,
            flightSession.SessionId,
            anchor.Timestamp,
            MissionConditionsVerified:
                true,
            ActualDeparture:
                null,
            ActualArrival:
                actualArrival,
            DiversionLocation:
                null,
            Payload:
                NoMissionPayload(
                    "Arrived at contracted destination"),
            SafetyOutcome:
                FlightSafetyOutcome.CompletedNormally,
            MissionOutcome:
                MissionOutcome.Succeeded);
    }

    private static CareerJobMissionCompletionEvidence CreateUnverified(
        JobContract contract,
        FlightSession flightSession,
        DateTimeOffset observedAt) =>
        new(
            contract.ContractId,
            flightSession.SessionId,
            observedAt,
            MissionConditionsVerified:
                false,
            ActualDeparture:
                null,
            ActualArrival:
                null,
            DiversionLocation:
                null,
            Payload:
                NoMissionPayload(
                    "Contracted destination not verified"),
            SafetyOutcome:
                FlightSafetyOutcome.CompletedNormally,
            MissionOutcome:
                MissionOutcome.Failed);

    private static PayloadDebrief NoMissionPayload(
        string outcome) =>
        new(
            PassengerCount:
                null,
            CargoMassPounds:
                null,
            CargoDescription:
                null,
            Outcome:
                outcome,
            EvidenceQuality:
                EvidenceQuality.DerivedHighConfidence);

    private static double GreatCircleNauticalMiles(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double EarthRadiusNauticalMiles =
            3_440.065;

        double lat1 =
            DegreesToRadians(latitude1);

        double lat2 =
            DegreesToRadians(latitude2);

        double deltaLat =
            DegreesToRadians(latitude2 - latitude1);

        double deltaLon =
            DegreesToRadians(longitude2 - longitude1);

        double sinLat =
            Math.Sin(deltaLat / 2);

        double sinLon =
            Math.Sin(deltaLon / 2);

        double a =
            sinLat * sinLat
            + Math.Cos(lat1)
                * Math.Cos(lat2)
                * sinLon
                * sinLon;

        double c =
            2
            * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(Math.Max(0, 1 - a)));

        return EarthRadiusNauticalMiles * c;
    }

    private static double DegreesToRadians(
        double degrees) =>
        degrees * Math.PI / 180;
}
