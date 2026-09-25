using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

/// <summary>
/// Explicit UI-only development offer seed. It does not accept, reserve,
/// dispatch, start, complete, or fabricate any part of a flight.
/// </summary>
public sealed class DevelopmentFlightService
{
    private readonly JobBoardGenerationService _boards;
    private readonly PlayerCareerRuntimeState _career;
    private readonly IJobContractRecoverySource _contracts;
    private readonly IFlightSessionCheckpointStore _checkpoints;
    private readonly TimeProvider _clock;
    private readonly PlayerCareerLocationCoordinator _location;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DevelopmentFlightService(
        JobBoardGenerationService boards,
        PlayerCareerRuntimeState career,
        IJobContractRecoverySource contracts,
        IFlightSessionCheckpointStore checkpoints,
        TimeProvider clock,
        PlayerCareerLocationCoordinator location)
    {
        _boards = boards
            ?? throw new ArgumentNullException(nameof(boards));
        _career = career
            ?? throw new ArgumentNullException(nameof(career));
        _contracts = contracts
            ?? throw new ArgumentNullException(nameof(contracts));
        _checkpoints = checkpoints
            ?? throw new ArgumentNullException(nameof(checkpoints));
        _clock = clock
            ?? throw new ArgumentNullException(nameof(clock));
        _location = location
            ?? throw new ArgumentNullException(nameof(location));
    }

    public async Task<JobMarketOfferDraft> GenerateAsync(
        bool positionPilotAtKjfk = false,
        CancellationToken cancellationToken = default)
    {
        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PlayerCareerProfileStoreRecord profile =
                await _career
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Complete career onboarding before generating a development flight.");

            if (await _checkpoints
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(false)
                is not null)
            {
                throw new InvalidOperationException(
                    "Finish the current flight and terminal cleanup before generating a development flight.");
            }

            IReadOnlyList<JobContractRecoveryCandidate> candidates =
                await _contracts
                    .ReadRecoveryCandidatesAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (candidates.Any(
                    static candidate =>
                        candidate.Status is
                            ContractStatus.Accepted
                                or ContractStatus.InProgress))
            {
                throw new InvalidOperationException(
                    "An accepted or in-progress career contract already exists.");
            }

            DateTimeOffset now =
                _clock.GetUtcNow();

            if (now < profile.SavedAt)
                now = profile.SavedAt;

            if (now < profile.Profile.Location.UpdatedAt)
                now = profile.Profile.Location.UpdatedAt;

            if (!string.Equals(
                    profile.Profile.Location.CurrentAirportIcao,
                    "KJFK",
                    StringComparison.Ordinal))
            {
                if (!positionPilotAtKjfk)
                {
                    throw new InvalidOperationException(
                        "The career pilot is not at KJFK. Select the explicit development-positioning option first.");
                }

                await _location
                    .UpdateAsync(
                        profile.Profile.Location with
                        {
                            CurrentAirportIcao = "KJFK",
                            Connections =
                                profile.Profile.Location.Connections.Add(
                                    "KJFK"),
                            UpdatedAt = now
                        },
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return await _boards
                .AddDevelopmentOfferAsync(
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
