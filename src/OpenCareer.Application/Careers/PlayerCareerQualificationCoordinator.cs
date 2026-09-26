using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class PlayerCareerQualificationCoordinator
{
    private readonly IPlayerCareerProfileStore _store;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public PlayerCareerQualificationCoordinator(
        IPlayerCareerProfileStore store,
        PlayerCareerRuntimeState runtime)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<PlayerCareerProfileStoreRecord> ApplyEarnedAsync(
        PilotQualificationState qualifications,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(qualifications);
        qualifications.Validate();

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
                    "Player career onboarding must be completed before qualifications can be updated.");

            PilotQualificationState existing =
                current.Profile.Qualifications;

            if (qualifications.License < existing.License)
            {
                throw new InvalidOperationException(
                    "Earned pilot license progression cannot move backwards.");
            }

            if (!qualifications.Ratings.IsSupersetOf(existing.Ratings))
            {
                throw new InvalidOperationException(
                    "Earned pilot ratings cannot be removed.");
            }

            if (qualifications.License == existing.License
                && qualifications.Ratings.SetEquals(existing.Ratings))
            {
                return current;
            }

            PlayerCareerProfile updatedProfile =
                current.Profile with
                {
                    Qualifications = qualifications
                };
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
