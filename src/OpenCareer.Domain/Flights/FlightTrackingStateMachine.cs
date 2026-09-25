namespace OpenCareer.Domain.Flights;

public static class FlightTrackingStateMachine
{
    public static FlightTrackingSnapshot Advance(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Timestamp < current.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "Flight evidence cannot move backwards in time.");
        }

        if (current.State is FlightTrackingState.Complete or FlightTrackingState.Interrupted)
        {
            return current;
        }

        if (evidence.CrashReported)
        {
            return current with
            {
                State = FlightTrackingState.Interrupted,
                SuspendedFrom = null,
                UpdatedAt = evidence.Timestamp,
                CrashReported = true
            };
        }

        if (!evidence.Connected)
        {
            if (current.State == FlightTrackingState.Suspended)
            {
                return current with { UpdatedAt = evidence.Timestamp };
            }

            return current with
            {
                State = FlightTrackingState.Suspended,
                SuspendedFrom = current.State,
                UpdatedAt = evidence.Timestamp
            };
        }

        if (current.State == FlightTrackingState.Suspended)
        {
            if (!evidence.StableTelemetry)
            {
                return current with { UpdatedAt = evidence.Timestamp };
            }

            if (!evidence.ContinuityPlausible)
            {
                return current with
                {
                    State = FlightTrackingState.Interrupted,
                    SuspendedFrom = null,
                    UpdatedAt = evidence.Timestamp
                };
            }

            return current with
            {
                State = current.SuspendedFrom ?? FlightTrackingState.Observing,
                SuspendedFrom = null,
                UpdatedAt = evidence.Timestamp
            };
        }

        var next = current.State switch
        {
            FlightTrackingState.Observing =>
                evidence.StableTelemetry && evidence.ValidLoadedAircraft
                    ? current with { State = FlightTrackingState.Preflight }
                    : current,

            FlightTrackingState.Preflight =>
                AdvancePreflight(current, evidence),

            FlightTrackingState.EngineStart =>
                AdvanceEngineStart(current, evidence),

            FlightTrackingState.TaxiOut =>
                evidence.TakeoffCandidate
                    ? current with { State = FlightTrackingState.TakeoffRoll }
                    : current,

            FlightTrackingState.TakeoffRoll =>
                AdvanceTakeoffRoll(current, evidence),

            FlightTrackingState.Airborne =>
                AdvanceAirborne(current, evidence),

            FlightTrackingState.Approach =>
                AdvanceApproach(current, evidence),

            FlightTrackingState.LandingEpisode =>
                AdvanceLandingEpisode(current, evidence),

            FlightTrackingState.TaxiIn =>
                evidence.ParkingConfirmed
                    ? current with { State = FlightTrackingState.Parked }
                    : current,

            FlightTrackingState.Parked =>
                evidence.OperationCompleteConfirmed
                    ? current with { State = FlightTrackingState.Complete }
                    : current,

            _ => current
        };

        return next with { UpdatedAt = evidence.Timestamp };
    }

    private static FlightTrackingSnapshot AdvancePreflight(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        if (evidence.AuthorizedAirborneStart && evidence.AirborneConfirmed)
        {
            return current with { State = FlightTrackingState.Airborne };
        }

        if (evidence.AuthorizedRunwayStart && evidence.TakeoffCandidate)
        {
            return current with { State = FlightTrackingState.TakeoffRoll };
        }

        if (evidence.EngineStartObserved)
        {
            return current with { State = FlightTrackingState.EngineStart };
        }

        if (evidence.SelfPoweredMovementForFlight)
        {
            return current with { State = FlightTrackingState.TaxiOut };
        }

        return current;
    }

    private static FlightTrackingSnapshot AdvanceEngineStart(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        if (evidence.AuthorizedRunwayStart && evidence.TakeoffCandidate)
        {
            return current with { State = FlightTrackingState.TakeoffRoll };
        }

        if (evidence.SelfPoweredMovementForFlight)
        {
            return current with { State = FlightTrackingState.TaxiOut };
        }

        return current;
    }

    private static FlightTrackingSnapshot AdvanceTakeoffRoll(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        if (evidence.RejectedTakeoffConfirmed)
        {
            return current with
            {
                State = FlightTrackingState.TaxiOut,
                RejectedTakeoffCount = current.RejectedTakeoffCount + 1
            };
        }

        if (evidence.AirborneConfirmed)
        {
            return current with
            {
                State = FlightTrackingState.Airborne,
                TakeoffCount = current.TakeoffCount + 1
            };
        }

        return current;
    }

    private static FlightTrackingSnapshot AdvanceAirborne(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        if (evidence.TouchdownConfirmed)
        {
            return current with
            {
                State = FlightTrackingState.LandingEpisode,
                LandingEpisodeCount = current.LandingEpisodeCount + 1,
                BounceCount = current.BounceCount + (evidence.BounceRecontact ? 1 : 0)
            };
        }

        if (evidence.ApproachConfirmed)
        {
            return current with { State = FlightTrackingState.Approach };
        }

        return current;
    }

    private static FlightTrackingSnapshot AdvanceApproach(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        if (evidence.GoAroundConfirmed)
        {
            return current with { State = FlightTrackingState.Airborne };
        }

        if (evidence.TouchdownConfirmed)
        {
            return current with
            {
                State = FlightTrackingState.LandingEpisode,
                LandingEpisodeCount = current.LandingEpisodeCount + 1,
                BounceCount = current.BounceCount + (evidence.BounceRecontact ? 1 : 0)
            };
        }

        return current;
    }

    private static FlightTrackingSnapshot AdvanceLandingEpisode(
        FlightTrackingSnapshot current,
        FlightStateEvidence evidence)
    {
        if (evidence.BounceRecontact)
        {
            return current with { BounceCount = current.BounceCount + 1 };
        }

        if (evidence.TouchAndGoConfirmed)
        {
            return current with
            {
                State = FlightTrackingState.Airborne,
                TouchAndGoCount = current.TouchAndGoCount + 1
            };
        }

        if (evidence.GoAroundConfirmed)
        {
            return current with { State = FlightTrackingState.Airborne };
        }

        if (evidence.LandingRolloutConfirmed)
        {
            return current with { State = FlightTrackingState.TaxiIn };
        }

        return current;
    }
}
