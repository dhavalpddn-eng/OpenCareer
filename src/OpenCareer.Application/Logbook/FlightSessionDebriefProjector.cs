using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Logbook;

public sealed record FlightSessionDebriefContext(
    LogbookEntryKind EntryKind,
    AircraftDebrief Aircraft,
    string? ActualDeparture,
    string? ActualArrival,
    string? DiversionLocation,
    PayloadDebrief Payload,
    FlightSafetyOutcome SafetyOutcome,
    MissionOutcome MissionOutcome,
    FlightSettlementRecord Settlement,
    IReadOnlyList<FlightDebriefEvent>? Events = null,
    bool PositionJumpObserved = false);

public static class FlightSessionDebriefProjector
{
    public static FlightDebrief Create(
        FlightSession session,
        FlightSessionDebriefContext context)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Aircraft);
        ArgumentNullException.ThrowIfNull(context.Payload);
        ArgumentNullException.ThrowIfNull(context.Settlement);

        if (!session.IsTerminal)
        {
            throw new InvalidOperationException(
                "A logbook debrief can only be projected from a terminal FlightSession.");
        }

        DateTimeOffset endedAt =
            session.Milestones.CompletedAt
            ?? session.Milestones.InterruptedAt
            ?? session.UpdatedAt;

        FlightSessionStatistics statistics =
            session.EffectiveStatistics;

        FlightRouteDebrief route =
            new(
                session.Plan?.PlannedOrigin,
                session.Plan?.PlannedDestination,
                context.ActualDeparture,
                context.ActualArrival,
                context.DiversionLocation,
                statistics.DistanceNauticalMiles);

        FlightTrackPoint[] track =
            statistics.RouteTrack
                .Where(point =>
                    point.Timestamp >= session.CreatedAt
                    && point.Timestamp <= endedAt)
                .Select(point =>
                    new FlightTrackPoint(
                        point.Timestamp,
                        point.LatitudeDegrees,
                        point.LongitudeDegrees,
                        point.AltitudeMslFeet))
                .ToArray();

        FlightSessionLandingEpisode[] sessionLandings =
            session.EffectiveLandingEpisodes
                .OrderBy(item => item.EpisodeNumber)
                .ToArray();

        LandingDebrief[] landings =
            sessionLandings
                .Select(MapLanding)
                .ToArray();

        int[] landingEpisodes =
            sessionLandings
                .Select(item => item.EpisodeNumber)
                .ToArray();

        var leg =
            new FlightLegDebrief(
                session.SessionId,
                Sequence: 1,
                session.CreatedAt,
                endedAt,
                route,
                session.TimeLedger,
                track,
                landingEpisodes);

        var fuel =
            new FlightFuelDebrief(
                statistics.StartFuelPounds,
                statistics.LastFuelPounds,
                statistics.StartFuelPounds is null
                    ? null
                    : statistics.FuelBurnedPounds,
                statistics.StartFuelPounds is null
                    ? EvidenceQuality.Unavailable
                    : EvidenceQuality.DerivedHighConfidence);

        var assistance =
            new FlightAssistanceDebrief(
                PauseObserved:
                    session.TimeLedger.PausedWallTime > TimeSpan.Zero,
                TimeAccelerationObserved:
                    session.TimeLedger.AcceleratedWallTime > TimeSpan.Zero,
                SlewObserved:
                    session.TimeLedger.SlewWallTime > TimeSpan.Zero,
                PositionJumpObserved:
                    context.PositionJumpObserved,
                RouteEvidenceCompromised:
                    context.PositionJumpObserved
                    || session.Status
                        == FlightSessionStatus.Interrupted);

        var draft =
            new FlightDebriefDraft(
                DebriefId:
                    session.SessionId,
                SessionId:
                    session.SessionId,
                ContractId:
                    session.ContractId,
                EntryKind:
                    context.EntryKind,
                StartedAt:
                    session.CreatedAt,
                EndedAt:
                    endedAt,
                Route:
                    route,
                Aircraft:
                    context.Aircraft,
                Time:
                    session.TimeLedger,
                Tracking:
                    session.Tracking,
                Legs:
                    [leg],
                Fuel:
                    fuel,
                Payload:
                    context.Payload,
                Assistance:
                    assistance,
                SafetyOutcome:
                    context.SafetyOutcome,
                MissionOutcome:
                    context.MissionOutcome,
                Landings:
                    landings,
                Events:
                    context.Events
                    ?? Array.Empty<FlightDebriefEvent>(),
                Settlement:
                    context.Settlement);

        return FlightDebriefFactory.Create(draft);
    }

    private static LandingDebrief MapLanding(
        FlightSessionLandingEpisode episode)
    {
        episode.Validate();

        LandingOperationType operationType =
            episode.Kind switch
            {
                FlightSessionLandingKind.TouchAndGo =>
                    LandingOperationType.TouchAndGo,

                FlightSessionLandingKind.FullStop =>
                    LandingOperationType.FullStop,

                _ =>
                    LandingOperationType.Unknown
            };

        return new LandingDebrief(
            episode.EpisodeNumber,
            episode.TouchdownAt,
            operationType,
            episode.BounceCount,
            VerticalSpeedFeetPerMinute: null,
            TouchdownG: null,
            IndicatedAirspeedKnots: null,
            PitchDegrees: null,
            BankDegrees: null,
            HardLanding: null,
            EvidenceQuality:
                EvidenceQuality.DerivedHighConfidence);
    }
}
