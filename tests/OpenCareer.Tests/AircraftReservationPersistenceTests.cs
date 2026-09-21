using OpenCareer.Application.Fleet;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Infrastructure.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftReservationPersistenceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "OpenCareer.Tests",
            Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_root, "aircraft-registry.db");

    [Fact]
    public async Task ReservationPersistsAsUnavailableAcrossStoreReopen()
    {
        var first = new SqliteAircraftAvailabilityStore(DatabasePath);

        AircraftReservationAcquireResult acquired =
            await first.TryReserveAsync(
                "msfs-title:Fixture 172",
                "dispatch:alpha");

        var reopened = new SqliteAircraftAvailabilityStore(DatabasePath);
        AircraftAvailabilityState? state =
            await reopened.FindAsync("MSFS-TITLE:FIXTURE 172");

        Assert.Equal(AircraftReservationAcquireResult.Acquired, acquired);
        Assert.NotNull(state);
        Assert.Equal(AircraftAvailabilityStatus.Unavailable, state.Status);
        Assert.Equal("dispatch:alpha", state.ReservationId);
        Assert.True(state.IsReserved);
    }

    [Fact]
    public async Task SameReservationIsIdempotentAndDifferentReservationConflicts()
    {
        var store = new SqliteAircraftAvailabilityStore(DatabasePath);

        AircraftReservationAcquireResult first =
            await store.TryReserveAsync(
                "msfs-title:Fixture Caravan",
                "dispatch:alpha");
        AircraftReservationAcquireResult repeated =
            await store.TryReserveAsync(
                "MSFS-TITLE:FIXTURE CARAVAN",
                "dispatch:alpha");
        AircraftReservationAcquireResult conflict =
            await store.TryReserveAsync(
                "msfs-title:fixture caravan",
                "dispatch:bravo");

        AircraftAvailabilityState? state =
            await store.FindAsync("msfs-title:Fixture Caravan");

        Assert.Equal(AircraftReservationAcquireResult.Acquired, first);
        Assert.Equal(AircraftReservationAcquireResult.AlreadyHeld, repeated);
        Assert.Equal(
            AircraftReservationAcquireResult.HeldByAnotherReservation,
            conflict);
        Assert.Equal("dispatch:alpha", state?.ReservationId);
    }

    [Fact]
    public async Task ExplicitUnavailableAircraftCannotBeReserved()
    {
        var store = new SqliteAircraftAvailabilityStore(DatabasePath);
        await store.SetAsync(
            new AircraftAvailabilityState(
                "msfs-title:Fixture Cargo",
                AircraftAvailabilityStatus.Unavailable));

        AircraftReservationAcquireResult result =
            await store.TryReserveAsync(
                "msfs-title:Fixture Cargo",
                "dispatch:alpha");

        Assert.Equal(AircraftReservationAcquireResult.Unavailable, result);
        AircraftAvailabilityState? state =
            await store.FindAsync("msfs-title:Fixture Cargo");
        Assert.NotNull(state);
        Assert.Null(state.ReservationId);
    }

    [Fact]
    public async Task OnlyMatchingReservationCanReleaseAircraft()
    {
        var store = new SqliteAircraftAvailabilityStore(DatabasePath);
        await store.TryReserveAsync(
            "msfs-title:Fixture Twin",
            "dispatch:alpha");

        AircraftReservationReleaseResult wrong =
            await store.ReleaseReservationAsync(
                "msfs-title:Fixture Twin",
                "dispatch:bravo");

        AircraftAvailabilityState? stillHeld =
            await store.FindAsync("msfs-title:Fixture Twin");

        AircraftReservationReleaseResult released =
            await store.ReleaseReservationAsync(
                "msfs-title:Fixture Twin",
                "dispatch:alpha");
        AircraftReservationReleaseResult repeated =
            await store.ReleaseReservationAsync(
                "msfs-title:Fixture Twin",
                "dispatch:alpha");

        AircraftAvailabilityState? available =
            await store.FindAsync("msfs-title:Fixture Twin");

        Assert.Equal(
            AircraftReservationReleaseResult.HeldByAnotherReservation,
            wrong);
        Assert.Equal(AircraftAvailabilityStatus.Unavailable, stillHeld?.Status);
        Assert.Equal("dispatch:alpha", stillHeld?.ReservationId);
        Assert.Equal(AircraftReservationReleaseResult.Released, released);
        Assert.Equal(
            AircraftReservationReleaseResult.AlreadyReleased,
            repeated);
        Assert.NotNull(available);
        Assert.True(available.IsAvailable);
        Assert.Null(available.ReservationId);
    }

    [Fact]
    public async Task ConcurrentReservationsAcrossStoreInstancesHaveSingleWinner()
    {
        var first = new SqliteAircraftAvailabilityStore(DatabasePath);
        await first.FindAsync("msfs-title:Fixture Concurrent");

        var second = new SqliteAircraftAvailabilityStore(DatabasePath);

        Task<AircraftReservationAcquireResult> alpha =
            first.TryReserveAsync(
                "msfs-title:Fixture Concurrent",
                "dispatch:alpha");
        Task<AircraftReservationAcquireResult> bravo =
            second.TryReserveAsync(
                "msfs-title:Fixture Concurrent",
                "dispatch:bravo");

        AircraftReservationAcquireResult[] results =
            await Task.WhenAll(alpha, bravo);

        Assert.Single(
            results,
            result => result == AircraftReservationAcquireResult.Acquired);
        Assert.Single(
            results,
            result => result == AircraftReservationAcquireResult.HeldByAnotherReservation);

        AircraftAvailabilityState? state =
            await first.FindAsync("msfs-title:Fixture Concurrent");

        Assert.NotNull(state);
        Assert.Equal(AircraftAvailabilityStatus.Unavailable, state.Status);
        Assert.Contains(
            state.ReservationId,
            new[] { "dispatch:alpha", "dispatch:bravo" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
