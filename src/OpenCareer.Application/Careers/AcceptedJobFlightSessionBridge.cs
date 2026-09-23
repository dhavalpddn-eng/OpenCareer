using OpenCareer.Application.Flights;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public sealed record StartedJobFlightSessionResult(
    PersistedJobContract Contract,
    FlightSession FlightSession);

public sealed class AcceptedJobFlightSessionBridge
{
    private readonly AcceptedJobStartBridge _contractStart;
    private readonly IJobContractStore _contractStore;
    private readonly FlightSessionPersistenceService _flightSessionPersistence;
    private readonly FlightSessionCoordinator _flightSessionCoordinator;
    private readonly ILiveAircraftIdentitySource _liveAircraft;
    private readonly IContractAirframeSelectionStore? _airframeSelections;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AcceptedJobFlightSessionBridge(
        AcceptedJobStartBridge contractStart,
        IJobContractStore contractStore,
        FlightSessionPersistenceService flightSessionPersistence,
        FlightSessionCoordinator flightSessionCoordinator,
        ILiveAircraftIdentitySource liveAircraft,
        IContractAirframeSelectionStore? airframeSelections = null)
    {
        _contractStart =
            contractStart
            ?? throw new ArgumentNullException(nameof(contractStart));
        _contractStore =
            contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));
        _flightSessionPersistence =
            flightSessionPersistence
            ?? throw new ArgumentNullException(nameof(flightSessionPersistence));
        _flightSessionCoordinator =
            flightSessionCoordinator
            ?? throw new ArgumentNullException(nameof(flightSessionCoordinator));
        _liveAircraft =
            liveAircraft
            ?? throw new ArgumentNullException(nameof(liveAircraft));
        _airframeSelections = airframeSelections;
    }

    public async Task<StartedJobFlightSessionResult> StartAsync(
        AcceptedJobDispatchResult acceptedDispatch,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default,
        string? selectedOwnershipId = null,
        Guid? selectedProviderAircraftInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(acceptedDispatch);
        ArgumentNullException.ThrowIfNull(context);

        PersistedJobContract retainedAccepted =
            acceptedDispatch.FleetResult.AcceptedContract
            ?? throw new InvalidOperationException(
                "A contract-linked flight session requires the retained accepted contract.");

        retainedAccepted.Validate();

        string aircraftId = acceptedDispatch.FleetResult.CanonicalAircraftId
            ?? throw new InvalidOperationException("Selected canonical aircraft identity is missing.");
        FlightSessionAircraftIdentity? requestedIdentity = selectedOwnershipId is { Length: > 0 }
            && selectedProviderAircraftInstanceId is null
            ? new(FlightSessionAircraftKind.Owned, selectedOwnershipId, aircraftId)
            : selectedOwnershipId is null && selectedProviderAircraftInstanceId is { } providerId
                && retainedAccepted.Contract.ProviderAircraft?.ProviderAircraftInstanceId == providerId
                ? new(FlightSessionAircraftKind.Provider, providerId.ToString("D"), aircraftId)
                : null;
        requestedIdentity?.Validate();

        Guid contractId =
            retainedAccepted.Contract.ContractId;

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSessionAircraftIdentity identity = requestedIdentity
                ?? (await (_airframeSelections?.ReadAsync(contractId, cancellationToken)
                    ?? Task.FromResult<FlightSessionAircraftIdentity?>(null)).ConfigureAwait(false))
                ?? throw new InvalidOperationException("No durable individual airframe selection exists for this contract.");
            identity.Validate();
            if (!string.Equals(identity.AircraftId, aircraftId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected airframe does not match the contract aircraft model.");

            PersistedJobContract authoritative =
                await _contractStore
                    .ReadJobContractAsync(
                        contractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The authoritative job contract was not found.");

            authoritative.Validate();
            ValidateSameAcceptedContract(
                retainedAccepted.Contract,
                authoritative.Contract);

            FlightSession? current =
                _flightSessionCoordinator.Current;

            if (current is not null)
            {
                if (current.AircraftIdentity != identity)
                    throw new InvalidOperationException("A replay cannot switch the FlightSession airframe.");
                if (current.IsTerminal)
                {
                    throw new InvalidOperationException(
                        "The previous terminal FlightSession must complete its owning terminal workflow before another job flight can start.");
                }

                if (current.ContractId
                    != contractId)
                {
                    throw new InvalidOperationException(
                        "A different active FlightSession already owns the simulator operation.");
                }
            }

            bool requiresLiveAircraftValidation =
                authoritative.Contract.Status == ContractStatus.Accepted
                || (authoritative.Contract.Status == ContractStatus.InProgress
                    && current is null);

            if (requiresLiveAircraftValidation)
            {
                ValidateLiveAircraft(
                    acceptedDispatch);
            }

            if (_airframeSelections is not null)
                await _airframeSelections.SaveAsync(contractId, identity, cancellationToken)
                    .ConfigureAwait(false);

            PersistedJobContract started =
                authoritative.Contract.Status switch
                {
                    ContractStatus.Accepted =>
                        await _contractStart
                            .StartAsync(
                                acceptedDispatch,
                                context,
                                cancellationToken)
                            .ConfigureAwait(false),

                    ContractStatus.InProgress =>
                        authoritative,

                    _ =>
                        throw new InvalidOperationException(
                            $"Contract {contractId:D} cannot establish a FlightSession from state {authoritative.Contract.Status}.")
                };

            if (current is not null)
            {
                return new(
                    started,
                    current);
            }

            DateTimeOffset startedAt =
                started.Contract.StartedAt
                ?? throw new InvalidOperationException(
                    "An InProgress contract must retain its authoritative start time.");

            var plan =
                new FlightSessionPlan(
                    PlannedOrigin:
                        started.Contract.OriginIcao,
                    PlannedDestination:
                        started.Contract.DestinationIcao,
                    SourceProvider:
                        "OpenCareer.JobContract",
                    SourceReference:
                        contractId.ToString("D"));

            FlightSession session =
                await _flightSessionPersistence
                    .StartAsync(
                        startedAt,
                        contractId,
                        sessionId:
                            GetFlightSessionId(contractId),
                        plan,
                        cancellationToken,
                        identity)
                    .ConfigureAwait(false);

            return new(
                started,
                session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static Guid GetFlightSessionId(
        Guid contractId)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        // The current playable loop owns one FlightSession per JobContract.
        return contractId;
    }

    private void ValidateLiveAircraft(
        AcceptedJobDispatchResult acceptedDispatch)
    {
        string? selectedAircraftId =
            acceptedDispatch.FleetResult.CanonicalAircraftId;

        if (string.IsNullOrWhiteSpace(selectedAircraftId))
        {
            throw new InvalidOperationException(
                "The selected OpenCareer aircraft is missing canonical identity.");
        }

        selectedAircraftId =
            selectedAircraftId.Trim();

        string? liveTitle =
            _liveAircraft.CurrentAircraftTitle;

        if (string.IsNullOrWhiteSpace(liveTitle))
        {
            throw new InvalidOperationException(
                "MSFS is not ready with a current aircraft. Load the selected aircraft in MSFS.");
        }

        string liveAircraftId =
            AircraftCanonicalIdentity.FromMsfsTitle(
                liveTitle);

        if (!string.Equals(
                liveAircraftId,
                selectedAircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Load the selected aircraft in MSFS. Selected='{selectedAircraftId}'; Loaded='{liveAircraftId}'.");
        }
    }

    private static void ValidateSameAcceptedContract(
        JobContract retainedAccepted,
        JobContract authoritative)
    {
        if (retainedAccepted.Status
            != ContractStatus.Accepted)
        {
            throw new InvalidOperationException(
                "The retained dispatch contract must represent the Accepted state.");
        }

        JobContract normalized =
            authoritative with
            {
                Status =
                    ContractStatus.Accepted,
                StartedAt =
                    null,
                CompletedAt =
                    null
            };

        if (normalized
            != retainedAccepted)
        {
            throw new InvalidOperationException(
                "The retained accepted contract no longer matches authoritative job-contract state.");
        }
    }
}
