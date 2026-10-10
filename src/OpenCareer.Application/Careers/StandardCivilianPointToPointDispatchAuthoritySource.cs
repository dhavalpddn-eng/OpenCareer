using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Careers;

public sealed class StandardCivilianPointToPointDispatchAuthoritySource
    : ICareerJobDispatchAuthoritySource
{
    public Task<CareerJobDispatchAuthorityEvidence?> ReadAsync(
        JobMarketOfferDraft offer,
        PlayerCareerProfile profile,
        AircraftRegistryRecord aircraft,
        CareerJobContractTermsEvidence contractTerms,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(contractTerms);

        cancellationToken.ThrowIfCancellationRequested();

        profile.Validate();
        aircraft.Validate();
        contractTerms.ValidateIdentity(offer);

        if (!Supports(offer)
            || offer.ContractTerms is not { } envelope
            || !string.Equals(
                envelope.AuthorityId,
                PersistedJobContractTermsSource.AuthorityId,
                StringComparison.Ordinal)
            || contractTerms.RequiredPilotQualifications is null
            || contractTerms.AuthorizedAircraftAccess == AircraftAccess.None
            || contractTerms.WorldEventId is not null
            || contractTerms.GovernmentAuthorizationRequired)
        {
            return Task.FromResult<CareerJobDispatchAuthorityEvidence?>(
                null);
        }

        PilotQualificationState required =
            contractTerms.RequiredPilotQualifications;

        required.Validate();

        bool qualificationsVerified =
            profile.Qualifications.License >= required.License
            && profile.Qualifications.Ratings.IsSupersetOf(
                required.Ratings);

        AircraftAccess authorizedAccess =
            contractTerms.AuthorizedAircraftAccess;

        if ((authorizedAccess
                & aircraft.Capabilities.Access
                & contractTerms.AircraftRequirements.AllowedAccess) == 0)
        {
            return Task.FromResult<CareerJobDispatchAuthorityEvidence?>(
                null);
        }

        var requirements =
            new OperationDispatchRequirements(
                PayloadPounds:
                    contractTerms.PayloadPounds,
                RequiredRangeNauticalMiles:
                    Math.Max(
                        offer.DistanceNm,
                        contractTerms.AircraftRequirements
                            .MinimumRangeNauticalMiles));

        requirements.Validate();

        return Task.FromResult<CareerJobDispatchAuthorityEvidence?>(
            new(
                offer.OfferId,
                aircraft.AircraftId,
                authorizedAccess,
                new WorldEventEffects(),
                requirements,
                qualificationsVerified));
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
