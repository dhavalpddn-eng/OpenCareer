using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class PersistedPassengerJobContractTermsSource
    : ICareerJobContractTermsSource
{
    public const string AuthorityId =
        "job-market:standard-civilian-passenger-v1";

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

        if ((requirements.RequiredCapabilities
                & AircraftCapability.Passenger)
                != AircraftCapability.Passenger
            || requirements.MinimumPassengerCapacity <= 0
            || envelope.PayloadPounds <= 0
            || Math.Abs(
                requirements.MinimumPayloadPounds
                - envelope.PayloadPounds) > 1e-9)
        {
            throw new ArgumentException(
                "Passenger offer terms require passenger capability, positive cabin capacity, and an exact positive payload requirement.",
                nameof(offer));
        }

        return Task.FromResult<CareerJobContractTermsEvidence?>(
            PersistedJobContractTermsSource.Project(
                offer,
                envelope));
    }

    private static bool Supports(
        JobMarketOfferDraft offer) =>
        offer.ServiceTrack
            == ServiceTrack.CivilianEmployment
        && offer.Scenario
            == JobScenarioKind.Standard
        && offer.Kind
            == ContractKind.Passenger;
}
