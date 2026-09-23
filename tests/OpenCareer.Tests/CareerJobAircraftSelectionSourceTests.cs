using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Tests;

public sealed class CareerJobAircraftSelectionSourceTests
{
    private static readonly Guid CareerId =
        Guid.Parse(
            "a6000000-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset Now =
        new(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OwnedAircraftAppearsWithoutInstalledAircraftDiscovery()
    {
        OwnedAircraft owned =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-instance-1",
                "canonical-aircraft-1",
                "Career Aircraft");

        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId,
                    owned));

        var availability =
            new TestAircraftAvailabilityStore();

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                ownership,
                availability);

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        CareerJobAircraftOption option =
            Assert.Single(
                snapshot.Aircraft);

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(
            "ownership:owned-instance-1",
            option.SelectionId);
        Assert.Equal(
            "owned-instance-1",
            option.OwnershipId);
        Assert.Equal(
            "canonical-aircraft-1",
            option.AircraftId);
        Assert.Equal(
            "canonical-aircraft-1",
            Assert.Single(
                availability.RequestedAircraftIds));
    }

    [Fact]
    public async Task ZeroOwnershipIsValidEmptyStateWithoutStarterAircraft()
    {
        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId));

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                ownership,
                new TestAircraftAvailabilityStore());

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.True(snapshot.IsAvailable);
        Assert.Empty(snapshot.Aircraft);
        Assert.Contains(
            "No available career-owned aircraft",
            snapshot.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyActiveLocalAvailableOwnedAircraftAreSurfaced()
    {
        OwnedAircraft available =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-available",
                "canonical-available",
                "Available");

        OwnedAircraft unavailable =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-unavailable",
                "canonical-unavailable",
                "Unavailable");

        OwnedAircraft remote =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-remote",
                "canonical-remote",
                "Remote",
                airportIcao:
                    "KDFW");

        OwnedAircraft sold =
            CareerAircraftTestData.Owned(
                CareerId,
                "owned-sold",
                "canonical-sold",
                "Sold",
                status:
                    OwnedAircraftStatus.Sold);

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                new TestOwnershipStore(
                    CareerAircraftTestData.Snapshot(
                        CareerId,
                        available,
                        unavailable,
                        remote,
                        sold)),
                new TestAircraftAvailabilityStore(
                    new AircraftAvailabilityState(
                        "canonical-unavailable",
                        AircraftAvailabilityStatus.Unavailable)));

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.Equal(
            "owned-available",
            Assert.Single(snapshot.Aircraft).OwnershipId);
    }

    [Fact]
    public async Task OwnedInstancesRemainDistinctWhileAvailabilityUsesCanonicalIdentity()
    {
        var availability =
            new TestAircraftAvailabilityStore();

        var source =
            new CareerJobAircraftSelectionSource(
                CareerRuntime(),
                new TestOwnershipStore(
                    CareerAircraftTestData.Snapshot(
                        CareerId,
                        CareerAircraftTestData.Owned(
                            CareerId,
                            "owned-instance-a",
                            "canonical-shared",
                            "Shared Type A"),
                        CareerAircraftTestData.Owned(
                            CareerId,
                            "owned-instance-b",
                            "canonical-shared",
                            "Shared Type B"))),
                availability);

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.Equal(
            2,
            snapshot.Aircraft.Count);
        Assert.Equal(
            2,
            snapshot.Aircraft
                .Select(static option => option.OwnershipId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(
            snapshot.Aircraft,
            static option => Assert.Equal(
                "canonical-shared",
                option.AircraftId));
        Assert.Equal(
            "canonical-shared",
            Assert.Single(
                availability.RequestedAircraftIds));
    }

    [Fact]
    public async Task MissingActiveCareerDoesNotReadOwnership()
    {
        var ownership =
            new TestOwnershipStore(
                CareerAircraftTestData.Snapshot(
                    CareerId));

        var source =
            new CareerJobAircraftSelectionSource(
                new PlayerCareerRuntimeState(
                    new FakeProfileStore(
                        record:
                            null)),
                ownership,
                new TestAircraftAvailabilityStore());

        CareerJobAircraftSelectionSnapshot snapshot =
            await source.ReadAsync();

        Assert.False(snapshot.IsAvailable);
        Assert.Empty(snapshot.Aircraft);
        Assert.Empty(ownership.LoadedCareerIds);
    }

    private static PlayerCareerRuntimeState CareerRuntime()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                CareerId,
                "KRME",
                Now.AddDays(-10));

        return new PlayerCareerRuntimeState(
            new FakeProfileStore(
                new PlayerCareerProfileStoreRecord(
                    Revision:
                        1,
                    profile,
                    SavedAt:
                        Now.AddDays(-1))));
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord? record)
        : IPlayerCareerProfileStore
    {
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
