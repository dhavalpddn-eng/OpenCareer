namespace OpenCareer.Domain.Flights;

public sealed record FlightSession(
    Guid SessionId,
    Guid? ContractId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    FlightSessionStatus Status,
    FlightOperationState OperationState,
    FlightTrackingSnapshot Tracking,
    FlightTimeLedger TimeLedger,
    FlightSessionMilestones Milestones,
    int SchemaVersion = 1,
    FlightContinuityAnchor? ContinuityAnchor = null,
    FlightSessionPlan? Plan = null,
    FlightSessionStatistics? Statistics = null,
    IReadOnlyList<FlightSessionLandingEpisode>? LandingEpisodes = null,
    IReadOnlyList<FlightLeg>? Legs = null)
{
    public static FlightSession Start(
        DateTimeOffset timestamp,
        Guid? contractId = null,
        Guid? sessionId = null,
        FlightSessionPlan? plan = null)
    {
        Guid resolvedSessionId =
            sessionId ?? Guid.NewGuid();

        if (resolvedSessionId == Guid.Empty)
            throw new ArgumentException("Flight session ID cannot be empty.", nameof(sessionId));

        plan?.Validate();

        return new FlightSession(
            resolvedSessionId,
            contractId,
            timestamp,
            timestamp,
            FlightSessionStatus.Active,
            FlightOperationState.Accepted,
            FlightTrackingSnapshot.Start(timestamp),
            FlightTimeLedger.Empty,
            FlightSessionMilestones.Empty,
            Plan: plan,
            Statistics: FlightSessionStatistics.Empty,
            LandingEpisodes: Array.Empty<FlightSessionLandingEpisode>(),
            Legs:
            [
                FlightLeg.First(
                    resolvedSessionId,
                    timestamp,
                    plan)
            ]);
    }

    public FlightSessionStatistics EffectiveStatistics =>
        Statistics ?? FlightSessionStatistics.Empty;

    public IReadOnlyList<FlightSessionLandingEpisode> EffectiveLandingEpisodes =>
        LandingEpisodes ?? Array.Empty<FlightSessionLandingEpisode>();

    public IReadOnlyList<FlightLeg> EffectiveLegs =>
        Legs
        ??
        [
            FlightLeg.First(
                SessionId,
                CreatedAt,
                Plan)
        ];

    public FlightSession EnsureInitialLeg() =>
        Legs is not null
            ? this
            : this with
            {
                Legs =
                [
                    FlightLeg.First(
                        SessionId,
                        CreatedAt,
                        Plan) with
                    {
                        TimeLedger = TimeLedger,
                        Statistics = EffectiveStatistics,
                        StatisticsContinuityAnchor = ContinuityAnchor,
                        LandingEpisodeNumbers =
                            EffectiveLandingEpisodes
                                .Select(episode => episode.EpisodeNumber)
                                .ToArray()
                    }
                ]
            };

    public FlightSession EnsureLegLifecycle()
    {
        FlightSession session = EnsureInitialLeg();

        if (session.Status != FlightSessionStatus.Completed
            || session.Legs is not { Count: 1 }
            || session.Legs[0].Status != FlightLegStatus.Active
            || session.Legs[0].CompletedAt is not null
            || session.Milestones.CompletedAt is not { } completedAt)
        {
            return session;
        }

        return session with
        {
            Legs =
            [
                session.Legs[0].Complete(completedAt)
            ]
        };
    }

    public FlightSession EnsureLegTimeAccounting()
    {
        FlightSession session = EnsureLegLifecycle();

        if (session.Legs is not { Count: 1 }
            || session.Legs[0].TimeLedger is not null)
        {
            return session;
        }

        return session with
        {
            Legs =
            [
                session.Legs[0] with
                {
                    TimeLedger = session.TimeLedger
                }
            ]
        };
    }

    public FlightSession EnsureLegStatistics()
    {
        FlightSession session = EnsureLegTimeAccounting();

        if (session.Legs is not { Count: 1 }
            || session.Legs[0].Statistics is not null)
        {
            return session;
        }

        return session with
        {
            Legs =
            [
                session.Legs[0] with
                {
                    Statistics = session.EffectiveStatistics,
                    StatisticsContinuityAnchor = session.ContinuityAnchor
                }
            ]
        };
    }

    public FlightSession EnsureLegLandingEpisodes()
    {
        FlightSession session = EnsureLegStatistics();

        if (session.Legs is not { Count: 1 }
            || session.Legs[0].LandingEpisodeNumbers is not null)
        {
            return session;
        }

        return session with
        {
            Legs =
            [
                session.Legs[0] with
                {
                    LandingEpisodeNumbers =
                        session.EffectiveLandingEpisodes
                            .Select(episode => episode.EpisodeNumber)
                            .ToArray()
                }
            ]
        };
    }

    public void ValidateLegs()
    {
        if (Legs is null || Legs.Count == 0)
            throw new InvalidOperationException("FlightSession requires at least one flight leg.");

        var legIds = new HashSet<Guid>();
        var referencedLandingEpisodes = new HashSet<int>();
        var sessionLandingEpisodes =
            EffectiveLandingEpisodes
                .Select(episode => episode.EpisodeNumber)
                .ToHashSet();
        FlightLeg? previous = null;
        int activeCount = 0;

        for (int index = 0; index < Legs.Count; index++)
        {
            FlightLeg leg =
                Legs[index]
                ?? throw new InvalidOperationException("FlightSession cannot contain a null flight leg.");

            leg.Validate();

            if (leg.Sequence != index + 1)
                throw new InvalidOperationException("Flight-leg sequence must be contiguous and ordered.");

            if (!legIds.Add(leg.LegId))
                throw new InvalidOperationException("Flight-leg identities must be unique within a session.");

            foreach (int episodeNumber in leg.EffectiveLandingEpisodeNumbers)
            {
                if (!sessionLandingEpisodes.Contains(episodeNumber)
                    || !referencedLandingEpisodes.Add(episodeNumber))
                {
                    throw new InvalidOperationException(
                        "Flight-leg landing references must identify unique session episodes.");
                }
            }

            if (leg.StartedAt < CreatedAt || leg.StartedAt > UpdatedAt)
                throw new InvalidOperationException("Flight-leg start must fall within its parent session.");

            if (leg.CompletedAt is { } completedAt && completedAt > UpdatedAt)
                throw new InvalidOperationException("Flight-leg completion cannot exceed its parent session timestamp.");

            if (previous is not null)
            {
                if (previous.CompletedAt is not { } previousCompletedAt)
                    throw new InvalidOperationException("Only the final flight leg may remain active.");

                if (leg.StartedAt < previousCompletedAt)
                    throw new InvalidOperationException("Flight legs must be chronologically ordered without overlap.");
            }

            if (leg.Status == FlightLegStatus.Active)
            {
                activeCount++;
                if (index != Legs.Count - 1)
                    throw new InvalidOperationException("An active flight leg must be the final leg.");
            }

            previous = leg;
        }

        if (activeCount > 1)
            throw new InvalidOperationException("FlightSession cannot contain multiple active flight legs.");

        FlightLeg first = Legs[0];
        if (first.LegId != SessionId
            || first.StartedAt != CreatedAt
            || first.Plan != Plan)
        {
            throw new InvalidOperationException(
                "Flight leg 1 must preserve its parent FlightSession identity, start, and plan.");
        }

        if (Status == FlightSessionStatus.Completed)
        {
            FlightLeg final = Legs[^1];
            if (activeCount != 0
                || Milestones.CompletedAt is not { } completedAt
                || final.CompletedAt != completedAt)
            {
                throw new InvalidOperationException(
                    "Completed FlightSession and final flight-leg terminal state must match.");
            }
        }
    }

    public bool IsTerminal =>
        Status is FlightSessionStatus.Interrupted
            or FlightSessionStatus.Completed
            or FlightSessionStatus.Cancelled;
}
