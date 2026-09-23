using OpenCareer.Application.Planning;
using OpenCareer.Application.Ownership;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Planning;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobContractTermsEvidence(
    Guid OfferId,
    AircraftMissionRequirements AircraftRequirements,
    double EstimatedFlightHours,
    double PayloadPounds,
    double DemandAttractiveness,
    double Urgency,
    double Difficulty,
    decimal EstimatedPlayerOperatingCosts = 0m,
    Guid? EmployerId = null,
    DateTimeOffset? MustStartBy = null,
    DateTimeOffset? MustCompleteBy = null,
    double ReputationReward = 1.0,
    double ReputationPenalty = 2.0,
    string? MarketId = null,
    string? WorldEventId = null,
    bool GovernmentAuthorizationRequired = false,
    PilotQualificationState? RequiredPilotQualifications = null,
    AircraftAccess AuthorizedAircraftAccess = AircraftAccess.None,
    ProviderAircraftAssignment? ProviderAircraft = null)
{
    public void ValidateIdentity(
        JobMarketOfferDraft offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(AircraftRequirements);

        if (OfferId == Guid.Empty
            || OfferId != offer.OfferId)
        {
            throw new InvalidOperationException(
                "Contract-term evidence does not belong to the requested job offer.");
        }

        AircraftRequirements.Validate();
        RequiredPilotQualifications?.Validate();
        ProviderAircraft?.Validate(
            offer.OriginIcao);

        if (offer.ContractTerms?.ProviderAircraft
            != ProviderAircraft)
        {
            throw new InvalidOperationException(
                "Contract-term provider aircraft does not match the persisted offer.");
        }

        if ((AuthorizedAircraftAccess & ~AircraftAccess.Any) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AuthorizedAircraftAccess));
        }
    }
}

public interface ICareerJobContractTermsSource
{
    Task<CareerJobContractTermsEvidence?> ReadAsync(
        JobMarketOfferDraft offer,
        PlayerCareerProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record CareerJobDispatchAuthorityEvidence(
    Guid OfferId,
    string AircraftId,
    AircraftAccess AuthorizedAccess,
    WorldEventEffects Effects,
    OperationDispatchRequirements Requirements,
    bool QualificationsVerified,
    bool GovernmentAuthorized = false,
    bool RestrictedAirspaceAuthorized = false,
    bool EmergencyOperationAuthorized = false,
    bool NavigationPlanVerified = false)
{
    public void ValidateIdentity(
        JobMarketOfferDraft offer,
        AircraftRegistryRecord aircraft)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(aircraft);
        ArgumentNullException.ThrowIfNull(Effects);
        ArgumentNullException.ThrowIfNull(Requirements);

        if (OfferId == Guid.Empty
            || OfferId != offer.OfferId)
        {
            throw new InvalidOperationException(
                "Dispatch authority evidence does not belong to the requested job offer.");
        }

        if (!string.Equals(
                AircraftId,
                aircraft.AircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Dispatch authority evidence does not belong to the selected aircraft.");
        }

        if (AuthorizedAccess == AircraftAccess.None
            || (AuthorizedAccess & ~AircraftAccess.Any) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AuthorizedAccess));
        }

        WorldEventEngine.ValidateEffects(Effects);
        Requirements.Validate();
    }
}

public interface ICareerJobDispatchAuthoritySource
{
    Task<CareerJobDispatchAuthorityEvidence?> ReadAsync(
        JobMarketOfferDraft offer,
        PlayerCareerProfile profile,
        AircraftRegistryRecord aircraft,
        CareerJobContractTermsEvidence contractTerms,
        CancellationToken cancellationToken = default);
}

public enum CareerJobStartInputState
{
    CareerUnavailable = 0,
    JobBoardUnavailable = 1,
    OfferUnavailable = 2,
    OfferLocked = 3,
    OfferInactive = 4,
    ContractAlreadyStarted = 5,
    ContractTermsUnavailable = 6,
    AircraftUnavailable = 7,
    AircraftCapabilityDataIncomplete = 8,
    DispatchAuthorityUnavailable = 9,
    QualificationsNotMet = 10,
    AircraftAccessUnauthorized = 11,
    DispatchRequirementsInvalid = 12,
    PreflightInfeasible = 13,
    PreflightDataInsufficient = 14,
    DomainDispatchGateRejected = 15,
    Ready = 16
}

