using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;
using OpenCareer.Infrastructure.Flights;

namespace OpenCareer.Tests;

public sealed class LandingContactEvidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationRetainsFirstContactValuesThroughFullStopOrTouchAndGo(bool touchAndGo)
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        AircraftTelemetrySnapshot first = await app.SampleAsync(true, 0, 52, impact: true);
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
        Assert.Empty((await app.Checkpoint.LoadAsync())!.EffectiveLandingEpisodes);
        await app.SampleAsync(true, 0, 48);
        FlightSessionLandingEpisode episode = Assert.Single(app.Session.EffectiveLandingEpisodes);
        Assert.Equal(Contact(first), Assert.Single(episode.EffectiveContacts));
        // TouchdownAt retains its established confirmation semantics, independently of the observed edge time.
        Assert.True(episode.TouchdownAt > first.Timestamp);
        Assert.Equal(0, episode.BounceCount);
        await app.SamplesAsync(3, true, 0, 40);
        Assert.Single(Assert.Single(app.Session.EffectiveLandingEpisodes).EffectiveContacts);

        if (touchAndGo)
        {
            // The existing reducer consumes semantic touch-and-go evidence. This slice does not
            // introduce a new normalized telemetry detector for that already-supported event.
            await app.Persistence.AdvanceAsync(new FlightSessionAdvance(new FlightStateEvidence(
                app.Clock.Tick(), Connected: true, StableTelemetry: true, ValidLoadedAircraft: true,
                ContinuityPlausible: true, TouchAndGoConfirmed: true)));
        }
        else
        {
            await app.SamplesAsync(3, true, 0, 15);
        }
        episode = Assert.Single(app.Session.EffectiveLandingEpisodes);
        Assert.Equal(touchAndGo ? FlightSessionLandingKind.TouchAndGo : FlightSessionLandingKind.FullStop, episode.Kind);
        Assert.Equal(Contact(first), Assert.Single(episode.EffectiveContacts));
        Assert.Equal(1, app.Session.Tracking.LandingEpisodeCount);
        Assert.Equal(touchAndGo ? 1 : 0, app.Session.Tracking.TouchAndGoCount);
        Assert.Equal(episode.EffectiveContacts.ToArray(), Assert.Single((await app.Checkpoint.LoadAsync())!.EffectiveLandingEpisodes).EffectiveContacts.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProvisionalContactAndBouncesRemainOneEpisodeWithOrderedContacts(int bounces)
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        Guid sessionId = app.Session.SessionId;
        Guid? contractId = app.Session.ContractId;
        var expected = new List<FlightLandingContactEvidence>
        {
            Contact(await app.SampleAsync(true, 0, 55, impact: true))
        };
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
        for (int bounce = 0; bounce < bounces; bounce++)
        {
            await app.SampleAsync(false, 8, 54);
            expected.Add(Contact(await app.SampleAsync(true, 0, 50, impact: true)));
            Assert.False(await app.Runtime.RefreshAsync()); // Replayed edge cannot confirm or duplicate a contact.
            await app.SampleAsync(true, 0, 49);
        }
        FlightSessionLandingEpisode episode = Assert.Single(app.Session.EffectiveLandingEpisodes);
        Assert.Equal(expected, episode.EffectiveContacts);
        Assert.Equal(bounces, episode.BounceCount);
        Assert.Equal(bounces, app.Session.Tracking.BounceCount);
        Assert.Equal(1, app.Session.Tracking.LandingEpisodeCount);
        Assert.Equal(sessionId, app.Session.SessionId);
        Assert.Equal(contractId, app.Session.ContractId);
        Assert.Equal(FlightSessionStatus.Active, app.Session.Status);
        // Bounce count changes checkpoint immediately, without waiting for the periodic timer.
        Assert.Equal(expected, Assert.Single((await app.Checkpoint.LoadAsync())!.EffectiveLandingEpisodes).EffectiveContacts);
        await app.SamplesAsync(3, true, 0, 15);
        Assert.Equal(expected, Assert.Single(app.Session.EffectiveLandingEpisodes).EffectiveContacts);
    }

    [Fact]
    public async Task DuplicateAndOlderTelemetryCannotConfirmProvisionalContact()
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        AircraftTelemetrySnapshot first = await app.SampleAsync(true, 0, 50, impact: true);
        for (int i = 0; i < 4; i++)
            Assert.False(await app.Runtime.RefreshAsync());
        app.Telemetry.Latest = first with { Timestamp = first.Timestamp.AddTicks(-1) };
        Assert.False(await app.Runtime.RefreshAsync());
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
        await app.SampleAsync(true, 0, 48);
        await app.SamplesAsync(3, true, 0, 40);
        Assert.Equal(Contact(first), Assert.Single(Assert.Single(app.Session.EffectiveLandingEpisodes).EffectiveContacts));
        Assert.Equal(0, app.Session.Tracking.BounceCount);
    }

    [Theory]
    [InlineData("pause", false)]
    [InlineData("slew", false)]
    [InlineData("invalid", false)]
    [InlineData("pause", true)]
    [InlineData("slew", true)]
    [InlineData("invalid", true)]
    public async Task UntrustworthySampleCannotCreateOrConfirmRetainedContact(string problem, bool afterContact)
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        if (afterContact)
            await app.SampleAsync(true, 0, 52, impact: true);
        await app.SampleAsync(true, 0, 51, transform: sample => problem switch
        {
            "pause" => sample with { Paused = true },
            "slew" => sample with { SlewActive = true },
            _ => sample with { NormalAccelerationG = double.NaN }
        });
        await app.SamplesAsync(4, true, 0, 45);
        Assert.All(app.Session.EffectiveLandingEpisodes, episode => Assert.Empty(episode.EffectiveContacts));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReconnectDoesNotInferAMissingAirGroundEdge(bool provisionalContact)
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        if (provisionalContact)
            await app.SampleAsync(true, 0, 52, impact: true);
        app.Connection.Current = new(SimulatorConnectionState.Reconnecting);
        app.Clock.Tick();
        Assert.True(await app.Runtime.RefreshAsync());
        app.Connection.Current = new(SimulatorConnectionState.Connected);
        await app.SamplesAsync(4, true, 0, 45);
        Assert.All(app.Session.EffectiveLandingEpisodes, episode => Assert.Empty(episode.EffectiveContacts));
    }

    [Fact]
    public async Task TeleportIsRejectedBeforeContactEvidenceReachesSessionOrCheckpoint()
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        await app.SampleAsync(true, 0, 50, impact: true, transform: x => x with { LatitudeDegrees = 50 });
        Assert.Equal(FlightSessionStatus.Suspended, app.Session.Status);
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
        Assert.Empty((await app.Checkpoint.LoadAsync())!.EffectiveLandingEpisodes);
        await app.SamplesAsync(4, true, 0, 50, transform: x => x with { LatitudeDegrees = 50 });
        Assert.Equal(FlightSessionStatus.Interrupted, app.Session.Status);
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
    }

    [Fact]
    public async Task UnconfirmedContactIsDiscardedOnContinuedFlight()
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        await app.SampleAsync(true, 0, 52, impact: true);
        await app.SamplesAsync(7, false, 200, 65);
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
        AircraftTelemetrySnapshot realContact = await app.SampleAsync(true, 0, 50, impact: true);
        await app.SampleAsync(true, 0, 48);
        Assert.Equal(Contact(realContact), Assert.Single(Assert.Single(app.Session.EffectiveLandingEpisodes).EffectiveContacts));
    }

    [Fact]
    public async Task PreTakeoffGroundTelemetryNeverCreatesLandingEvidence()
    {
        using var app = new Harness();
        await app.StartAsync();
        await app.SamplesAsync(6, true, 0, 0);
        await app.SamplesAsync(3, true, 0, 8);
        Assert.Empty(app.Session.EffectiveLandingEpisodes);
        Assert.Null(app.Runtime.Current!.LandingContacts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContactCannotUseAnUntrustworthyPrecedingAirborneSample(bool paused)
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        await app.SampleAsync(false, 20, 55, transform: x => paused ? x with { Paused = true } : x with { SlewActive = true });
        await app.SamplesAsync(3, true, 0, 50);
        Assert.All(app.Session.EffectiveLandingEpisodes, episode => Assert.Empty(episode.EffectiveContacts));
    }

    [Fact]
    public void ContactValuesCannotCreateAnUnconfirmedLandingInTheReducer()
    {
        DateTimeOffset now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        FlightSession session = FlightSession.Start(now);
        var contact = new FlightLandingContactEvidence(now, -350, 1.2, 50, 49, 90, 2, 0, 170, 340);
        session = FlightSessionEngine.Advance(session, new FlightSessionAdvance(new FlightStateEvidence(
            now.AddSeconds(1), Connected: true, StableTelemetry: true, ValidLoadedAircraft: true,
            ContinuityPlausible: true, LandingContacts: [contact])));
        Assert.Empty(session.EffectiveLandingEpisodes);
        Assert.Equal(0, session.Tracking.LandingEpisodeCount);
    }

    [Fact]
    public async Task RestartPreservesExactContactsWithoutFabricatingRecontact()
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        AircraftTelemetrySnapshot first = await app.SampleAsync(true, 0, 50, impact: true);
        await app.SampleAsync(true, 0, 49);
        await app.SampleAsync(false, 8, 49);
        AircraftTelemetrySnapshot second = await app.SampleAsync(true, 0, 48, impact: true);
        await app.SampleAsync(true, 0, 47);
        Guid session = app.Session.SessionId;
        Guid? contract = app.Session.ContractId;
        await app.RestartAsync();
        Assert.Equal(FlightSessionStatus.Suspended, app.Session.Status);
        Assert.Equal(new[] { Contact(first), Contact(second) }, Assert.Single(app.Session.EffectiveLandingEpisodes).EffectiveContacts);
        await app.SamplesAsync(4, true, 0, 45);
        Assert.Equal(session, app.Session.SessionId);
        Assert.Equal(contract, app.Session.ContractId);
        Assert.Equal(1, app.Session.Tracking.BounceCount);
        Assert.Equal(1, app.Session.Tracking.LandingEpisodeCount);
        Assert.Equal(new[] { Contact(first), Contact(second) }, Assert.Single(app.Session.EffectiveLandingEpisodes).EffectiveContacts);
        Assert.Equal(18L, await app.ScalarAsync("PRAGMA user_version;"));
        Assert.Equal(1, app.Session.SchemaVersion);
    }

    [Fact]
    public async Task LegacyCheckpointWithoutContactsLoadsAsUnknownEvidence()
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        await app.SampleAsync(true, 0, 50, impact: true);
        await app.SampleAsync(true, 0, 49);
        await app.EditCheckpointAsync(root =>
        {
            foreach (string property in new[] { "landingEpisodes", "effectiveLandingEpisodes" })
                foreach (JsonNode? episode in root[property]!.AsArray())
                    episode!.AsObject().Remove("contacts");
        });
        await app.RestartAsync();
        FlightSessionLandingEpisode legacy = Assert.Single(app.Session.EffectiveLandingEpisodes);
        Assert.Null(legacy.Contacts);
        Assert.Empty(legacy.EffectiveContacts);
        Assert.Equal(1, app.Session.Tracking.LandingEpisodeCount);
        Assert.Equal(1, app.Session.SchemaVersion);
    }

    [Fact]
    public async Task PartialContactJsonCannotSilentlyBecomeZeroValuedEvidence()
    {
        using var app = new Harness();
        await app.TakeoffAsync();
        await app.SampleAsync(true, 0, 50, impact: true);
        await app.SampleAsync(true, 0, 49);
        await app.EditCheckpointAsync(root => root["landingEpisodes"]![0]!["contacts"]![0]!.AsObject().Remove("normalAccelerationG"));
        await Assert.ThrowsAsync<JsonException>(() => app.Checkpoint.LoadAsync());
    }

    [Fact]
    public void EvidenceValidationRejectsUnknownNumbersAndOutOfOrderContacts()
    {
        var contact = new FlightLandingContactEvidence(DateTimeOffset.UtcNow, -350, 1.2, 50, 49, 90, 2, 0, 170, 340);
        contact.Validate();
        Assert.ThrowsAny<ArgumentException>(() => (contact with { NormalAccelerationG = double.NaN }).Validate());
        Assert.ThrowsAny<ArgumentException>(() => (contact with { FuelTotalPounds = -1 }).Validate());
        Assert.ThrowsAny<ArgumentException>(() => (contact with { Timestamp = default }).Validate());
        Assert.ThrowsAny<ArgumentException>(() => new FlightSessionLandingEpisode(1, contact.Timestamp, FlightSessionLandingKind.Unknown, 0,
            Contacts: [contact, contact]).Validate());
    }

    private static FlightLandingContactEvidence Contact(AircraftTelemetrySnapshot sample) => new(
        sample.Timestamp, sample.VerticalSpeedFeetPerMinute, sample.NormalAccelerationG,
        sample.IndicatedAirspeedKnots, sample.GroundSpeedKnots, sample.HeadingDegrees,
        sample.PitchDegrees, sample.BankDegrees, sample.FuelTotalPounds, sample.PayloadPounds);

    private sealed class Harness : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenCareer.Tests", Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(_root, "career.db");
        public TestClock Clock { get; } = new();
        public TestConnection Connection { get; } = new();
        public TestTelemetry Telemetry { get; } = new();
        public FlightSessionCoordinator Coordinator { get; private set; } = null!;
        public FlightSessionPersistenceService Persistence { get; private set; } = null!;
        public FlightSessionRuntime Runtime { get; private set; } = null!;
        public SqliteFlightSessionCheckpointStore Checkpoint { get; private set; } = null!;
        public FlightSession Session => Coordinator.Current!;

        public Harness() => Wire();
        private void Wire()
        {
            Coordinator = new FlightSessionCoordinator();
            Checkpoint = new SqliteFlightSessionCheckpointStore(PathName);
            Persistence = new FlightSessionPersistenceService(Coordinator, Checkpoint);
            Runtime = new FlightSessionRuntime(Coordinator, Persistence, new FlightTelemetryEvidenceProcessor(),
                new FlightContinuityPolicy(), Connection, Telemetry, Clock);
        }
        public Task<FlightSession> StartAsync() => Persistence.StartAsync(Clock.Now, Guid.NewGuid());
        public async Task RestartAsync()
        {
            Wire();
            await Persistence.RecoverAsync();
        }
        public async Task TakeoffAsync()
        {
            await StartAsync();
            await SamplesAsync(3, true, 0, 0, x => x with { EnginesRunning = 0 });
            await SamplesAsync(2, true, 0, 0);
            await SamplesAsync(2, true, 0, 8);
            await SamplesAsync(2, true, 0, 55);
            await SamplesAsync(4, false, 200, 70);
            Assert.Equal(FlightOperationState.Airborne, Session.OperationState);
            Assert.Equal(1, Session.Tracking.TakeoffCount);
        }
        public async Task SamplesAsync(int count, bool ground, double agl, double speed, Func<AircraftTelemetrySnapshot, AircraftTelemetrySnapshot>? transform = null)
        {
            for (int i = 0; i < count; i++)
                await SampleAsync(ground, agl, speed, transform: transform);
        }
        public async Task<AircraftTelemetrySnapshot> SampleAsync(bool ground, double agl, double speed, bool impact = false,
            Func<AircraftTelemetrySnapshot, AircraftTelemetrySnapshot>? transform = null)
        {
            var sample = new AircraftTelemetrySnapshot(Clock.Tick(), 40.63993, -73.77869, 13 + agl, agl,
                impact ? speed + 1.3 : speed, speed, impact ? -612.5 : 0, impact ? 1.25 : 90,
                impact ? 2.75 : 0, impact ? -1.5 : 0, impact ? 1.85 : 1,
                ground, true, 1, impact ? 170.75 : 170, impact ? 340.25 : 340, 0, true, false, false);
            sample = transform?.Invoke(sample) ?? sample;
            Telemetry.Latest = sample;
            await Runtime.RefreshAsync();
            return sample;
        }
        public async Task<object?> ScalarAsync(string sql)
        {
            await using var db = new SqliteConnection($"Data Source={PathName};Pooling=False");
            await db.OpenAsync();
            await using SqliteCommand command = db.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }
        public async Task EditCheckpointAsync(Action<JsonObject> edit)
        {
            var root = JsonNode.Parse((string)(await ScalarAsync("SELECT payload_json FROM flight_session_checkpoint WHERE slot_id = 1;"))!)!.AsObject();
            edit(root);
            await using var db = new SqliteConnection($"Data Source={PathName};Pooling=False");
            await db.OpenAsync();
            await using SqliteCommand command = db.CreateCommand();
            command.CommandText = "UPDATE flight_session_checkpoint SET payload_json = $json WHERE slot_id = 1;";
            command.Parameters.AddWithValue("$json", root.ToJsonString());
            await command.ExecuteNonQueryAsync();
        }
        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        public DateTimeOffset Tick() => Now = Now.AddSeconds(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class TestTelemetry : ISimulatorTelemetrySource
    {
        public AircraftTelemetrySnapshot? Latest { get; set; }
    }
    private sealed class TestConnection : ISimulatorConnection
    {
        public SimulatorConnectionSnapshot Current { get; set; } = new(SimulatorConnectionState.Connected);
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
