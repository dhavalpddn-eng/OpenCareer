using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class PlayerCareerOnboardingCoordinator
{
    private readonly IPlayerCareerProfileStore _store;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly SemaphoreSlim _createGate = new(1, 1);

    public PlayerCareerOnboardingCoordinator(
        IPlayerCareerProfileStore store,
        PlayerCareerRuntimeState runtime)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<PlayerCareerProfileStoreRecord> CreateAsync(
        Guid careerId,
        string homeAirportIcao,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                careerId,
                homeAirportIcao,
                createdAt);

        await _createGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PlayerCareerProfileStoreRecord? existing =
                await _runtime
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (existing is not null)
            {
                throw new InvalidOperationException(
                    "Player career onboarding has already been completed.");
            }

            PlayerCareerProfileStoreRecord saved =
                await _store
                    .SaveAsync(
                        profile,
                        expectedRevision: null,
                        savedAt: createdAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            _runtime.Replace(saved);
            return saved;
        }
        finally
        {
            _createGate.Release();
        }
    }
}
