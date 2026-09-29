using Microsoft.Data.Sqlite;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;
using OpenCareer.Infrastructure.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionLongDurationReplayTests : IDisposable
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly FlightSessionCheckpointPolicy ReplayCheckpointPolicy =
        new(TimeSpan.FromMinutes(15));

    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1, 120)]
    [InlineData(3, 360)]
    [InlineData(6, FlightSessionStatistics.MaximumTrackPoints)]
    public async Task LongDurationReplayIsDeterministicBoundedAndRecoverySafe(
        int hours,
        int expectedTrackPoints)
    {
        FlightSession uninterrupted =
            await RunAsync(hours, restartAtMidpoint: false, "continuous");

        FlightSession recovered =
            await RunAsync(hours, restartAtMidpoint: true, "recovered");

        AssertEquivalent(uninterrupted, recovered);
        Assert.Equal(FlightSessionStatus.Completed, recovered.Status);
        Assert.Equal(FlightOperationState.Complete, recovered.OperationState);
        Assert.Equal(1, recovered.Tracking.TakeoffCount);
        Assert.Equal(1, recovered.Tracking.LandingEpisodeCount);
        Assert.Equal(0, recovered.Tracking.BounceCount);
        Assert.Equal(0, recovered.Tracking.TouchAndGoCount);
        Assert.Single(recovered.EffectiveLandingEpisodes);
        Assert.Equal(expectedTrackPoints, recovered.EffectiveStatistics.RouteTrack.Count);
        Assert.True(
            recovered.EffectiveStatistics.RouteTrack.Count
            <= FlightSessionStatistics.MaximumTrackPoints);

        TimeSpan duration = TimeSpan.FromHours(hours);
        Assert.Equal(duration, recovered.TimeLedger.ObservedWallTime);
        Assert.Equal(duration, recovered.TimeLedger.SimulatedOperationalTime);
        Assert.Equal(duration, recovered.TimeLedger.BlockTime);
        Assert.Equal(duration, recovered.TimeLedger.MovementFlightTime);
        Assert.Equal(duration, recovered.TimeLedger.AirborneTime);
        Assert.Equal(duration, recovered.TimeLedger.CareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(hours * 30), recovered.TimeLedger.NightCareerCreditTime);
        Assert.Equal(TimeSpan.FromMinutes(hours * 20), recovered.TimeLedger.ActualInstrumentCareerCreditTime);
    }

    private async Task<FlightSession> RunAsync(
        int hours,
        bool restartAtMidpoint,
        string suffix)
    {
        string databasePath =
            Path.Combine(_directory, $"{hours}-{suffix}.db");
        Guid sessionId =
            new($"00000000-0000-0000-0000-{hours:D12}");
        Guid contractId =
            new($"10000000-0000-0000-0000-{hours:D12}");

        var coordinator = new FlightSessionCoordinator();
        var store = new SqliteFlightSessionCheckpointStore(databasePath);
        var persistence =
            new FlightSessionPersistenceService(
                coordinator,
                store,
                ReplayCheckpointPolicy);

        await persistence.StartAsync(Epoch, contractId, sessionId);
        await AdvanceAsync(persistence, Epoch.AddSeconds(1), validAircraft: true);
        await AdvanceAsync(persistence, Epoch.AddSeconds(2), engineStart: true);
        await AdvanceAsync(persistence, Epoch.AddSeconds(3), movement: true);
        await AdvanceAsync(persistence, Epoch.AddSeconds(4), takeoffCandidate: true);
        await AdvanceAsync(persistence, Epoch.AddSeconds(5), airborne: true);

        int sampleCount = hours * 120;
        for (int sample = 1; sample <= sampleCount; sample++)
        {
            DateTimeOffset timestamp =
                Epoch.AddSeconds(5 + sample * 30L);
            double longitude = -73.78 + sample * 0.001;
            var anchor =
                new FlightContinuityAnchor(
                    timestamp,
                    40.64,
                    longitude,
                    8_000,
                    OnGround: false);

            await persistence.AdvanceAsync(
                new FlightSessionAdvance(
                    Evidence(timestamp, airborne: true),
                    new FlightTimeInterval(
                        TimeSpan.FromSeconds(30),
                        SimulationRate: 1,
                        ValidOperationalEvidence: true,
                        Paused: false,
                        SlewActive: false,
                        CountsTowardBlockTime: true,
                        CountsTowardFlightTime: true,
                        Airborne: true,
                        TaxiOut: false,
                        TaxiIn: false,
                        Night: sample % 2 == 0,
                        ActualInstrument: sample % 3 == 0),
                    ContinuityAnchor: anchor,
                    Observation:
                        new FlightSessionObservation(
                            timestamp,
                            anchor.LatitudeDegrees,
                            anchor.LongitudeDegrees,
                            anchor.AltitudeMslFeet,
                            135,
                            140,
                            1_000 - sample * 0.5,
                            400,
                            CaptureTrackPoint: true)));

            if (sample == sampleCount / 2)
            {
                if (restartAtMidpoint)
                {
                    await persistence.FlushAsync();
                    coordinator = new FlightSessionCoordinator();
                    store = new SqliteFlightSessionCheckpointStore(databasePath);
                    persistence =
                        new FlightSessionPersistenceService(
                            coordinator,
                            store,
                            ReplayCheckpointPolicy);
                    Assert.NotNull(await persistence.RecoverAsync());
                }
                else
                {
                    await persistence.AdvanceAsync(
                        new FlightSessionAdvance(
                            new FlightStateEvidence(
                                timestamp,
                                Connected: false,
                                ContinuityPlausible: false)));
                }

                await AdvanceAsync(persistence, timestamp);
            }
        }

        DateTimeOffset arrival = Epoch.AddSeconds(5 + sampleCount * 30L);
        await AdvanceAsync(persistence, arrival.AddSeconds(1), approach: true);
        await AdvanceAsync(persistence, arrival.AddSeconds(2), touchdown: true);
        await AdvanceAsync(persistence, arrival.AddSeconds(3), rollout: true);
        await AdvanceAsync(persistence, arrival.AddSeconds(4), parking: true);
        await persistence.AdvanceAsync(
            new FlightSessionAdvance(
                Evidence(arrival.AddSeconds(5)),
                ShutdownConfirmed: true));
        FlightSession completed =
            await persistence.CompleteAsync(
                sessionId,
                contractId,
                arrival.AddSeconds(6));

        Assert.Equal(1L, await CountAsync(databasePath, "flight_session_checkpoint"));
        Assert.InRange(
            await CountAsync(databasePath, "flight_session_checkpoint_previous"),
            0L,
            1L);
        return completed;
    }

    private static Task<FlightSession> AdvanceAsync(
        FlightSessionPersistenceService persistence,
        DateTimeOffset timestamp,
        bool validAircraft = false,
        bool engineStart = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool approach = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false) =>
        persistence.AdvanceAsync(
            new FlightSessionAdvance(
                Evidence(
                    timestamp,
                    validAircraft,
                    engineStart,
                    movement,
                    takeoffCandidate,
                    airborne,
                    approach,
                    touchdown,
                    rollout,
                    parking)));

    private static FlightStateEvidence Evidence(
        DateTimeOffset timestamp,
        bool validAircraft = false,
        bool engineStart = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool approach = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false) =>
        new(
            timestamp,
            Connected: true,
            StableTelemetry: true,
            ValidLoadedAircraft: validAircraft,
            ContinuityPlausible: true,
            EngineStartObserved: engineStart,
            SelfPoweredMovementForFlight: movement,
            TakeoffCandidate: takeoffCandidate,
            AirborneConfirmed: airborne,
            ApproachConfirmed: approach,
            TouchdownConfirmed: touchdown,
            LandingRolloutConfirmed: rollout,
            ParkingConfirmed: parking);

    private static async Task<long> CountAsync(
        string databasePath,
        string table)
    {
        await using var connection =
            new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static void AssertEquivalent(
        FlightSession expected,
        FlightSession actual)
    {
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.ContractId, actual.ContractId);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.OperationState, actual.OperationState);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.Tracking, actual.Tracking);
        Assert.Equal(expected.TimeLedger, actual.TimeLedger);
        Assert.Equal(expected.Milestones, actual.Milestones);
        Assert.Equal(
            expected.EffectiveLandingEpisodes.ToArray(),
            actual.EffectiveLandingEpisodes.ToArray());
        Assert.Equal(
            expected.EffectiveStatistics.DistanceNauticalMiles,
            actual.EffectiveStatistics.DistanceNauticalMiles);
        Assert.Equal(
            expected.EffectiveStatistics.MaximumAltitudeMslFeet,
            actual.EffectiveStatistics.MaximumAltitudeMslFeet);
        Assert.Equal(
            expected.EffectiveStatistics.MaximumIndicatedAirspeedKnots,
            actual.EffectiveStatistics.MaximumIndicatedAirspeedKnots);
        Assert.Equal(
            expected.EffectiveStatistics.MaximumGroundSpeedKnots,
            actual.EffectiveStatistics.MaximumGroundSpeedKnots);
        Assert.Equal(
            expected.EffectiveStatistics.FuelBurnedPounds,
            actual.EffectiveStatistics.FuelBurnedPounds);
        Assert.Equal(
            expected.EffectiveStatistics.RouteTrack.ToArray(),
            actual.EffectiveStatistics.RouteTrack.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
