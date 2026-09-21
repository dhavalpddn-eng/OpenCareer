using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Tutorials;

public sealed class FlightSessionTutorialEvidenceSource :
    ITutorialStepEvidenceSource
{
    private readonly FlightSessionCoordinator _flightSessions;

    public FlightSessionTutorialEvidenceSource(
        FlightSessionCoordinator flightSessions)
    {
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
    }

    public TutorialStepEvidenceState GetState(
        TutorialStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        Func<FlightSession, bool>? predicate =
            step.Id switch
            {
                "job-prepare" =>
                    static session =>
                        session.Milestones.AircraftReadyAt is not null,

                "job-engine-start" =>
                    static session =>
                        session.Milestones.EngineStartAt is not null,

                "job-taxi-out" =>
                    static session =>
                        session.Milestones.TaxiOutAt is not null,

                "job-takeoff" =>
                    static session =>
                        session.Milestones.TakeoffAt is not null,

                "job-initial-climb" =>
                    static session =>
                        session.Milestones.InitialClimbAt is not null,

                "job-fly" =>
                    static session =>
                        session.Milestones.MissionFlightProgressAt is not null,

                "job-approach" =>
                    static session =>
                        session.Milestones.MissionFlightProgressAt is { } progressAt
                        && session.Milestones.ApproachAt is { } approachAt
                        && approachAt > progressAt,

                "job-land" =>
                    static session =>
                        session.Milestones.MissionFlightProgressAt is { } progressAt
                        && session.Milestones.ApproachAt is { } approachAt
                        && session.Milestones.LandingAt is { } landingAt
                        && approachAt > progressAt
                        && landingAt > approachAt,

                "job-taxi-in" =>
                    static session =>
                        session.Milestones.MissionFlightProgressAt is { } progressAt
                        && session.Milestones.ApproachAt is { } approachAt
                        && session.Milestones.LandingAt is { } landingAt
                        && session.Milestones.TaxiInProgressAt is { } taxiAt
                        && approachAt > progressAt
                        && landingAt > approachAt
                        && taxiAt > landingAt,

                "job-arrive" =>
                    static session =>
                        session.Milestones.TaxiInProgressAt is { } taxiAt
                        && session.Milestones.ParkedAt is { } parkedAt
                        && session.Milestones.ShutdownAt is { } shutdownAt
                        && parkedAt > taxiAt
                        && shutdownAt > parkedAt,

                "carrier-to-launch" =>
                    static session =>
                        session.Tracking.TakeoffCount > 0,

                "carrier-to-climb" =>
                    static session =>
                        session.Tracking.TakeoffCount > 0
                        && Reached(
                            session,
                            FlightOperationState.Airborne),

                "carrier-land-touchdown" =>
                    static session =>
                        session.Tracking.LandingEpisodeCount > 0,

                "carrier-land-secure" =>
                    static session =>
                        session.Status
                            == FlightSessionStatus.Completed
                        || Reached(
                            session,
                            FlightOperationState.Parked),

                "banner-return" =>
                    static session =>
                        session.Status
                            == FlightSessionStatus.Completed
                        || Reached(
                            session,
                            FlightOperationState.Parked),

                _ =>
                    null
            };

        if (predicate is null)
            return TutorialStepEvidenceState.NotApplicable;

        FlightSession? session =
            _flightSessions.Current;

        if (session is null)
            return TutorialStepEvidenceState.Waiting;

        if (session.Status
            is FlightSessionStatus.Interrupted
                or FlightSessionStatus.Cancelled)
        {
            return TutorialStepEvidenceState.Waiting;
        }

        return predicate(session)
            ? TutorialStepEvidenceState.Satisfied
            : TutorialStepEvidenceState.Waiting;
    }

    private static bool Reached(
        FlightSession session,
        FlightOperationState target)
    {
        if (session.OperationState
            is FlightOperationState.Failed
                or FlightOperationState.Cancelled)
        {
            return false;
        }

        return session.OperationState >= target;
    }
}
