using OpenCareer.Application.Fleet;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Tests;

public sealed class AircraftReservationCoordinatorTests
{
    [Fact]
    public async Task InstalledAircraftUsesResolvedCanonicalIdentity()
    {
        AircraftRegistryResolution resolution = Resolution(
            "canonical-aircraft",
            isInstalled: true);
        var registry = new StubAircraftRegistrySource(resolution);
        var store = new StubAircraftReservationStore(
            AircraftReservationAcquireResult.Acquired);
        var coordinator = new AircraftReservationCoordinator(registry, store);

        AircraftReservationRequestResult result = await coordinator.ReserveAsync(
            "provider-alias",
            "dispatch:alpha");

        Assert.Equal(
            AircraftReservationRequestStatus.Acquired,
            result.Status);
        Assert.Equal("canonical-aircraft", result.CanonicalAircraftId);
        Assert.Equal("provider-alias", registry.RequestedAircraftId);
        Assert.Equal("canonical-aircraft", store.CanonicalAircraftId);
        Assert.Equal("dispatch:alpha", store.ReservationId);
        Assert.Equal(1, store.TryReserveCallCount);
    }

    [Fact]
    public async Task UnknownAircraftDoesNotTouchReservationStore()
    {
        var registry = new StubAircraftRegistrySource(null);
        var store = new StubAircraftReservationStore(
            AircraftReservationAcquireResult.Acquired);
        var coordinator = new AircraftReservationCoordinator(registry, store);

        AircraftReservationRequestResult result = await coordinator.ReserveAsync(
            "missing-aircraft",
            "dispatch:alpha");

        Assert.Equal(
            AircraftReservationRequestStatus.AircraftNotFound,
            result.Status);
        Assert.Null(result.CanonicalAircraftId);
        Assert.Equal(0, store.TryReserveCallCount);
    }

    [Fact]
    public async Task KnownButNotInstalledAircraftDoesNotTouchReservationStore()
    {
        AircraftRegistryResolution resolution = Resolution(
            "canonical-aircraft",
            isInstalled: false);
        var store = new StubAircraftReservationStore(
            AircraftReservationAcquireResult.Acquired);
        var coordinator = new AircraftReservationCoordinator(
            new StubAircraftRegistrySource(resolution),
            store);

        AircraftReservationRequestResult result = await coordinator.ReserveAsync(
            "provider-alias",
            "dispatch:alpha");

        Assert.Equal(
            AircraftReservationRequestStatus.AircraftNotInstalled,
            result.Status);
        Assert.Equal("canonical-aircraft", result.CanonicalAircraftId);
        Assert.Equal(0, store.TryReserveCallCount);
    }

    [Theory]
    [InlineData(
        AircraftReservationAcquireResult.Acquired,
        AircraftReservationRequestStatus.Acquired)]
    [InlineData(
        AircraftReservationAcquireResult.AlreadyHeld,
        AircraftReservationRequestStatus.AlreadyHeld)]
    [InlineData(
        AircraftReservationAcquireResult.Unavailable,
        AircraftReservationRequestStatus.Unavailable)]
    [InlineData(
        AircraftReservationAcquireResult.HeldByAnotherReservation,
        AircraftReservationRequestStatus.HeldByAnotherReservation)]
    public async Task StoreOutcomeIsMappedDeterministically(
        AircraftReservationAcquireResult storeResult,
        AircraftReservationRequestStatus expectedStatus)
    {
        var coordinator = new AircraftReservationCoordinator(
            new StubAircraftRegistrySource(
                Resolution("canonical-aircraft", isInstalled: true)),
            new StubAircraftReservationStore(storeResult));

        AircraftReservationRequestResult result = await coordinator.ReserveAsync(
            "provider-alias",
            "dispatch:alpha");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("canonical-aircraft", result.CanonicalAircraftId);
    }

    [Fact]
    public async Task ReleaseUsesResolvedCanonicalIdentity()
    {
        AircraftRegistryResolution resolution = Resolution(
            "canonical-aircraft",
            isInstalled: true);
        var registry = new StubAircraftRegistrySource(resolution);
        var store = new StubAircraftReservationStore(
            AircraftReservationAcquireResult.Acquired,
            AircraftReservationReleaseResult.Released);
        var coordinator = new AircraftReservationCoordinator(registry, store);

        AircraftReservationReleaseRequestResult result =
            await coordinator.ReleaseAsync(
                "provider-alias",
                "dispatch:alpha");

        Assert.Equal(
            AircraftReservationReleaseRequestStatus.Released,
            result.Status);
        Assert.Equal("canonical-aircraft", result.CanonicalAircraftId);
        Assert.Equal("provider-alias", registry.RequestedAircraftId);
        Assert.Equal("canonical-aircraft", store.ReleasedCanonicalAircraftId);
        Assert.Equal("dispatch:alpha", store.ReleasedReservationId);
        Assert.Equal(1, store.ReleaseCallCount);
    }

