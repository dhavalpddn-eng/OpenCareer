using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class PersistedJobContractTermsSource
    : ICareerJobContractTermsSource
{
    public const string AuthorityId =
        "job-market:standard-civilian-point-to-point-v2";

    public Task<CareerJobContractTermsEvidence?> ReadAsync(
        JobMarketOfferDraft offer,
        PlayerCareerProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(profile);

        cancellationToken.ThrowIfCancellationRequested();
        profile.Validate();

        JobMarketContractTermsEnvelope? envelope =
            offer.ContractTerms;

        if (envelope is null
            || !string.Equals(
                envelope.AuthorityId,
                AuthorityId,
                StringComparison.Ordinal)
            || !Supports(offer))
        {
            return Task.FromResult<CareerJobContractTermsEvidence?>(
                null);
        }

        envelope.ValidateForOffer(
            offer);

        return Task.FromResult<CareerJobContractTermsEvidence?>(
            new(
                offer.OfferId,
                envelope.AircraftRequirements,
                envelope.EstimatedFlightHours,
                envelope.PayloadPounds,
                envelope.DemandAttractiveness,
                envelope.Urgency,
                envelope.Difficulty,
                envelope.EstimatedPlayerOperatingCosts,
                envelope.EmployerId,
                envelope.MustStartBy,
                envelope.MustCompleteBy,
                envelope.ReputationReward,
                envelope.ReputationPenalty,
                envelope.MarketId,
                envelope.WorldEventId,
                envelope.GovernmentAuthorizationRequired,
                envelope.RequiredPilotQualifications,
                envelope.AuthorizedAircraftAccess));
    }

    private static bool Supports(
        JobMarketOfferDraft offer) =>
        offer.ServiceTrack
            == ServiceTrack.CivilianEmployment
        && offer.Scenario
            == JobScenarioKind.Standard
        && offer.Kind is
            ContractKind.Ferry
                or ContractKind.Reposition;
}
