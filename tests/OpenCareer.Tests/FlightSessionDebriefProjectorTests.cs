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
            Assert.Single(debrief.Legs).RouteTrack.Count);

        LandingDebrief landing =
            Assert.Single(debrief.Landings);

        Assert.Equal(
            LandingOperationType.FullStop,
            landing.OperationType);

        Assert.Equal(
            1,
            landing.EpisodeNumber);
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
                touchdown: true);

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
        FlightContinuityAnchor? anchor = null) =>
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
                Observation: observation));

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