    [Fact]
    public async Task ReleaseStillWorksWhenAircraftIsNoLongerInstalled()
    {
        AircraftRegistryResolution resolution = Resolution(
            "canonical-aircraft",
            isInstalled: false);
        var store = new StubAircraftReservationStore(
            AircraftReservationAcquireResult.Acquired,
            AircraftReservationReleaseResult.Released);
        var coordinator = new AircraftReservationCoordinator(
            new StubAircraftRegistrySource(resolution),
            store);

        AircraftReservationReleaseRequestResult result =
            await coordinator.ReleaseAsync(
                "provider-alias",
                "dispatch:alpha");

        Assert.Equal(
            AircraftReservationReleaseRequestStatus.Released,
            result.Status);
        Assert.Equal("canonical-aircraft", result.CanonicalAircraftId);
        Assert.Equal(1, store.ReleaseCallCount);
    }

    [Fact]
    public async Task UnknownAircraftDoesNotTouchReservationStoreOnRelease()
    {
        var store = new StubAircraftReservationStore(
            AircraftReservationAcquireResult.Acquired,
            AircraftReservationReleaseResult.Released);
        var coordinator = new AircraftReservationCoordinator(
            new StubAircraftRegistrySource(null),
            store);

        AircraftReservationReleaseRequestResult result =
            await coordinator.ReleaseAsync(
                "missing-aircraft",
                "dispatch:alpha");

        Assert.Equal(
            AircraftReservationReleaseRequestStatus.AircraftNotFound,
            result.Status);
        Assert.Null(result.CanonicalAircraftId);
        Assert.Equal(0, store.ReleaseCallCount);
    }

    [Theory]
    [InlineData(
        AircraftReservationReleaseResult.Released,
        AircraftReservationReleaseRequestStatus.Released)]
    [InlineData(
        AircraftReservationReleaseResult.AlreadyReleased,
        AircraftReservationReleaseRequestStatus.AlreadyReleased)]
    [InlineData(
        AircraftReservationReleaseResult.NotReserved,
        AircraftReservationReleaseRequestStatus.NotReserved)]
    [InlineData(
        AircraftReservationReleaseResult.HeldByAnotherReservation,
        AircraftReservationReleaseRequestStatus.HeldByAnotherReservation)]
    public async Task ReleaseStoreOutcomeIsMappedDeterministically(
        AircraftReservationReleaseResult storeResult,
        AircraftReservationReleaseRequestStatus expectedStatus)
    {
        var coordinator = new AircraftReservationCoordinator(
            new StubAircraftRegistrySource(
                Resolution("canonical-aircraft", isInstalled: true)),
            new StubAircraftReservationStore(
                AircraftReservationAcquireResult.Acquired,
                storeResult));

        AircraftReservationReleaseRequestResult result =
            await coordinator.ReleaseAsync(
                "provider-alias",
                "dispatch:alpha");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("canonical-aircraft", result.CanonicalAircraftId);
    }

    private static AircraftRegistryResolution Resolution(
        string canonicalAircraftId,
        bool isInstalled) =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: canonicalAircraftId,
                ProviderId: "test-source",
                ProviderRecordId: "fixture",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: isInstalled)
        ]);

    private sealed class StubAircraftRegistrySource(
        AircraftRegistryResolution? result)
        : IAircraftRegistrySource
    {
        public string? RequestedAircraftId { get; private set; }

        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedAircraftId = aircraftId;
            return Task.FromResult(result);
        }
    }

    private sealed class StubAircraftReservationStore(
        AircraftReservationAcquireResult acquireResult,
        AircraftReservationReleaseResult releaseResult =
            AircraftReservationReleaseResult.Released)
        : IAircraftReservationStore
    {
        public int TryReserveCallCount { get; private set; }
        public int ReleaseCallCount { get; private set; }
        public string? CanonicalAircraftId { get; private set; }
        public string? ReservationId { get; private set; }
        public string? ReleasedCanonicalAircraftId { get; private set; }
        public string? ReleasedReservationId { get; private set; }

        public Task<AircraftReservationAcquireResult> TryReserveAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryReserveCallCount++;
            CanonicalAircraftId = canonicalAircraftId;
            ReservationId = reservationId;
            return Task.FromResult(acquireResult);
        }

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCallCount++;
            ReleasedCanonicalAircraftId = canonicalAircraftId;
            ReleasedReservationId = reservationId;
            return Task.FromResult(releaseResult);
        }
    }
}
