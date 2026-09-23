using System.Collections.Immutable;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Ownership;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Maintenance;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Tests;

internal sealed class TestOwnershipStore(
    OwnershipSnapshot snapshot)
    : IOwnershipStore
{
    public OwnershipSnapshot Snapshot { get; set; } =
        snapshot;

    public List<string> LoadedCareerIds { get; } = [];

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<OwnershipSnapshot> LoadSnapshotAsync(
        string careerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadedCareerIds.Add(careerId);

        if (!string.Equals(
                Snapshot.Account.CareerId,
                careerId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Test ownership snapshot belongs to another career.");
        }

        return Task.FromResult(
            Snapshot);
    }

    public Task SetCareerAccountAsync(
        CareerAccountSnapshot account,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task UpsertDealerStockAsync(
        string dealerId,
        DealerStock stock,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SaveDealerOfferAsync(
        DealerOffer offer,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task UpsertStorageOfferAsync(
        AircraftStorageOffer offer,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AircraftPurchaseReceipt> ExecutePurchaseAsync(
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ImmutableArray<LoanScheduleItem>> LoadLoanScheduleAsync(
        string loanId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> TryUseInsuranceRedoAsync(
        string policyId,
        DateOnly realLocalDay,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AircraftMaintenanceState> RecordMaintenanceUsageAsync(
        string ownershipId,
        string operationId,
        MaintenanceProgram program,
        MaintenanceUsage usage,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AircraftMaintenanceState> CompleteMaintenanceServiceAsync(
        string ownershipId,
        string operationId,
        MaintenanceProgram program,
        MaintenanceServiceQuote quote,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class TestAircraftAvailabilityStore(
    params AircraftAvailabilityState[] states)
    : IAircraftAvailabilityStore
{
    private readonly Dictionary<string, AircraftAvailabilityState> _states =
        states.ToDictionary(
            static state => state.CanonicalAircraftId,
            StringComparer.OrdinalIgnoreCase);

    public List<string> RequestedAircraftIds { get; } = [];

    public Task<AircraftAvailabilityState?> FindAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedAircraftIds.Add(canonicalAircraftId);
        _states.TryGetValue(
            canonicalAircraftId,
            out AircraftAvailabilityState? state);
        return Task.FromResult(
            state);
    }

    public Task SetAsync(
        AircraftAvailabilityState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Validate();
        _states[state.CanonicalAircraftId] =
            state;
        return Task.CompletedTask;
    }
}

internal static class CareerAircraftTestData
{
    internal static OwnershipSnapshot Snapshot(
        Guid careerId,
        params OwnedAircraft[] aircraft) =>
        new(
            new CareerAccountSnapshot(
                careerId.ToString("D"),
                CashBalance:
                    0m,
                OperatingReserve:
                    0m),
            aircraft.ToImmutableArray(),
            ImmutableArray<AircraftLoanAccount>.Empty,
            ImmutableArray<AircraftInsurancePolicy>.Empty,
            ImmutableArray<AircraftStorageLease>.Empty,
            ImmutableArray<AircraftMaintenanceState>.Empty);

    internal static OwnedAircraft Owned(
        Guid careerId,
        string ownershipId,
        string aircraftId,
        string displayName,
        string airportIcao = "KRME",
        OwnedAircraftStatus status = OwnedAircraftStatus.Active) =>
        new(
            ownershipId,
            careerId.ToString("D"),
            aircraftId,
            displayName,
            $"listing-{ownershipId}",
            PurchasePrice:
                100_000m,
            AcquiredConditionPercent:
                90m,
            CurrentAirportIcao:
                airportIcao,
            AcquiredAt:
                new DateTimeOffset(
                    2026,
                    9,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero),
            Status:
                status);
}