public sealed record CareerJobStartInputSnapshot(
    CareerJobStartInputState State,
    Guid OfferId,
    CareerJobPlayableStartRequest? Request,
    string Detail)
{
    public bool IsReady =>
        State == CareerJobStartInputState.Ready
        && Request is not null;
}

public sealed class CareerJobStartInputSource
{
    private readonly IJobBoardStateStore _jobBoards;
    private readonly PlayerCareerRuntimeState _career;
    private readonly IJobContractStore _contracts;
    private readonly IAircraftRegistrySource _aircraftRegistry;
    private readonly OperationDispatchPlanningService _dispatchPlanning;
    private readonly ICareerJobContractTermsSource[] _contractTermSources;
    private readonly ICareerJobDispatchAuthoritySource[] _dispatchAuthoritySources;
    private readonly TimeProvider _timeProvider;
    private readonly IOwnershipStore? _ownership;

    public CareerJobStartInputSource(
        IJobBoardStateStore jobBoards,
        PlayerCareerRuntimeState career,
        IJobContractStore contracts,
        IAircraftRegistrySource aircraftRegistry,
        OperationDispatchPlanningService dispatchPlanning,
        IEnumerable<ICareerJobContractTermsSource> contractTermSources,
        IEnumerable<ICareerJobDispatchAuthoritySource> dispatchAuthoritySources,
        TimeProvider timeProvider,
        IOwnershipStore? ownership = null)
    {
        _jobBoards =
            jobBoards
            ?? throw new ArgumentNullException(nameof(jobBoards));
        _career =
            career
            ?? throw new ArgumentNullException(nameof(career));
        _contracts =
            contracts
            ?? throw new ArgumentNullException(nameof(contracts));
        _aircraftRegistry =
            aircraftRegistry
            ?? throw new ArgumentNullException(nameof(aircraftRegistry));
        _dispatchPlanning =
            dispatchPlanning
            ?? throw new ArgumentNullException(nameof(dispatchPlanning));
        _contractTermSources =
            contractTermSources?.ToArray()
            ?? throw new ArgumentNullException(nameof(contractTermSources));
        _dispatchAuthoritySources =
            dispatchAuthoritySources?.ToArray()
            ?? throw new ArgumentNullException(nameof(dispatchAuthoritySources));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _ownership = ownership;

        if (_contractTermSources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Contract-term sources cannot contain null entries.",
                nameof(contractTermSources));
        }

