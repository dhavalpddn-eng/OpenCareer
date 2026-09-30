namespace OpenCareer.Domain.Flights;

public static class FlightSessionEngine
{
    public static FlightSession StartNextLeg(
        FlightSession current,
        Guid nextLegId,
        FlightSessionPlan nextPlan,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(nextPlan);

        if (nextLegId == Guid.Empty)
            throw new ArgumentException("Flight leg ID cannot be empty.", nameof(nextLegId));

        if (requestedAt == default || requestedAt < current.CreatedAt)
            throw new ArgumentOutOfRangeException(nameof(requestedAt));

        if (current.IsTerminal)
            throw new InvalidOperationException("A terminal FlightSession cannot start another leg.");

        nextPlan.Validate();
        current.ValidateLegs();

        FlightLeg[] legs = current.EffectiveLegs.ToArray();
        DateTimeOffset startedAt =
            requestedAt < current.UpdatedAt
                ? current.UpdatedAt
                : requestedAt;

        FlightLeg final = legs[^1];
        if (final.Status == FlightLegStatus.Active)
        {
            if (final.Sequence > 1
                && final.LegId == nextLegId
                && final.StartedAt == startedAt
                && final.Plan == nextPlan)
            {
                return current;
            }

            throw new InvalidOperationException(
                "The current flight leg must complete before another leg can start.");
        }

        if (legs.Any(leg => leg.LegId == nextLegId))
            throw new InvalidOperationException("Flight leg ID already exists in this session.");

        var nextLeg =
            new FlightLeg(
                nextLegId,
                final.Sequence + 1,
                startedAt,
                nextPlan,
                TimeLedger: FlightTimeLedger.Empty,
                Statistics: FlightSessionStatistics.Empty,
                LandingEpisodeNumbers: Array.Empty<int>());

        FlightSession next =
            current with
            {
                UpdatedAt = startedAt,
                Status = FlightSessionStatus.Active,
                OperationState = FlightOperationState.ReadyForStart,
                Tracking = current.Tracking with
                {
                    State = FlightTrackingState.Preflight,
                    SuspendedFrom = null,
                    UpdatedAt = startedAt
                },
                Legs = [.. legs, nextLeg]
            };

        next.ValidateLegs();
        return next;
    }

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

        FlightTimeLedger ledger = current.TimeLedger;
        FlightLeg[] legs = current.EffectiveLegs.ToArray();

        if (update.TimeInterval is not null)
        {
            int activeLegIndex =
                Array.FindLastIndex(
                    legs,
                    leg => leg.Status == FlightLegStatus.Active);

            if (activeLegIndex >= 0)
            {
                ledger = ledger.Add(update.TimeInterval);
                legs[activeLegIndex] =
                    legs[activeLegIndex].AddTime(update.TimeInterval);
            }
        }

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
                update.ShutdownConfirmed);

        FlightSessionStatistics statistics =
            current.EffectiveStatistics;

        if (update.Observation is not null)
        {
            statistics =
                statistics.Observe(
                    update.Observation,
                    current.ContinuityAnchor);

            int activeLegIndex =
                Array.FindLastIndex(
                    legs,
                    leg => leg.Status == FlightLegStatus.Active);

            if (activeLegIndex >= 0)
            {
                legs[activeLegIndex] =
                    legs[activeLegIndex].Observe(
                        update.Observation,
                        update.ContinuityAnchor);
            }
        }

        IReadOnlyList<FlightSessionLandingEpisode> landingEpisodes =
            UpdateLandingEpisodes(
                current.EffectiveLandingEpisodes,
                previousTracking,
                nextTracking,
                update.Evidence.Timestamp);

        if (landingEpisodes.Count > current.EffectiveLandingEpisodes.Count)
        {
            int activeLegIndex =
                Array.FindLastIndex(
                    legs,
                    leg => leg.Status == FlightLegStatus.Active);

            if (activeLegIndex >= 0)
            {
                foreach (FlightSessionLandingEpisode episode
                         in landingEpisodes.Skip(current.EffectiveLandingEpisodes.Count))
                {
                    legs[activeLegIndex] =
                        legs[activeLegIndex]
                            .ReferenceLandingEpisode(episode.EpisodeNumber);
                }
            }
        }

        if (update.ShutdownConfirmed
            && nextTracking.State == FlightTrackingState.Parked)
        {
            operationState = FlightOperationState.Shutdown;
        }

        if (nextTracking.State == FlightTrackingState.Complete)
        {
            operationState = FlightOperationState.Complete;
            status = FlightSessionStatus.Completed;
            legs[^1] =
                legs[^1].Complete(update.Evidence.Timestamp);
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
                ?? current.ContinuityAnchor,
            Statistics = statistics,
            LandingEpisodes = landingEpisodes,
            Legs = legs
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

    private static IReadOnlyList<FlightSessionLandingEpisode> UpdateLandingEpisodes(
        IReadOnlyList<FlightSessionLandingEpisode> current,
        FlightTrackingSnapshot previous,
        FlightTrackingSnapshot next,
        DateTimeOffset timestamp)
    {
        var episodes =
            current.ToList();

        if (next.LandingEpisodeCount
            > previous.LandingEpisodeCount)
        {
            for (int episode =
                     previous.LandingEpisodeCount + 1;
                 episode <= next.LandingEpisodeCount;
                 episode++)
            {
                episodes.Add(
                    new FlightSessionLandingEpisode(
                        episode,
                        timestamp,
                        FlightSessionLandingKind.Unknown,
                        BounceCount: 0));
            }
        }

        int bounceDelta =
            next.BounceCount
            - previous.BounceCount;

        if (bounceDelta > 0
            && episodes.Count > 0)
        {
            FlightSessionLandingEpisode last =
                episodes[^1];

            episodes[^1] =
                last with
                {
                    BounceCount =
                        last.BounceCount + bounceDelta
                };
        }

        if (next.TouchAndGoCount
            > previous.TouchAndGoCount
            && episodes.Count > 0)
        {
            FlightSessionLandingEpisode last =
                episodes[^1];

            episodes[^1] =
                last with
                {
                    Kind =
                        FlightSessionLandingKind.TouchAndGo,
                    CompletedAt =
                        last.CompletedAt
                        ?? timestamp
                };
        }

        if (Entered(
                previous,
                next,
                FlightTrackingState.TaxiIn)
            && episodes.Count > 0)
        {
            FlightSessionLandingEpisode last =
                episodes[^1];

            episodes[^1] =
                last with
                {
                    Kind =
                        FlightSessionLandingKind.FullStop,
                    CompletedAt =
                        last.CompletedAt
                        ?? timestamp
                };
        }

        foreach (FlightSessionLandingEpisode episode
                 in episodes)
        {
            episode.Validate();
        }

        return episodes;
    }

    private static bool Entered(
        FlightTrackingSnapshot previous,
        FlightTrackingSnapshot next,
        FlightTrackingState state) =>
        previous.State != state
        && next.State == state;
}
