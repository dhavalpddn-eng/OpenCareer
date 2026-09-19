using System.Collections.Immutable;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Maintenance;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Application.Ownership;

public sealed record CareerAccountSnapshot(
    string CareerId,
    decimal CashBalance,
    decimal OperatingReserve);

public sealed record OwnershipSnapshot(
    CareerAccountSnapshot Account,
    ImmutableArray<OwnedAircraft> Aircraft,
    ImmutableArray<AircraftLoanAccount> Loans,
    ImmutableArray<AircraftInsurancePolicy> InsurancePolicies,
    ImmutableArray<AircraftStorageLease> StorageLeases,
    ImmutableArray<AircraftMaintenanceState> MaintenanceStates);

public interface IOwnershipStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetCareerAccountAsync(
        CareerAccountSnapshot account,
        CancellationToken cancellationToken = default);

    Task UpsertDealerStockAsync(
        string dealerId,
        DealerStock stock,
        CancellationToken cancellationToken = default);

    Task SaveDealerOfferAsync(
        DealerOffer offer,
        CancellationToken cancellationToken = default);

    Task UpsertStorageOfferAsync(
        AircraftStorageOffer offer,
        CancellationToken cancellationToken = default);

    Task<AircraftPurchaseReceipt> ExecutePurchaseAsync(
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken = default);

    Task<OwnershipSnapshot> LoadSnapshotAsync(
        string careerId,
        CancellationToken cancellationToken = default);

    Task<ImmutableArray<LoanScheduleItem>> LoadLoanScheduleAsync(
        string loanId,
        CancellationToken cancellationToken = default);

    Task<bool> TryUseInsuranceRedoAsync(
        string policyId,
        DateOnly realLocalDay,
        CancellationToken cancellationToken = default);

    Task<AircraftMaintenanceState> RecordMaintenanceUsageAsync(
        string ownershipId,
        string operationId,
        MaintenanceProgram program,
        MaintenanceUsage usage,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    Task<AircraftMaintenanceState> CompleteMaintenanceServiceAsync(
        string ownershipId,
        string operationId,
        MaintenanceProgram program,
        MaintenanceServiceQuote quote,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
