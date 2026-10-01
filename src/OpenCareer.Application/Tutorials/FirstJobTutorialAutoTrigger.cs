using OpenCareer.Application.Careers;
using OpenCareer.Application.Settings;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Tutorials;

public sealed class FirstJobTutorialAutoTrigger(
    IAppSettingsService settings,
    ITutorialCatalog catalog,
    ITutorialProgressStore progressStore,
    TutorialCoordinator coordinator,
    PlayerCareerRuntimeState career,
    IJobBoardStateStore jobBoards,
    IJobContractRuntimeSource contracts)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> TryStartAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!settings.Current.AutomaticallyOfferTutorials
                || coordinator.Current.IsActive)
            {
                return false;
            }

            TutorialDefinition? definition = catalog.Get(AppTutorialCatalog.FirstJobId);
            if (definition is null || definition.Steps.Count == 0)
                return false;

            TutorialProgress progress = await progressStore
                .GetAsync(definition.Id, cancellationToken)
                .ConfigureAwait(true);
            if (progress.CompletedVersion >= definition.Version
                || progress.SkippedVersion >= definition.Version)
            {
                return false;
            }

            PlayerCareerProfileStoreRecord? profile = career.Current;
            if (profile is null)
                return false;

            bool hasCurrentContract = contracts.Current.Any(
                static item => item.Contract.Status is
                    ContractStatus.Accepted or ContractStatus.InProgress);

            if (!hasCurrentContract)
            {
                JobBoardState? board = await jobBoards
                    .GetAsync(
                        profile.Profile.Location.CurrentAirportIcao,
                        cancellationToken)
                    .ConfigureAwait(true);
                if (board is null || board.Offers.Length == 0)
                    return false;

                board.Validate();
            }

            await coordinator
                .StartAsync(definition.Id, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
