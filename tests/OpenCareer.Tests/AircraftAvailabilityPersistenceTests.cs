using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftAvailabilityPersistenceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_root, "aircraft-registry.db");

    [Fact]
    public async Task AvailabilitySurvivesStoreReopen()
    {
        var expected = new AircraftAvailabilityState(
            "msfs-title:Fixture 172",
            AircraftAvailabilityStatus.Unavailable);

        var first = new SqliteAircraftAvailabilityStore(DatabasePath);
        await first.SetAsync(expected);

        var reopened = new SqliteAircraftAvailabilityStore(DatabasePath);
        AircraftAvailabilityState? actual =
            await reopened.FindAsync("MSFS-TITLE:FIXTURE 172");

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
        Assert.False(actual.IsAvailable);
    }

    [Fact]
    public async Task SettingSameAircraftReplacesAvailabilityCaseInsensitively()
    {
        var store = new SqliteAircraftAvailabilityStore(DatabasePath);

        await store.SetAsync(
            new AircraftAvailabilityState(
                "msfs-title:Fixture Caravan",
                AircraftAvailabilityStatus.Unavailable));

        await store.SetAsync(
            new AircraftAvailabilityState(
                "MSFS-TITLE:FIXTURE CARAVAN",
                AircraftAvailabilityStatus.Available));

        AircraftAvailabilityState? actual =
            await store.FindAsync("msfs-title:fixture caravan");

        Assert.NotNull(actual);
        Assert.Equal(
            AircraftAvailabilityStatus.Available,
            actual.Status);
        Assert.True(actual.IsAvailable);
        Assert.Equal(
            "MSFS-TITLE:FIXTURE CARAVAN",
            actual.CanonicalAircraftId);
    }

    [Fact]
    public async Task UnknownAircraftHasNoAvailabilityState()
    {
        var store = new SqliteAircraftAvailabilityStore(DatabasePath);

        AircraftAvailabilityState? actual =
            await store.FindAsync("msfs-title:missing");

        Assert.Null(actual);
    }

    [Fact]
    public async Task InvalidAvailabilityStateIsRejectedBeforePersistence()
    {
        var store = new SqliteAircraftAvailabilityStore(DatabasePath);
        var invalid = new AircraftAvailabilityState(
            "msfs-title:Fixture",
            (AircraftAvailabilityStatus)99);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetAsync(invalid));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
