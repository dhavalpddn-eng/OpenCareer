using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionEngineTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TimeIntervalsAccumulateInParentAndOnlyActiveLeg()
    {
        FlightSession first = FlightSession.Start(Epoch);
        FlightTimeInterval firstInterval = TimeInterval(2, simulationRate: 2, night: true);
        first = AdvanceWithTime(first, Epoch.AddMinutes(2), firstInterval);

        FlightTimeLedger firstLedger = Assert.Single(first.EffectiveLegs).EffectiveTimeLedger;
        Assert.Equal(first.TimeLedger, firstLedger);
        Assert.Equal(TimeSpan.FromMinutes(2), firstLedger.CareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(2), firstLedger.NightCareerCreditTime);

        FlightSession betweenLegs =
            first with
            {
                UpdatedAt = Epoch.AddMinutes(2),
                Legs = [first.EffectiveLegs[0].Complete(Epoch.AddMinutes(2))]
            };

        FlightSession second =
            FlightSessionEngine.StartNextLeg(
                betweenLegs,
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new FlightSessionPlan("KBOS", "KPHL"),
                Epoch.AddMinutes(3));

        Assert.Equal(FlightTimeLedger.Empty, second.EffectiveLegs[1].EffectiveTimeLedger);

        FlightTimeInterval secondInterval = TimeInterval(1, actualInstrument: true);
        second = AdvanceWithTime(second, Epoch.AddMinutes(4), secondInterval);

        Assert.Equal(firstLedger, second.EffectiveLegs[0].EffectiveTimeLedger);
        Assert.Equal(TimeSpan.FromMinutes(1), second.EffectiveLegs[1].EffectiveTimeLedger.CareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(1), second.EffectiveLegs[1].EffectiveTimeLedger.ActualInstrumentCareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(3), second.TimeLedger.CareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(2), second.TimeLedger.NightCareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(1), second.TimeLedger.ActualInstrumentCareerCreditTime);
    }

    [Fact]
    public void CompletedLegTimeCannotChange()
    {
        FlightLeg completed =
            Assert.Single(FlightSession.Start(Epoch).EffectiveLegs)
                .Complete(Epoch.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(
            () => completed.AddTime(TimeInterval(1)));
        Assert.Equal(FlightTimeLedger.Empty, completed.EffectiveTimeLedger);
    }

    [Fact]
    public void ObservationsAccumulateOnlyInActiveLegWithFreshBoundaryBaseline()
    {
        FlightSession first = FlightSession.Start(Epoch);
        first = Observe(first, Epoch.AddMinutes(1), 40, -73, 100, 10);
        first = Observe(first, Epoch.AddMinutes(2), 40.1, -73, 90, 10);
        FlightSessionStatistics firstStatistics =
            Assert.Single(first.EffectiveLegs).EffectiveStatistics;
        Assert.True(firstStatistics.DistanceNauticalMiles > 0);
        Assert.Equal(10, firstStatistics.FuelBurnedPounds);

        first = first with
        {
            Legs = [first.EffectiveLegs[0].Complete(first.UpdatedAt)]
        };
        FlightSession second = FlightSessionEngine.StartNextLeg(
            first,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new FlightSessionPlan("KBOS", "KPHL"),
            Epoch.AddMinutes(3));

        second = Observe(second, Epoch.AddMinutes(4), 42, -71, 70, 20);
        Assert.Equal(firstStatistics, second.EffectiveLegs[0].EffectiveStatistics);
        Assert.Equal(0, second.EffectiveLegs[1].EffectiveStatistics.DistanceNauticalMiles);
        Assert.Equal(0, second.EffectiveLegs[1].EffectiveStatistics.FuelBurnedPounds);
        Assert.Equal(70, second.EffectiveLegs[1].EffectiveStatistics.StartFuelPounds);
        Assert.Equal(20, second.EffectiveLegs[1].EffectiveStatistics.StartPayloadPounds);

        second = Observe(second, Epoch.AddMinutes(5), 42.1, -71, 60, 20);
        Assert.True(second.EffectiveLegs[1].EffectiveStatistics.DistanceNauticalMiles > 0);
        Assert.Equal(10, second.EffectiveLegs[1].EffectiveStatistics.FuelBurnedPounds);
        Assert.Equal(40, second.EffectiveStatistics.FuelBurnedPounds);
        Assert.Equal(4, second.EffectiveStatistics.RouteTrack.Count);
    }

    [Fact]
    public void CompletedLegStatisticsCannotChange()
    {
        FlightLeg completed =
            Assert.Single(FlightSession.Start(Epoch).EffectiveLegs)
                .Complete(Epoch.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(
            () => completed.Observe(
                Observation(Epoch.AddMinutes(2), 40, -73, 100, 10),
                Anchor(Epoch.AddMinutes(2), 40, -73)));
        Assert.Equal(FlightSessionStatistics.Empty, completed.EffectiveStatistics);
    }

    private static FlightSession Observe(
        FlightSession session,
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double fuel,
        double payload) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    timestamp,
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true),
                ContinuityAnchor: Anchor(timestamp, latitude, longitude),
                Observation: Observation(timestamp, latitude, longitude, fuel, payload)));

    private static FlightSessionObservation Observation(
        DateTimeOffset timestamp,
        double latitude,
        double longitude,
        double fuel,
        double payload) =>
        new(timestamp, latitude, longitude, 2_000, 100, 100, fuel, payload, true);

    private static FlightContinuityAnchor Anchor(
        DateTimeOffset timestamp,
        double latitude,
        double longitude) =>
        new(timestamp, latitude, longitude, 2_000, OnGround: false);

    private static FlightSession AdvanceWithTime(
        FlightSession session,
        DateTimeOffset timestamp,
        FlightTimeInterval interval) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    timestamp,
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true),
                TimeInterval: interval));

    private static FlightTimeInterval TimeInterval(
        int wallMinutes,
        double simulationRate = 1,
        bool night = false,
        bool actualInstrument = false) =>
        new(
            TimeSpan.FromMinutes(wallMinutes),
            simulationRate,
            ValidOperationalEvidence: true,
            Paused: false,
            SlewActive: false,
            CountsTowardBlockTime: true,
            CountsTowardFlightTime: true,
            Airborne: true,
            TaxiOut: false,
            TaxiIn: false,
            Night: night,
            ActualInstrument: actualInstrument);

    [Fact]
    public void StartingNextLegResetsBoundaryStateAndPreservesCumulativeAggregates()
    {
        FlightSession started =
            FlightSession.Start(
                Epoch,
                sessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                plan: new FlightSessionPlan("KJFK", "KBOS"));

        FlightTimeLedger ledger =
            FlightTimeLedger.Empty.Add(
                new FlightTimeInterval(
                    TimeSpan.FromMinutes(10),
                    1,
                    ValidOperationalEvidence: true,
                    Paused: false,
                    SlewActive: false,
                    CountsTowardBlockTime: true,
                    CountsTowardFlightTime: true,
                    Airborne: true,
                    TaxiOut: false,
                    TaxiIn: false,
                    Night: false,
                    ActualInstrument: false));

        FlightSession completedLeg =
            started with
            {
                UpdatedAt = Epoch.AddMinutes(10),
                OperationState = FlightOperationState.Shutdown,
                Tracking = new FlightTrackingSnapshot(
                    FlightTrackingState.Parked,
                    null,
                    Epoch.AddMinutes(10),
                    TakeoffCount: 2,
                    LandingEpisodeCount: 3,
                    BounceCount: 1,
                    TouchAndGoCount: 1,
                    RejectedTakeoffCount: 1,
                    CrashReported: false),
                TimeLedger = ledger,
                Statistics = FlightSessionStatistics.Empty,
                LandingEpisodes =
                [
                    new FlightSessionLandingEpisode(
                        1,
                        Epoch.AddMinutes(8),
                        FlightSessionLandingKind.FullStop,
                        BounceCount: 1,
                        CompletedAt: Epoch.AddMinutes(9))
                ],
                Legs =
                [
                    Assert.Single(started.EffectiveLegs)
                        .Complete(Epoch.AddMinutes(10))
                ]
            };

        FlightSession next =
            FlightSessionEngine.StartNextLeg(
                completedLeg,
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new FlightSessionPlan("KBOS", "KPHL"),
                Epoch.AddMinutes(11));

        Assert.Equal(completedLeg.SessionId, next.SessionId);
        Assert.Equal(FlightSessionStatus.Active, next.Status);
        Assert.Equal(FlightOperationState.ReadyForStart, next.OperationState);
        Assert.Equal(FlightTrackingState.Preflight, next.Tracking.State);
        Assert.Null(next.Tracking.SuspendedFrom);
        Assert.Equal(completedLeg.Tracking.TakeoffCount, next.Tracking.TakeoffCount);
        Assert.Equal(completedLeg.Tracking.LandingEpisodeCount, next.Tracking.LandingEpisodeCount);
        Assert.Equal(completedLeg.Tracking.BounceCount, next.Tracking.BounceCount);
        Assert.Equal(completedLeg.Tracking.TouchAndGoCount, next.Tracking.TouchAndGoCount);
        Assert.Equal(completedLeg.TimeLedger, next.TimeLedger);
        Assert.Equal(completedLeg.EffectiveStatistics, next.EffectiveStatistics);
        Assert.Equal(completedLeg.EffectiveLandingEpisodes, next.EffectiveLandingEpisodes);
        Assert.Equal(2, next.EffectiveLegs.Count);
        Assert.Equal(2, next.EffectiveLegs[^1].Sequence);
        Assert.Equal(FlightLegStatus.Active, next.EffectiveLegs[^1].Status);
    }

    [Fact]
    public void ColdAndDarkSessionRecordsTheOperationalMilestones()
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId: Guid.NewGuid(),
                sessionId: Guid.NewGuid());

        Assert.Equal(
            FlightOperationState.Accepted,
            session.OperationState);

        session =
            Advance(
                session,
                1,
                stable: true,
                validAircraft: true);

        Assert.Equal(
            FlightOperationState.ReadyForStart,
            session.OperationState);

        session =
            Advance(
                session,
                2,
                engineStart: true);

        Assert.Equal(
            FlightOperationState.EngineStart,
            session.OperationState);

        session =
            Advance(
                session,
                3,
                movement: true);

        Assert.Equal(
            FlightOperationState.TaxiOut,
            session.OperationState);

        session =
            Advance(
                session,
                4,
                takeoffCandidate: true);

        Assert.Equal(
            FlightOperationState.DepartureReady,
            session.OperationState);

        session =
            Advance(
                session,
                5,
                airborne: true);

        Assert.Equal(
            FlightOperationState.Airborne,
            session.OperationState);

        session =
            Advance(
                session,
                6,
                approach: true);

        session =
            Advance(
                session,
                7,
                touchdown: true);

        FlightLeg landedLeg = Assert.Single(session.EffectiveLegs);
        Assert.Equal(FlightLegStatus.Active, landedLeg.Status);
        Assert.Null(landedLeg.CompletedAt);

        Assert.Equal(
            FlightOperationState.Landed,
            session.OperationState);

        session =
            Advance(
                session,
                8,
                rollout: true);

        Assert.Equal(
            FlightOperationState.TaxiIn,
            session.OperationState);

        session =
            Advance(
                session,
                9,
                parking: true);

        Assert.Equal(
            FlightOperationState.Parked,
            session.OperationState);

        session =
            Advance(
                session,
                10,
                shutdown: true);

        Assert.Equal(
            FlightOperationState.Shutdown,
            session.OperationState);

        session =
            Advance(
                session,
                11,
                complete: true);

        Assert.Equal(
            FlightSessionStatus.Completed,
            session.Status);

        Assert.Equal(
            FlightOperationState.Complete,
            session.OperationState);

        Assert.Equal(
            Epoch.AddSeconds(1),
            session.Milestones.AircraftReadyAt);

        Assert.Equal(
            Epoch.AddSeconds(2),
            session.Milestones.EngineStartAt);

        Assert.Equal(
            Epoch.AddSeconds(3),
            session.Milestones.TaxiOutAt);

        Assert.Equal(
            Epoch.AddSeconds(4),
            session.Milestones.TakeoffRollAt);

        Assert.Equal(
            Epoch.AddSeconds(5),
            session.Milestones.TakeoffAt);

        Assert.Equal(
            Epoch.AddSeconds(6),
            session.Milestones.ApproachAt);

        Assert.Equal(
            Epoch.AddSeconds(7),
            session.Milestones.FirstTouchdownAt);

        Assert.Equal(
            Epoch.AddSeconds(8),
            session.Milestones.LandingAt);

        Assert.Equal(
            Epoch.AddSeconds(8),
            session.Milestones.TaxiInAt);

        Assert.Equal(
            Epoch.AddSeconds(9),
            session.Milestones.ParkedAt);

        Assert.Equal(
            Epoch.AddSeconds(10),
            session.Milestones.ShutdownAt);

        Assert.Equal(
            Epoch.AddSeconds(11),
            session.Milestones.CompletedAt);

        FlightLeg completedLeg = Assert.Single(session.EffectiveLegs);
        Assert.Equal(FlightLegStatus.Completed, completedLeg.Status);
        Assert.Equal(Epoch.AddSeconds(11), completedLeg.CompletedAt);

        FlightSession replayed =
            Advance(
                session,
                12,
                complete: true);

        Assert.Same(session, replayed);
        Assert.Equal(
            Epoch.AddSeconds(11),
            Assert.Single(replayed.EffectiveLegs).CompletedAt);
    }

    [Fact]
    public void DisconnectSuspendsWithoutLosingOperationalPhase()
    {
        FlightSession session =
            AirborneSession();

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(10),
                        Connected: false)));

        Assert.Equal(
            FlightSessionStatus.Suspended,
            session.Status);

        Assert.Equal(
            FlightOperationState.Airborne,
            session.OperationState);

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(20),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: true)));

        Assert.Equal(
            FlightSessionStatus.Active,
            session.Status);

        Assert.Equal(
            FlightOperationState.Airborne,
            session.OperationState);

        Assert.Null(
            session.Milestones.InterruptedAt);
    }

    [Fact]
    public void FailedContinuityInterruptsButPreservesPartialFlight()
    {
        FlightSession session =
            AirborneSession();

        Guid sessionId =
            session.SessionId;

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(10),
                        Connected: false)));

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(20),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: false)));

        Assert.Equal(
            sessionId,
            session.SessionId);

        Assert.Equal(
            FlightSessionStatus.Interrupted,
            session.Status);

        Assert.Equal(
            FlightOperationState.Airborne,
            session.OperationState);

        Assert.Equal(
            1,
            session.Tracking.TakeoffCount);

        Assert.NotNull(
            session.Milestones.TakeoffAt);

        Assert.Equal(
            Epoch.AddMinutes(20),
            session.Milestones.InterruptedAt);
    }

    [Fact]
    public void TouchAndGoDoesNotCreateFinalLandingMilestone()
    {
        FlightSession session =
            AirborneSession();

        session =
            Advance(
                session,
                6,
                approach: true);

        session =
            Advance(
                session,
                7,
                touchdown: true);

        session =
            Advance(
                session,
                8,
                touchAndGo: true);

        Assert.Equal(
            FlightOperationState.Airborne,
            session.OperationState);

        Assert.NotNull(
            session.Milestones.FirstTouchdownAt);

        Assert.Null(
            session.Milestones.LandingAt);

        Assert.Equal(
            1,
            session.Tracking.TouchAndGoCount);
    }

    [Fact]
    public void TimeLedgerAccumulatesAlongsideFlightState()
    {
        FlightSession session =
            AirborneSession();

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddMinutes(35),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: true),
                    new FlightTimeInterval(
                        TimeSpan.FromMinutes(30),
                        SimulationRate: 4d,
                        ValidOperationalEvidence: true,
                        Paused: false,
                        SlewActive: false,
                        CountsTowardBlockTime: true,
                        CountsTowardFlightTime: true,
                        Airborne: true,
                        TaxiOut: false,
                        TaxiIn: false,
                        Night: true,
                        ActualInstrument: false)));

        Assert.Equal(
            TimeSpan.FromMinutes(120),
            session.TimeLedger.AirborneTime);

        Assert.Equal(
            TimeSpan.FromMinutes(30),
            session.TimeLedger.CareerCreditTime);

        Assert.Equal(
            TimeSpan.FromMinutes(30),
            session.TimeLedger.NightCareerCreditTime);
    }

    [Fact]
    public void RejectedTakeoffReturnsToTaxiWithoutRecordingTakeoff()
    {
        FlightSession session =
            ReadyForTakeoffSession();

        session =
            Advance(
                session,
                5,
                rejectedTakeoff: true);

        Assert.Equal(
            FlightOperationState.TaxiOut,
            session.OperationState);

        Assert.Null(
            session.Milestones.TakeoffAt);

        Assert.Equal(
            1,
            session.Tracking.RejectedTakeoffCount);
    }

    [Fact]
    public void CancellationIsTerminalAndCannotBeOverwrittenByLaterEvidence()
    {
        FlightSession session =
            FlightSession.Start(Epoch);

        session =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        FlightSession after =
            FlightSessionEngine.Advance(
                session,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(2),
                        Connected: true,
                        StableTelemetry: true,
                        ValidLoadedAircraft: true)));

        Assert.Equal(
            FlightSessionStatus.Cancelled,
            after.Status);

        Assert.Equal(
            FlightOperationState.Cancelled,
            after.OperationState);
    }

    [Fact]
    public void EmptyExplicitSessionIdIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () =>
                FlightSession.Start(
                    Epoch,
                    sessionId: Guid.Empty));
    }

    private static FlightSession ReadyForTakeoffSession()
    {
        FlightSession session =
            FlightSession.Start(Epoch);

        session =
            Advance(
                session,
                1,
                stable: true,
                validAircraft: true);

        session =
            Advance(
                session,
                2,
                engineStart: true);

        session =
            Advance(
                session,
                3,
                movement: true);

        return Advance(
            session,
            4,
            takeoffCandidate: true);
    }

    private static FlightSession AirborneSession()
    {
        FlightSession session =
            ReadyForTakeoffSession();

        return Advance(
            session,
            5,
            airborne: true);
    }

    private static FlightSession Advance(
        FlightSession session,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool engineStart = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool rejectedTakeoff = false,
        bool airborne = false,
        bool approach = false,
        bool touchdown = false,
        bool touchAndGo = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false,
        bool complete = false) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(seconds),
                    Connected: true,
                    StableTelemetry: stable,
                    ValidLoadedAircraft: validAircraft,
                    ContinuityPlausible: true,
                    EngineStartObserved: engineStart,
                    SelfPoweredMovementForFlight: movement,
                    TakeoffCandidate: takeoffCandidate,
                    RejectedTakeoffConfirmed: rejectedTakeoff,
                    AirborneConfirmed: airborne,
                    ApproachConfirmed: approach,
                    TouchdownConfirmed: touchdown,
                    TouchAndGoConfirmed: touchAndGo,
                    LandingRolloutConfirmed: rollout,
                    ParkingConfirmed: parking,
                    OperationCompleteConfirmed: complete),
                ShutdownConfirmed: shutdown));
}
