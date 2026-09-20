namespace OpenCareer.Domain.Flights;

public static class FlightSessionEngine
{
    public static FlightSession Advance(
        FlightSession current,
        FlightSessionAdvance update)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(update);

        update.Validate(current.UpdatedAt);

        if (current.IsTerminal)
            return current;

        if (update.CancelRequested)
        {
            return current with
            {
                Status = FlightSessionStatus.Cancelled,
                OperationState = FlightOperationState.Cancelled,
                UpdatedAt = update.Evidence.Timestamp
            };
        }

        FlightTrackingSnapshot previousTracking =
            current.Tracking;

        FlightTrackingSnapshot nextTracking =
            FlightTrackingStateMachine.Advance(
                previousTracking,
                update.Evidence);

        FlightTimeLedger ledger =
            update.TimeInterval is null
                ? current.TimeLedger
                : current.TimeLedger.Add(update.TimeInterval);

        FlightSessionStatus status =
            ResolveStatus(nextTracking);

        FlightOperationState operationState =
            ResolveOperationState(
                current.OperationState,
                nextTracking);

        FlightSessionMilestones milestones =
            UpdateMilestones(
                current.Milestones,
                previousTracking,
                nextTracking,
                update.Evidence.Timestamp,
                update.Evidence.InitialClimbConfirmed,
                update.Evidence.MissionFlightProgressConfirmed,
                update.ShutdownConfirmed);

        if (update.ShutdownConfirmed
            && nextTracking.State == FlightTrackingState.Parked)
        {
            operationState = FlightOperationState.Shutdown;
        }

        if (nextTracking.State == FlightTrackingState.Complete)
        {
            operationState = FlightOperationState.Complete;
            status = FlightSessionStatus.Completed;
        }

        return current with
        {
            UpdatedAt = update.Evidence.Timestamp,
            Status = status,
            OperationState = operationState,
            Tracking = nextTracking,
            TimeLedger = ledger,
            Milestones = milestones,
            ContinuityAnchor =
                update.ContinuityAnchor
                ?? current.ContinuityAnchor
        };
    }

    private static FlightSessionStatus ResolveStatus(
        FlightTrackingSnapshot tracking) =>
        tracking.State switch
        {
            FlightTrackingState.Suspended =>
                FlightSessionStatus.Suspended,

            FlightTrackingState.Interrupted =>
                FlightSessionStatus.Interrupted,

            FlightTrackingState.Complete =>
                FlightSessionStatus.Completed,

            _ =>
                FlightSessionStatus.Active
        };

    private static FlightOperationState ResolveOperationState(
        FlightOperationState current,
        FlightTrackingSnapshot tracking) =>
        tracking.State switch
        {
            FlightTrackingState.Observing =>
                current,

            FlightTrackingState.Preflight =>
                FlightOperationState.ReadyForStart,

            FlightTrackingState.EngineStart =>
                FlightOperationState.EngineStart,

            FlightTrackingState.TaxiOut =>
                FlightOperationState.TaxiOut,

            FlightTrackingState.TakeoffRoll =>
                FlightOperationState.DepartureReady,

            FlightTrackingState.Airborne =>
                FlightOperationState.Airborne,

            FlightTrackingState.Approach =>
                FlightOperationState.Airborne,

            FlightTrackingState.LandingEpisode =>
                FlightOperationState.Landed,

            FlightTrackingState.TaxiIn =>
                FlightOperationState.TaxiIn,

            FlightTrackingState.Parked =>
                FlightOperationState.Parked,

            FlightTrackingState.Suspended =>
                current,

            FlightTrackingState.Interrupted =>
                current,

            FlightTrackingState.Complete =>
                FlightOperationState.Complete,

            _ =>
                current
        };

    private static FlightSessionMilestones UpdateMilestones(
        FlightSessionMilestones current,
        FlightTrackingSnapshot previous,
        FlightTrackingSnapshot next,
        DateTimeOffset timestamp,
        bool initialClimbConfirmed,
        bool missionFlightProgressConfirmed,
        bool shutdownConfirmed)
    {
        FlightSessionMilestones milestones =
            current;

        if (Entered(
                previous,
                next,
                FlightTrackingState.Preflight))
        {
            milestones =
                milestones with
                {
                    AircraftReadyAt =
                        milestones.AircraftReadyAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.EngineStart))
        {
            milestones =
                milestones with
                {
                    EngineStartAt =
                        milestones.EngineStartAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.TaxiOut))
        {
            milestones =
                milestones with
                {
                    TaxiOutAt =
                        milestones.TaxiOutAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.TakeoffRoll))
        {
            milestones =
                milestones with
                {
                    TakeoffRollAt =
                        milestones.TakeoffRollAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.Airborne)
            && next.TakeoffCount > previous.TakeoffCount)
        {
            milestones =
                milestones with
                {
                    TakeoffAt =
                        milestones.TakeoffAt
                        ?? timestamp
                };
        }

        if (current.TakeoffAt is not null
            && current.InitialClimbAt is null
            && next.State == FlightTrackingState.Airborne
            && initialClimbConfirmed)
        {
            milestones =
                milestones with
                {
                    InitialClimbAt = timestamp
                };
        }

        if (milestones.InitialClimbAt is not null
            && milestones.MissionFlightProgressAt is null
            && next.State == FlightTrackingState.Airborne
            && missionFlightProgressConfirmed)
        {
            milestones =
                milestones with
                {
                    MissionFlightProgressAt = timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.Approach))
        {
            milestones =
                milestones with
                {
                    ApproachAt =
                        milestones.ApproachAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.LandingEpisode))
        {
            milestones =
                milestones with
                {
                    FirstTouchdownAt =
                        milestones.FirstTouchdownAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.TaxiIn))
        {
            milestones =
                milestones with
                {
                    LandingAt =
                        milestones.LandingAt
                        ?? timestamp,
                    TaxiInAt =
                        milestones.TaxiInAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.Parked))
        {
            milestones =
                milestones with
                {
                    ParkedAt =
                        milestones.ParkedAt
                        ?? timestamp
                };
        }

        if (shutdownConfirmed
            && next.State == FlightTrackingState.Parked)
        {
            milestones =
                milestones with
                {
                    ShutdownAt =
                        milestones.ShutdownAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.Complete))
        {
            milestones =
                milestones with
                {
                    CompletedAt =
                        milestones.CompletedAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.Interrupted))
        {
            milestones =
                milestones with
                {
                    InterruptedAt =
                        milestones.InterruptedAt
                        ?? timestamp
                };
        }

        return milestones;
    }

    private static bool Entered(
        FlightTrackingSnapshot previous,
        FlightTrackingSnapshot next,
        FlightTrackingState state) =>
        previous.State != state
        && next.State == state;
}
