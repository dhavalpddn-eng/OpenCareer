using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class FlightSessionDebriefProjectorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompletedSessionProjectsRouteTrackFuelAndLandingEvidence()
    {
        FlightSession session =
            CompletedSession();

        var context =
            new FlightSessionDebriefContext(
                LogbookEntryKind.FreeFlight,
                new AircraftDebrief(
                    "Test Aircraft",
                    Family: "Test"),
                ActualDeparture: "KDFW",
                ActualArrival: "KIAH",
                DiversionLocation: null,
                Payload:
                    new PayloadDebrief(
                        PassengerCount: null,
                        CargoMassPounds: null,
                        CargoDescription: null,
                        Outcome: null,
                        EvidenceQuality:
                            EvidenceQuality.Unavailable),
                SafetyOutcome:
                    FlightSafetyOutcome.CompletedNormally,
                MissionOutcome:
                    MissionOutcome.NotApplicable,
                Settlement:
                    FlightSettlementRecord.NotApplicable);

        FlightDebrief debrief =
            FlightSessionDebriefProjector.Create(
                session,
                context);

        Assert.Equal(
            session.SessionId,
            debrief.SessionId);

        FlightLeg runtimeLeg =
            Assert.Single(session.EffectiveLegs);

        FlightLegDebrief debriefLeg =
            Assert.Single(debrief.Legs);

        Assert.Equal(runtimeLeg.LegId, debriefLeg.LegId);
        Assert.Equal(runtimeLeg.Sequence, debriefLeg.Sequence);
        Assert.Equal(runtimeLeg.StartedAt, debriefLeg.StartedAt);
        Assert.Equal(FlightLegStatus.Completed, runtimeLeg.Status);
        Assert.Equal(runtimeLeg.CompletedAt, debriefLeg.EndedAt);
        Assert.Equal(runtimeLeg.EffectiveTimeLedger, debriefLeg.Time);
        Assert.Equal(
            runtimeLeg.EffectiveLandingEpisodeNumbers,
            debriefLeg.LandingEpisodeNumbers);

        Assert.Equal(
            "KDFW",
            debrief.Route.PlannedOrigin);

        Assert.Equal(
            "KIAH",
            debrief.Route.PlannedDestination);

        Assert.InRange(
            debrief.Route.DistanceNauticalMiles!.Value,
            59,
            61);

        Assert.Equal(
            50,
            debrief.Fuel.FuelUsedPounds);

        Assert.Equal(
            2,
            debriefLeg.RouteTrack.Count);

        LandingDebrief landing =
            Assert.Single(debrief.Landings);

        Assert.Equal(
            LandingOperationType.FullStop,
            landing.OperationType);

        Assert.Equal(
            1,
            landing.EpisodeNumber);
        Assert.Equal(-220, landing.VerticalSpeedFeetPerMinute);
        Assert.Equal(1.25, landing.TouchdownG);
        Assert.Equal(71, landing.IndicatedAirspeedKnots);
        Assert.Equal(3, landing.PitchDegrees);
        Assert.Equal(-2, landing.BankDegrees);
        Assert.Null(landing.HardLanding);
    }

    [Fact]
    public void NonTerminalSessionCannotFreezeIntoLogbook()
    {
        FlightSession session =
            FlightSession.Start(Epoch);

        var context =
            new FlightSessionDebriefContext(
                LogbookEntryKind.FreeFlight,
                new AircraftDebrief("Test"),
                null,
                null,
                null,
                new PayloadDebrief(
                    null,
                    null,
                    null,
                    null,
                    EvidenceQuality.Unavailable),
                FlightSafetyOutcome.Interrupted,
                MissionOutcome.NotApplicable,
                FlightSettlementRecord.NotApplicable);

        Assert.Throws<InvalidOperationException>(
            () =>
                FlightSessionDebriefProjector.Create(
                    session,
                    context));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void MultiLegSessionProjectsIndependentOrderedLegEvidence(int legCount)
    {
        FlightSession session = MultiLegSession(legCount);
        FlightSessionDebriefContext context = Context(
            actualDeparture: "KDFW",
            actualArrival: "KMIA",
            diversionLocation: "KFLL");

        FlightDebrief debrief =
            FlightSessionDebriefProjector.Create(session, context);

        Assert.Equal(legCount, debrief.Legs.Count);
        Assert.Equal(session.TimeLedger, debrief.Time);
        Assert.Equal(session.EffectiveStatistics.DistanceNauticalMiles, debrief.Route.DistanceNauticalMiles);

        for (int index = 0; index < legCount; index++)
        {
            FlightLeg runtimeLeg = session.EffectiveLegs[index];
            FlightLegDebrief projected = debrief.Legs[index];
            Assert.Equal(index + 1, projected.Sequence);
            Assert.Equal(runtimeLeg.LegId, projected.LegId);
            Assert.Equal(runtimeLeg.StartedAt, projected.StartedAt);
            Assert.Equal(runtimeLeg.CompletedAt, projected.EndedAt);
            Assert.Equal(runtimeLeg.EffectiveTimeLedger, projected.Time);
            Assert.Equal(runtimeLeg.EffectiveStatistics.DistanceNauticalMiles, projected.Route.DistanceNauticalMiles);
            Assert.Equal(runtimeLeg.EffectiveStatistics.RouteTrack.Count, projected.RouteTrack.Count);
            Assert.Equal(runtimeLeg.EffectiveLandingEpisodeNumbers, projected.LandingEpisodeNumbers);
            Assert.Equal(index == 0 ? "KDFW" : null, projected.Route.ActualDeparture);
            Assert.Equal(index == legCount - 1 ? "KMIA" : null, projected.Route.ActualArrival);
            Assert.Equal(index == legCount - 1 ? "KFLL" : null, projected.Route.DiversionLocation);
        }
    }

    private static FlightSession CompletedSession()
    {
        var plan =
            new FlightSessionPlan(
                "KDFW",
                "KIAH");

        FlightSession session =
            FlightSession.Start(
                Epoch,
                plan: plan);

        session =
            Advance(
                session,
                1,
                stable: true,
                validAircraft: true,
                observation:
                    Observation(
                        1,
                        32,
                        -97,
                        1_000,
                        500,
                        capture: true),
                anchor:
                    Anchor(
                        1,
                        32,
                        -97,
                        1_000));

        session =
            Advance(
                session,
                2,
                movement: true);

        session =
            Advance(
                session,
                3,
                takeoffCandidate: true);

        session =
            Advance(
                session,
                4,
                airborne: true);

        session =
            Advance(
                session,
                5,
                observation:
                    Observation(
                        5,
                        33,
                        -97,
                        8_000,
                        450,
                        capture: true),
                anchor:
                    Anchor(
                        5,
                        33,
                        -97,
                        8_000));

        session =
            Advance(
                session,
                6,
                touchdown: true,
                touchdownMetrics:
                    new FlightSessionTouchdownMetrics(
                        -220,
                        1.25,
                        71,
                        3,
                        -2));

        session =
            Advance(
                session,
                7,
                rollout: true);

        session =
            Advance(
                session,
                8,
                parking: true,
                shutdown: true);

        return FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(9),
                    Connected: true,
                    StableTelemetry: true,
                    ContinuityPlausible: true,
                    OperationCompleteConfirmed: true),
                ShutdownConfirmed: true));
    }

    private static FlightSession MultiLegSession(int legCount)
    {
        FlightSession session = CompletedSession();
        var legs = new List<FlightLeg>(legCount);
        var landings = new List<FlightSessionLandingEpisode>(legCount);

        for (int index = 0; index < legCount; index++)
        {
            int sequence = index + 1;
            int startSecond = index * 3;
            int endSecond =
                index == legCount - 1
                    ? 9
                    : (index + 1) * 3;
            var plan =
                index switch
                {
                    0 => session.Plan!,
                    1 => new FlightSessionPlan("KIAH", "KATL"),
                    _ => new FlightSessionPlan("KATL", "KMIA")
                };
            var statistics =
                FlightSessionStatistics.Empty with
                {
                    DistanceNauticalMiles = sequence * 10,
                    RouteTrack =
                    [
                        new FlightSessionTrackPoint(
                            Epoch.AddSeconds(startSecond + 1),
                            30 + sequence,
                            -90 + sequence,
                            2_000 + sequence)
                    ]
                };
            FlightTimeLedger time =
                FlightTimeLedger.Empty with
                {
                    BlockTime = TimeSpan.FromMinutes(sequence),
                    CareerCreditTime = TimeSpan.FromMinutes(sequence)
                };

            legs.Add(
                new FlightLeg(
                    index == 0 ? session.SessionId : Guid.NewGuid(),
                    sequence,
                    Epoch.AddSeconds(startSecond),
                    plan,
                    FlightLegStatus.Completed,
                    Epoch.AddSeconds(endSecond),
                    time,
                    statistics,
                    StatisticsContinuityAnchor: null,
                    LandingEpisodeNumbers: [sequence]));
            landings.Add(
                new FlightSessionLandingEpisode(
                    sequence,
                    Epoch.AddSeconds(endSecond),
                    FlightSessionLandingKind.FullStop,
                    0,
                    Epoch.AddSeconds(endSecond)));
        }

        return session with
        {
            Legs = legs,
            LandingEpisodes = landings
        };
    }

    private static FlightSessionDebriefContext Context(
        string? actualDeparture,
        string? actualArrival,
        string? diversionLocation) =>
        new(
            LogbookEntryKind.FreeFlight,
            new AircraftDebrief("Test Aircraft", Family: "Test"),
            actualDeparture,
            actualArrival,
            diversionLocation,
            new PayloadDebrief(null, null, null, null, EvidenceQuality.Unavailable),
            FlightSafetyOutcome.CompletedNormally,
            MissionOutcome.NotApplicable,
            FlightSettlementRecord.NotApplicable);

    private static FlightSession Advance(
        FlightSession session,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false,
        FlightSessionObservation? observation = null,
        FlightContinuityAnchor? anchor = null,
        FlightSessionTouchdownMetrics? touchdownMetrics = null) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(seconds),
                    Connected: true,
                    StableTelemetry: stable,
                    ValidLoadedAircraft: validAircraft,
                    ContinuityPlausible: true,
                    SelfPoweredMovementForFlight: movement,
                    TakeoffCandidate: takeoffCandidate,
                    AirborneConfirmed: airborne,
                    TouchdownConfirmed: touchdown,
                    LandingRolloutConfirmed: rollout,
                    ParkingConfirmed: parking),
                ShutdownConfirmed: shutdown,
                ContinuityAnchor: anchor,
                Observation: observation,
                TouchdownMetrics: touchdownMetrics));

    private static FlightSessionObservation Observation(
        int seconds,
        double latitude,
        double longitude,
        double altitude,
        double fuel,
        bool capture) =>
        new(
            Epoch.AddSeconds(seconds),
            latitude,
            longitude,
            altitude,
            120,
            140,
            fuel,
            200,
            capture);

    private static FlightContinuityAnchor Anchor(
        int seconds,
        double latitude,
        double longitude,
        double altitude) =>
        new(
            Epoch.AddSeconds(seconds),
            latitude,
            longitude,
            altitude,
            OnGround: false);
}