        if (_dispatchAuthoritySources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Dispatch-authority sources cannot contain null entries.",
                nameof(dispatchAuthoritySources));
        }
    }

    public async Task<CareerJobStartInputSnapshot> ReadAsync(
        Guid offerId,
        string aircraftId,
        Guid? selectedProviderAircraftInstanceId = null,
        CancellationToken cancellationToken = default,
        string? selectedOwnershipId = null)
    {
        if (offerId == Guid.Empty)
            throw new ArgumentException("Offer ID is required.", nameof(offerId));

        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);

        PlayerCareerProfileStoreRecord? careerRecord =
            await _career
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

        if (careerRecord is null)
        {
            return Blocked(
                CareerJobStartInputState.CareerUnavailable,
                offerId,
                "Career onboarding must be complete before a job can start.");
        }

        PlayerCareerProfile profile =
            careerRecord.Profile;

        string currentAirport =
            profile.Location.CurrentAirportIcao;

        JobBoardState? board =
            await _jobBoards
                .GetAsync(
                    currentAirport,
                    cancellationToken)
                .ConfigureAwait(false);

        if (board is null)
        {
            return Blocked(
                CareerJobStartInputState.JobBoardUnavailable,
                offerId,
                $"No authoritative job board is available for {currentAirport}.");
        }

        board.Validate();

        JobMarketOfferDraft? offer =
            board.Offers
                .SingleOrDefault(
                    candidate =>
                        candidate.OfferId
                        == offerId);

        if (offer is null)
        {
            return Blocked(
                CareerJobStartInputState.OfferUnavailable,
                offerId,
                "The requested offer is not active on the authoritative local job board.");
        }

        if (offer.IsLockedPreview)
        {
            return Blocked(
                CareerJobStartInputState.OfferLocked,
                offerId,
                "A locked job preview cannot be accepted or started.");
        }

        if (!string.Equals(
                offer.OriginIcao,
                currentAirport,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The authoritative local board returned an offer from a different origin.");
        }

        DateTimeOffset now =
            _timeProvider.GetUtcNow();

        PersistedJobContract? existing =
            await _contracts
                .ReadJobContractAsync(
                    offerId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Validate();

            if (existing.Contract.Status
                is ContractStatus.InProgress
                    or ContractStatus.Completed
                    or ContractStatus.Failed
                    or ContractStatus.Cancelled
                    or ContractStatus.Expired)
            {
                return Blocked(
                    CareerJobStartInputState.ContractAlreadyStarted,
                    offerId,
                    $"Contract is already in state {existing.Contract.Status}.");
            }
        }

        DateTimeOffset acceptanceTime =
            existing?.Contract.Status
                == ContractStatus.Accepted
            && existing.Contract.AcceptedAt
                is { } acceptedAt
                ? acceptedAt
                : now;

        if (acceptanceTime < offer.OfferedAt
            || acceptanceTime >= offer.ExpiresAt)
        {
            return Blocked(
                CareerJobStartInputState.OfferInactive,
                offerId,
                "The authoritative job offer is not active at the acceptance time.");
        }

        CareerJobContractTermsEvidence? terms =
            await ReadSingleContractTermsAsync(
                    offer,
                    profile,
                    cancellationToken)
                .ConfigureAwait(false);

        if (terms is null)
        {
            return Blocked(
                CareerJobStartInputState.ContractTermsUnavailable,
                offerId,
                "No authoritative source produced the contract terms required to accept this offer.");
        }

        terms.ValidateIdentity(offer);

        bool providerAircraftSelected =
            selectedProviderAircraftInstanceId is not null;

        if (providerAircraftSelected
            && (terms.ProviderAircraft is not { } providerAircraft
                || providerAircraft.ProviderAircraftInstanceId
                    != selectedProviderAircraftInstanceId
                || !string.Equals(
                    providerAircraft.AircraftId,
                    aircraftId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return Blocked(
                CareerJobStartInputState.AircraftUnavailable,
                offerId,
                "The selected provider-aircraft instance does not belong to this contract and canonical aircraft identity.");
        }

        var creationRequest =
            new JobContractCreationRequest(
                offer,
                acceptanceTime,
                terms.AircraftRequirements,
                terms.EstimatedFlightHours,
                terms.PayloadPounds,
                terms.DemandAttractiveness,
                terms.Urgency,
                terms.Difficulty,
                terms.EstimatedPlayerOperatingCosts,
                terms.EmployerId,
                terms.MustStartBy,
                terms.MustCompleteBy,
                terms.ReputationReward,
                terms.ReputationPenalty,
                terms.MarketId,
                terms.WorldEventId,
                terms.GovernmentAuthorizationRequired,
                terms.ProviderAircraft);

        creationRequest.Validate();

        AircraftRegistryResolution? resolution =
            await _aircraftRegistry
                .FindAircraftAsync(
                    aircraftId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (resolution is null
            || (!providerAircraftSelected
                && resolution.InstallationStatus
                    != AircraftInstallationStatus.Installed))
        {
            return Blocked(
                CareerJobStartInputState.AircraftUnavailable,
                offerId,
                resolution is null
                    ? $"Selected aircraft '{aircraftId}' has no registry resolution."
                    : $"Selected aircraft '{aircraftId}' resolved with installation status {resolution.InstallationStatus}, not Installed.");
        }

        AircraftRegistryRecord? aircraft =
            resolution.TryCreateRegistryRecord()
            ?? TryCreateConservativeJobRecord(
                resolution,
                requireInstalled:
                    !providerAircraftSelected);

        if (aircraft is null)
        {
            string unresolved =
                string.Join(
                    ", ",
                    resolution.UnresolvedCapabilityFields);

            return Blocked(
                CareerJobStartInputState.AircraftCapabilityDataIncomplete,
                offerId,
                $"Selected aircraft '{resolution.CanonicalAircraftId}' resolved as {resolution.InstallationStatus}, but no safe job capability projection can be created. TryCreateRegistryRecord=false. Unresolved capability fields: {unresolved}.");
        }

        aircraft.Validate();

        CareerJobDispatchAuthorityEvidence? dispatchAuthority =
            await ReadSingleDispatchAuthorityAsync(
                    offer,
                    profile,
                    aircraft,
                    terms,
                    cancellationToken)
                .ConfigureAwait(false);

        if (dispatchAuthority is null)
        {
            return Blocked(
                CareerJobStartInputState.DispatchAuthorityUnavailable,
                offerId,
                "No authoritative source produced qualification, authorization, world-state, and operation requirements for this job.");
        }

        dispatchAuthority.ValidateIdentity(
            offer,
            aircraft);

        if (!dispatchAuthority.QualificationsVerified)
        {
            return Blocked(
                CareerJobStartInputState.QualificationsNotMet,
                offerId,
                $"Career qualifications do not satisfy the job. Current={profile.Qualifications}; Required={terms.RequiredPilotQualifications?.ToString() ?? "unspecified"}.");
        }

        if (terms.AuthorizedAircraftAccess == AircraftAccess.None
            || (dispatchAuthority.AuthorizedAccess
                & ~terms.AuthorizedAircraftAccess) != 0
            || (dispatchAuthority.AuthorizedAccess
                & aircraft.Capabilities.Access
                & terms.AircraftRequirements.AllowedAccess) == 0)
        {
            return Blocked(
                CareerJobStartInputState.AircraftAccessUnauthorized,
                offerId,
                $"Aircraft access is unauthorized. Aircraft={aircraft.Capabilities.Access}; Authorized={dispatchAuthority.AuthorizedAccess}; ContractAuthorized={terms.AuthorizedAircraftAccess}; Allowed={terms.AircraftRequirements.AllowedAccess}.");
        }

        if (dispatchAuthority.Requirements.PayloadPounds + 1e-9
                < creationRequest.PayloadPounds
            || dispatchAuthority.Requirements.RequiredRangeNauticalMiles + 1e-9
                < offer.DistanceNm)
        {
            return Blocked(
                CareerJobStartInputState.DispatchRequirementsInvalid,
                offerId,
                "Authoritative dispatch requirements cannot understate the accepted job payload or route distance.");
        }

        DispatchFeasibilityResult preflight =
            providerAircraftSelected
                ? await _dispatchPlanning
                    .EvaluateRegisteredAircraftAsync(
                        aircraft.AircraftId,
                        offer.OriginIcao,
                        offer.DestinationIcao,
                        dispatchAuthority.Requirements,
                        reservationId:
                            null,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await _dispatchPlanning
                    .EvaluateAsync(
                        aircraft.AircraftId,
                        offer.OriginIcao,
                        offer.DestinationIcao,
                        dispatchAuthority.Requirements,
                        cancellationToken)
                    .ConfigureAwait(false);

        if (preflight.Status
            == DispatchFeasibilityStatus.Infeasible)
        {
            return Blocked(
                CareerJobStartInputState.PreflightInfeasible,
                offerId,
                $"Authoritative preflight status={preflight.Status}; issues={FormatPreflightIssues(preflight)}.");
        }

        if (preflight.Status
            != DispatchFeasibilityStatus.Feasible)
        {
            return Blocked(
                CareerJobStartInputState.PreflightDataInsufficient,
                offerId,
                $"Authoritative preflight status={preflight.Status}; issues={FormatPreflightIssues(preflight)}.");
        }

        var context =
            new ContractDispatchContext(
                acceptanceTime,
                aircraft.Capabilities,
                dispatchAuthority.AuthorizedAccess,
                dispatchAuthority.Effects,
                dispatchAuthority.QualificationsVerified,
                DispatchFeasibilityVerified:
                    true,
                dispatchAuthority.GovernmentAuthorized,
                dispatchAuthority.RestrictedAirspaceAuthorized,
                dispatchAuthority.EmergencyOperationAuthorized,
                dispatchAuthority.NavigationPlanVerified);

        try
        {
            _ =
                JobContractFactory
                    .Create(creationRequest)
                    .Accept(context);
        }
        catch (InvalidOperationException ex)
        {
            return Blocked(
                CareerJobStartInputState.DomainDispatchGateRejected,
                offerId,
                ex.Message);
        }

        if (providerAircraftSelected == (selectedOwnershipId is not null))
            return Blocked(CareerJobStartInputState.AircraftUnavailable, offerId,
                "Select exactly one individual owned or provider airframe.");

        if (selectedOwnershipId is not null)
        {
            if (_ownership is null)
                throw new InvalidOperationException("Ownership authority is unavailable.");

            OwnershipSnapshot owned = await _ownership.LoadSnapshotAsync(
                profile.CareerId.ToString("D"), cancellationToken).ConfigureAwait(false);
            if (!owned.Aircraft.Any(item =>
                    item.OwnershipId == selectedOwnershipId
                    && item.CareerId == profile.CareerId.ToString("D")
                    && item.Status == OwnedAircraftStatus.Active
                    && string.Equals(item.AircraftId, aircraftId, StringComparison.OrdinalIgnoreCase)))
                return Blocked(CareerJobStartInputState.AircraftUnavailable, offerId,
                    "The selected owned airframe is not active for this career and aircraft model.");
        }

        return new(
            CareerJobStartInputState.Ready,
            offerId,
            new CareerJobPlayableStartRequest(
                creationRequest,
                context,
                dispatchAuthority.Requirements,
                selectedProviderAircraftInstanceId,
                selectedOwnershipId),
            "Authoritative contract terms, selected aircraft, dispatch authority, and physical preflight are ready.");
    }

    private static AircraftRegistryRecord? TryCreateConservativeJobRecord(
        AircraftRegistryResolution resolution,
        bool requireInstalled)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if ((requireInstalled
                && resolution.InstallationStatus
                    != AircraftInstallationStatus.Installed)
            || resolution.CapabilityValues.Access is not { } access
            || access == AircraftAccess.None)
        {
            return null;
        }

        ResolvedAircraftCapabilities values =
            resolution.CapabilityValues;

        var profile =
            new AircraftCapabilityProfile(
                resolution.CanonicalAircraftId,
                string.IsNullOrWhiteSpace(values.DisplayName)
                    ? resolution.CanonicalAircraftId
                    : values.DisplayName,
                values.Capabilities
                    ?? AircraftCapability.None,
                access,
                values.MaximumPayloadPounds
                    ?? 0,
                values.MaximumRangeNauticalMiles
                    ?? 0,
                values.TypicalCruiseKnots
                    ?? 0,
                values.Seats
                    ?? 0,
                values.EngineCount
                    ?? 0,
                values.IfrCapable
                    ?? false,
                values.Pressurized
                    ?? false,
                values.RetractableGear
                    ?? false);

        var record =
            new AircraftRegistryRecord(
                profile,
                IsInstalled:
                    resolution.InstallationStatus
                    == AircraftInstallationStatus.Installed,
                resolution.RunwayPerformance);

        record.Validate();
        return record;
    }

    private static string FormatPreflightIssues(
        DispatchFeasibilityResult preflight) =>
        preflight.Issues.Count == 0
            ? "none"
            : string.Join(
                ", ",
                preflight.Issues
                    .Select(
                        static issue =>
                            issue.AircraftField is { } field
                                ? $"{issue.Reason}[{field}]"
                                : issue.Reason.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(
                        static value =>
                            value,
                        StringComparer.Ordinal));

    private async Task<CareerJobContractTermsEvidence?> ReadSingleContractTermsAsync(
        JobMarketOfferDraft offer,
        PlayerCareerProfile profile,
        CancellationToken cancellationToken)
    {
        var matches =
            new List<CareerJobContractTermsEvidence>();

        foreach (ICareerJobContractTermsSource source in _contractTermSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CareerJobContractTermsEvidence? evidence =
                await source
                    .ReadAsync(
                        offer,
                        profile,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (evidence is not null)
                matches.Add(evidence);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple contract-term sources claimed authority for one job offer.")
        };
    }

    private async Task<CareerJobDispatchAuthorityEvidence?> ReadSingleDispatchAuthorityAsync(
        JobMarketOfferDraft offer,
        PlayerCareerProfile profile,
        AircraftRegistryRecord aircraft,
        CareerJobContractTermsEvidence terms,
        CancellationToken cancellationToken)
    {
        var matches =
            new List<CareerJobDispatchAuthorityEvidence>();

        foreach (ICareerJobDispatchAuthoritySource source in _dispatchAuthoritySources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CareerJobDispatchAuthorityEvidence? evidence =
                await source
                    .ReadAsync(
                        offer,
                        profile,
                        aircraft,
                        terms,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (evidence is not null)
                matches.Add(evidence);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple dispatch-authority sources claimed authority for one job offer.")
        };
    }

    private static CareerJobStartInputSnapshot Blocked(
        CareerJobStartInputState state,
        Guid offerId,
        string detail) =>
        new(
            state,
            offerId,
            Request:
                null,
            detail);
}
