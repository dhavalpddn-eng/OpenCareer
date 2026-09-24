using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

/// <summary>Explicit UI-only seed. Does not accept, reserve, start, or complete a flight.</summary>
public sealed class DevelopmentFlightService(
    JobBoardGenerationService boards,
    PlayerCareerRuntimeState career,
    IJobContractRecoverySource contracts,
    IFlightSessionCheckpointStore checkpoints,
    TimeProvider clock,
    PlayerCareerLocationCoordinator location)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<JobMarketOfferDraft> GenerateAsync(
        bool positionPilotAtKjfk = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PlayerCareerProfileStoreRecord profile = await career.InitializeAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Complete career onboarding first.");
            if (await checkpoints.LoadAsync(cancellationToken).ConfigureAwait(false) is not null)
                throw new InvalidOperationException("Finish the current flight and terminal cleanup before generating a test.");
            var candidates = await contracts.ReadRecoveryCandidatesAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Any(c => c.Status is ContractStatus.Accepted or ContractStatus.InProgress))
                throw new InvalidOperationException("An accepted or in-progress contract already exists.");

            DateTimeOffset now = clock.GetUtcNow();
            if (now < profile.SavedAt) now = profile.SavedAt;
            if (now < profile.Profile.Location.UpdatedAt) now = profile.Profile.Location.UpdatedAt;
            if (profile.Profile.Location.CurrentAirportIcao != "KJFK")
            {
                if (!positionPilotAtKjfk)
                    throw new InvalidOperationException("Pilot is not at KJFK. Explicitly select the development positioning checkbox first.");
                // Explicit development positioning: no simulated flight, home-base move,
                // ownership change, aircraft relocation, or fabricated travel contract.
                await location.UpdateAsync(profile.Profile.Location with
                {
                    CurrentAirportIcao = "KJFK",
                    Connections = profile.Profile.Location.Connections.Add("KJFK"),
                    UpdatedAt = now
                }, now, cancellationToken).ConfigureAwait(false);
            }
            return await boards.AddDevelopmentOfferAsync(now, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
