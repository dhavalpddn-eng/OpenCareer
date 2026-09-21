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
        AircraftReservationAcquireResult result)
        : IAircraftReservationStore
    {
        public int TryReserveCallCount { get; private set; }
        public string? CanonicalAircraftId { get; private set; }
        public string? ReservationId { get; private set; }

        public Task<AircraftReservationAcquireResult> TryReserveAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryReserveCallCount++;
            CanonicalAircraftId = canonicalAircraftId;
            ReservationId = reservationId;
            return Task.FromResult(result);
        }

        public Task<AircraftReservationReleaseResult> ReleaseReservationAsync(
            string canonicalAircraftId,
            string reservationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
