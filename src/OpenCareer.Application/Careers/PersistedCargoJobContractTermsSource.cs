using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class PersistedCargoJobContractTermsSource
    : ICareerJobContractTermsSource
{
    public const string AuthorityId =
        "job-market:standard-civilian-cargo-v1";

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
            return Task.FromResult<CareerJobContractTermsEvidence?>(null);
        }

        envelope.ValidateForOffer(offer);

        AircraftMissionRequirements requirements =
            envelope.AircraftRequirements;

        if ((requirements.RequiredCapabilities & AircraftCapability.Cargo)
                != AircraftCapability.Cargo
            || envelope.EffectiveCargoLots.IsEmpty
            || envelope.PayloadPounds <= 0
            || Math.Abs(
                requirements.MinimumPayloadPounds
                - envelope.PayloadPounds) > 1e-9)
        {
            throw new ArgumentException(
                "Cargo offer terms require cargo capability, named lots, and an exact positive payload requirement.",
                nameof(offer));
        }

        return Task.FromResult<CareerJobContractTermsEvidence?>(
            PersistedJobContractTermsSource.Project(
                offer,
                envelope));
    }

    private static bool Supports(JobMarketOfferDraft offer) =>
        offer.ServiceTrack == ServiceTrack.CivilianEmployment
        && offer.Scenario == JobScenarioKind.Standard
        && offer.Kind is
            ContractKind.Cargo
                or ContractKind.ExpressCargo
                or ContractKind.AogPartsDelivery;
}
