using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

/// <summary>
/// Resolves dispatch candidates from career-supported aircraft, independently of
/// the installed MSFS catalog and the aircraft currently loaded in the simulator.
/// An offer may carry an older, already assigned provider instance; keep it stable.
/// </summary>
public sealed class ProviderAircraftAssignmentResolver
{
    private readonly PlayableLoopAircraftRegistrySource _supported = new();

    public async Task<ProviderAircraftAssignment?> ResolveAsync(
        JobMarketOfferDraft offer,
        AircraftMissionRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(requirements);
        requirements.Validate();

        if (offer.ContractTerms?.ProviderAircraft is { } legacy)
        {
            legacy.Validate(offer.OriginIcao);
            return legacy;
        }

        if (offer.ServiceTrack != ServiceTrack.CivilianEmployment
            || offer.Kind is not (ContractKind.Ferry or ContractKind.Reposition)
            || offer.Scenario != JobScenarioKind.Standard
            || !await IsEligibleAsync(
                    PlayableLoopAircraftRegistrySource.AircraftId,
                    requirements,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        return ProviderAircraftAssignment.CreateForOffer(
            offer.OfferId,
            new ProviderAircraftType(
                PlayableLoopAircraftRegistrySource.AircraftId,
                PlayableLoopAircraftRegistrySource.AircraftTitle),
            offer.OriginIcao);
    }

    public async Task<bool> IsEligibleAsync(
        string aircraftId,
        AircraftMissionRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);
        ArgumentNullException.ThrowIfNull(requirements);
        requirements.Validate();

        IReadOnlyList<AircraftRegistryObservation> observations =
            await _supported.FindAircraftObservationsAsync(
                    aircraftId,
                    cancellationToken)
                .ConfigureAwait(false);

        return observations.Count != 0
            && AircraftRegistryResolver.Resolve(observations)
                .TryCreateRegistryRecord()?.Capabilities.Satisfies(requirements) == true;
    }
}
