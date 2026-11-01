using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class PlayerCareerLocationCoordinator
{
    private readonly IPlayerCareerProfileStore _store;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public PlayerCareerLocationCoordinator(
        IPlayerCareerProfileStore store,
        PlayerCareerRuntimeState runtime)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<PlayerCareerProfileStoreRecord> UpdateAsync(
        CareerLocation location,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        location.Validate();

        await _updateGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PlayerCareerProfileStoreRecord current =
                await RequireCurrentAsync(cancellationToken)
                    .ConfigureAwait(false);

            return await PersistLocationAsync(
                    current,
                    location,
                    savedAt,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task<PlayerCareerProfileStoreRecord> ApplyCompletedTravelAsync(
        JobContract completedContract,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedContract);
        completedContract.Validate();

        if (completedContract.Status
                != ContractStatus.Completed
            || completedContract.CompletedAt is null)
        {
            throw new InvalidOperationException(
                "Career travel settlement requires a completed authoritative job contract.");
        }

        if (savedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedAt));
        }

        await _updateGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PlayerCareerProfileStoreRecord current =
                await RequireCurrentAsync(cancellationToken)
                    .ConfigureAwait(false);

            CareerLocation existing =
                current.Profile.Location;

            if (existing.AppliedTravelContracts.Contains(
                    completedContract.ContractId))
            {
                ValidateAppliedTravel(
                    existing,
                    completedContract);

                return current;
            }

            CareerLocation arrived =
                existing.ApplyCompletedTravel(
                    completedContract);

            DateTimeOffset effectiveSavedAt =
                savedAt < current.SavedAt
                    ? current.SavedAt
                    : savedAt;

            return await PersistLocationAsync(
                    current,
                    arrived,
                    effectiveSavedAt,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private async Task<PlayerCareerProfileStoreRecord> RequireCurrentAsync(
        CancellationToken cancellationToken) =>
        await _runtime
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException(
            "Player career onboarding must be completed before location can be updated.");

    private async Task<PlayerCareerProfileStoreRecord> PersistLocationAsync(
        PlayerCareerProfileStoreRecord current,
        CareerLocation location,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                current.Profile.Location.HomeAirportIcao,
                location.HomeAirportIcao,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Home-base relocation requires its own authoritative workflow.");
        }

        if (location.UpdatedAt
            < current.Profile.Location.UpdatedAt)
        {
            throw new InvalidOperationException(
                "Career location cannot move backwards in time.");
        }

        PlayerCareerProfile updatedProfile =
            current.Profile with
            {
                Location =
                    location
            };

        updatedProfile.Validate();

        PlayerCareerProfileStoreRecord saved =
            await _store
                .SaveAsync(
                    updatedProfile,
                    expectedRevision:
                        current.Revision,
                    savedAt,
                    cancellationToken)
                .ConfigureAwait(false);

        _runtime.Replace(saved);
        return saved;
    }

    private static void ValidateAppliedTravel(
        CareerLocation location,
        JobContract contract)
    {
        string destination =
            contract.DestinationIcao
                .Trim()
                .ToUpperInvariant();

        if (!location.Connections.Contains(destination)
            || location.UpdatedAt
                < contract.CompletedAt!.Value)
        {
            throw new InvalidOperationException(
                "Career travel idempotency marker does not match persisted location history.");
        }
    }
}
