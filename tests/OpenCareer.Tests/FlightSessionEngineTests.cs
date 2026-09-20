using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionEngineTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

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
    }

    [Fact]
    public void InitialClimbMilestoneRequiresConfirmedClimbAfterTakeoff()
    {
        FlightSession session =
            AirborneSession();

        Assert.NotNull(
            session.Milestones.TakeoffAt);

        Assert.Null(
            session.Milestones.InitialClimbAt);

        session =
            Advance(
                session,
                6,
                initialClimb: true);

        Assert.Equal(
            Epoch.AddSeconds(6),
            session.Milestones.InitialClimbAt);
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
        bool initialClimb = false,
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
                    InitialClimbConfirmed: initialClimb,
                    ApproachConfirmed: approach,
                    TouchdownConfirmed: touchdown,
                    TouchAndGoConfirmed: touchAndGo,
                    LandingRolloutConfirmed: rollout,
                    ParkingConfirmed: parking,
                    OperationCompleteConfirmed: complete),
                ShutdownConfirmed: shutdown));
}
