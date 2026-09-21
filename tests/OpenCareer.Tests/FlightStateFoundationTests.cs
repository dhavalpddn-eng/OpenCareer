using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public class FlightStateFoundationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ColdDarkFlightTracksTakeoffBounceLandingAndCompletion()
    {
        var state = FlightTrackingSnapshot.Start(Epoch);

        state = Step(state, 1, stable: true, validAircraft: true);
        Assert.Equal(FlightTrackingState.Preflight, state.State);

        state = Step(state, 2, engineStart: true);
        Assert.Equal(FlightTrackingState.EngineStart, state.State);

        state = Step(state, 3, movement: true);
        Assert.Equal(FlightTrackingState.TaxiOut, state.State);

        state = Step(state, 4, takeoffCandidate: true);
        Assert.Equal(FlightTrackingState.TakeoffRoll, state.State);

        state = Step(state, 5, airborne: true);
        Assert.Equal(FlightTrackingState.Airborne, state.State);
        Assert.Equal(1, state.TakeoffCount);

        state = Step(state, 6, approach: true);
        Assert.Equal(FlightTrackingState.Approach, state.State);

        state = Step(state, 7, touchdown: true);
        Assert.Equal(FlightTrackingState.LandingEpisode, state.State);
        Assert.Equal(1, state.LandingEpisodeCount);

        state = Step(state, 8, bounce: true);
        Assert.Equal(FlightTrackingState.LandingEpisode, state.State);
        Assert.Equal(1, state.LandingEpisodeCount);
        Assert.Equal(1, state.BounceCount);

        state = Step(state, 9, rollout: true);
        state = Step(state, 10, parking: true);
        state = Step(state, 11, complete: true);

        Assert.Equal(FlightTrackingState.Complete, state.State);
        Assert.Equal(1, state.LandingEpisodeCount);
    }

    [Fact]
    public void RejectedTakeoffReturnsToTaxiWithoutTakeoffCount()
    {
        var state = ReadyForTakeoff();

        state = Step(state, 5, rejectedTakeoff: true);

        Assert.Equal(FlightTrackingState.TaxiOut, state.State);
        Assert.Equal(0, state.TakeoffCount);
        Assert.Equal(1, state.RejectedTakeoffCount);
    }

    [Fact]
    public void TouchAndGoUsesOneLandingEpisodeThenReturnsAirborne()
    {
        var state = Airborne();
        state = Step(state, 6, approach: true);
        state = Step(state, 7, touchdown: true);
        state = Step(state, 8, touchAndGo: true);

        Assert.Equal(FlightTrackingState.Airborne, state.State);
        Assert.Equal(1, state.LandingEpisodeCount);
        Assert.Equal(1, state.TouchAndGoCount);
    }

    [Fact]
    public void GoAroundDoesNotCreateLandingEpisodeWithoutTouchdown()
    {
        var state = Airborne();
        state = Step(state, 6, approach: true);
        state = Step(state, 7, goAround: true);

        Assert.Equal(FlightTrackingState.Airborne, state.State);
        Assert.Equal(0, state.LandingEpisodeCount);
    }

    [Fact]
    public void DisconnectHasNoTimeoutAndResumesWhenContinuityIsPlausible()
    {
        var state = Airborne();

        state = FlightTrackingStateMachine.Advance(
            state,
            new FlightStateEvidence(Epoch.AddHours(1), Connected: false));

        Assert.Equal(FlightTrackingState.Suspended, state.State);
        Assert.Equal(FlightTrackingState.Airborne, state.SuspendedFrom);

        state = FlightTrackingStateMachine.Advance(
            state,
            new FlightStateEvidence(
                Epoch.AddDays(1),
                Connected: true,
                StableTelemetry: true,
                ContinuityPlausible: true));

        Assert.Equal(FlightTrackingState.Airborne, state.State);
        Assert.Null(state.SuspendedFrom);
    }

    [Fact]
    public void FailedContinuityPreservesInterruptedRecordInsteadOfInventingResume()
    {
        var state = Airborne();
        state = FlightTrackingStateMachine.Advance(
            state,
            new FlightStateEvidence(Epoch.AddHours(1), Connected: false));

        state = FlightTrackingStateMachine.Advance(
            state,
            new FlightStateEvidence(
                Epoch.AddHours(2),
                Connected: true,
                StableTelemetry: true,
                ContinuityPlausible: false));

        Assert.Equal(FlightTrackingState.Interrupted, state.State);
    }

    [Fact]
    public void CrashInterruptsFlightButDoesNotErasePartialTracking()
    {
        var state = Airborne();

        state = Step(state, 6, crash: true);

        Assert.Equal(FlightTrackingState.Interrupted, state.State);
        Assert.True(state.CrashReported);
        Assert.Equal(1, state.TakeoffCount);
    }

    [Fact]
    public void AuthorizedRunwayAndAirborneStartsAreExplicitExceptions()
    {
        var runway = FlightTrackingSnapshot.Start(Epoch);
        runway = Step(runway, 1, stable: true, validAircraft: true);
        runway = Step(runway, 2, authorizedRunwayStart: true, takeoffCandidate: true);
        Assert.Equal(FlightTrackingState.TakeoffRoll, runway.State);

        var airborne = FlightTrackingSnapshot.Start(Epoch);
        airborne = Step(airborne, 1, stable: true, validAircraft: true);
        airborne = Step(airborne, 2, authorizedAirborneStart: true, airborne: true);
        Assert.Equal(FlightTrackingState.Airborne, airborne.State);
        Assert.Equal(0, airborne.TakeoffCount);
    }

    [Fact]
    public void AccelerationChangesSimulatedTimeButNeverMultipliesCareerCredit()
    {
        var ledger = FlightTimeLedger.Empty
            .Add(FlightInterval(30, 1d, airborne: true))
            .Add(FlightInterval(30, 4d, airborne: true, actualInstrument: true))
            .Add(FlightInterval(20, 0.5d, taxiOut: true))
            .Add(FlightInterval(10, 8d, airborne: true));

        Assert.Equal(TimeSpan.FromMinutes(90), ledger.ObservedWallTime);
        Assert.Equal(TimeSpan.FromMinutes(240), ledger.SimulatedOperationalTime);
        Assert.Equal(TimeSpan.FromMinutes(240), ledger.MovementFlightTime);
        Assert.Equal(TimeSpan.FromMinutes(230), ledger.AirborneTime);
        Assert.Equal(TimeSpan.FromMinutes(10), ledger.TaxiOutTime);
        Assert.Equal(TimeSpan.FromMinutes(80), ledger.CareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(30), ledger.ActualInstrumentCareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(40), ledger.AcceleratedWallTime);
    }

    [Fact]
    public void PushbackCountsAsBlockTimeWithoutCreatingFlightHours()
    {
        var ledger = FlightTimeLedger.Empty.Add(new FlightTimeInterval(
            TimeSpan.FromMinutes(10),
            1d,
            ValidOperationalEvidence: true,
            Paused: false,
            SlewActive: false,
            CountsTowardBlockTime: true,
            CountsTowardFlightTime: false,
            Airborne: false,
            TaxiOut: false,
            TaxiIn: false,
            Night: false,
            ActualInstrument: false));

        Assert.Equal(TimeSpan.FromMinutes(10), ledger.BlockTime);
        Assert.Equal(TimeSpan.Zero, ledger.MovementFlightTime);
        Assert.Equal(TimeSpan.Zero, ledger.CareerCreditTime);
    }

    [Fact]
    public void PauseAndSlewNeverCreateCareerOrSimulatedOperationalTime()
    {
        var paused = FlightTimeLedger.Empty.Add(new FlightTimeInterval(
            TimeSpan.FromMinutes(5), 4d, true, true, false, true, true, false, true, false, false, false));
        var slewed = paused.Add(new FlightTimeInterval(
            TimeSpan.FromMinutes(5), 1d, true, false, true, true, true, false, false, true, false, false));

        Assert.Equal(TimeSpan.FromMinutes(10), slewed.ObservedWallTime);
        Assert.Equal(TimeSpan.FromMinutes(5), slewed.PausedWallTime);
        Assert.Equal(TimeSpan.FromMinutes(5), slewed.SlewWallTime);
        Assert.Equal(TimeSpan.Zero, slewed.SimulatedOperationalTime);
        Assert.Equal(TimeSpan.Zero, slewed.CareerCreditTime);
    }

    [Fact]
    public void ConventionalCommercialLegRequiresShutdownAndServicing()
    {
        var policy = FlightLegTerminalPolicy.ConventionalCommercialTurnaround;
        var notServiced = new FlightLegTerminalEvidence(true, true, true, true, 0, false);
        var engineRunning = new FlightLegTerminalEvidence(true, true, true, true, 1, true);
        var complete = new FlightLegTerminalEvidence(true, true, true, true, 0, true);

        Assert.False(policy.IsSatisfied(notServiced));
        Assert.False(policy.IsSatisfied(engineRunning));
        Assert.True(policy.IsSatisfied(complete));
    }

    [Fact]
    public void EvidenceCannotMoveBackwardInTime()
    {
        var state = FlightTrackingSnapshot.Start(Epoch.AddMinutes(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FlightTrackingStateMachine.Advance(
                state,
                new FlightStateEvidence(Epoch, Connected: true)));
    }

    private static FlightTrackingSnapshot ReadyForTakeoff()
    {
        var state = FlightTrackingSnapshot.Start(Epoch);
        state = Step(state, 1, stable: true, validAircraft: true);
        state = Step(state, 2, engineStart: true);
        state = Step(state, 3, movement: true);
        return Step(state, 4, takeoffCandidate: true);
    }

    private static FlightTrackingSnapshot Airborne()
    {
        var state = ReadyForTakeoff();
        return Step(state, 5, airborne: true);
    }

    private static FlightTimeInterval FlightInterval(
        int wallMinutes,
        double rate,
        bool airborne = false,
        bool taxiOut = false,
        bool taxiIn = false,
        bool actualInstrument = false) =>
        new(
            TimeSpan.FromMinutes(wallMinutes),
            rate,
            ValidOperationalEvidence: true,
            Paused: false,
            SlewActive: false,
            CountsTowardBlockTime: true,
            CountsTowardFlightTime: true,
            Airborne: airborne,
            TaxiOut: taxiOut,
            TaxiIn: taxiIn,
            Night: false,
            ActualInstrument: actualInstrument);

    private static FlightTrackingSnapshot Step(
        FlightTrackingSnapshot state,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool authorizedAirborneStart = false,
        bool authorizedRunwayStart = false,
        bool engineStart = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool rejectedTakeoff = false,
        bool airborne = false,
        bool approach = false,
        bool touchdown = false,
        bool bounce = false,
        bool goAround = false,
        bool touchAndGo = false,
        bool rollout = false,
        bool parking = false,
        bool complete = false,
        bool crash = false) =>
        FlightTrackingStateMachine.Advance(
            state,
            new FlightStateEvidence(
                Epoch.AddSeconds(seconds),
                Connected: true,
                StableTelemetry: stable,
                ValidLoadedAircraft: validAircraft,
                ContinuityPlausible: true,
                AuthorizedAirborneStart: authorizedAirborneStart,
                AuthorizedRunwayStart: authorizedRunwayStart,
                EngineStartObserved: engineStart,
                SelfPoweredMovementForFlight: movement,
                TakeoffCandidate: takeoffCandidate,
                RejectedTakeoffConfirmed: rejectedTakeoff,
                AirborneConfirmed: airborne,
                ApproachConfirmed: approach,
                TouchdownConfirmed: touchdown,
                BounceRecontact: bounce,
                GoAroundConfirmed: goAround,
                TouchAndGoConfirmed: touchAndGo,
                LandingRolloutConfirmed: rollout,
                ParkingConfirmed: parking,
                OperationCompleteConfirmed: complete,
                CrashReported: crash));
}
