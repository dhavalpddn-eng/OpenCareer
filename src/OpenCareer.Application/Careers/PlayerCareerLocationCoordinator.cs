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
                await _runtime
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Player career onboarding must be completed before location can be updated.");

            if (!string.Equals(
                    current.Profile.Location.HomeAirportIcao,
                    location.HomeAirportIcao,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Home-base relocation requires its own authoritative workflow.");
            }

            if (location.UpdatedAt < current.Profile.Location.UpdatedAt)
            {
                throw new InvalidOperationException(
                    "Career location cannot move backwards in time.");
            }

            PlayerCareerProfile updatedProfile =
                current.Profile with { Location = location };
            updatedProfile.Validate();

            PlayerCareerProfileStoreRecord saved =
                await _store
                    .SaveAsync(
                        updatedProfile,
                        expectedRevision: current.Revision,
                        savedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            _runtime.Replace(saved);
            return saved;
        }
        finally
        {
            _updateGate.Release();
        }
    }
}
