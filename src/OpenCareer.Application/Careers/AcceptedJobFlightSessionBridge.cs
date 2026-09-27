using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
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
    private readonly PhysicalAirframeEligibilityService _physicalAirframes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AcceptedJobFlightSessionBridge(
        AcceptedJobStartBridge contractStart,
        IJobContractStore contractStore,
        FlightSessionPersistenceService flightSessionPersistence,
        FlightSessionCoordinator flightSessionCoordinator,
        PhysicalAirframeEligibilityService? physicalAirframes = null)
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
        _physicalAirframes = physicalAirframes ?? new();
    }

    public async Task<StartedJobFlightSessionResult> StartAsync(
        AcceptedJobDispatchResult acceptedDispatch,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default,
        AirframeId? physicalAirframeId = null)
    {
        ArgumentNullException.ThrowIfNull(acceptedDispatch);
        ArgumentNullException.ThrowIfNull(context);

        PersistedJobContract retainedAccepted =
            acceptedDispatch.FleetResult.AcceptedContract
            ?? throw new InvalidOperationException(
                "A contract-linked flight session requires the retained accepted contract.");

        retainedAccepted.Validate();

        Guid contractId =
            retainedAccepted.Contract.ContractId;

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
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

            // Fleet has already resolved provider aliases to the model actually reserved for dispatch.
            // Validate any explicit physical assignment before Accepted can become InProgress.
            FlightSessionAircraftIdentity aircraftIdentity =
                await ResolveAircraftIdentityAsync(
                        acceptedDispatch.FleetResult.CanonicalAircraftId,
                        physicalAirframeId,
                        cancellationToken)
                    .ConfigureAwait(false);

            FlightSession? current =
                _flightSessionCoordinator.Current;

            if (current is not null)
            {
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

                // Legacy checkpoints have no aircraft identity. Keep that absence rather than inventing
                // history; only a model-only replay is compatible with such a checkpoint.
                if (current.AircraftIdentity != aircraftIdentity
                    && (current.AircraftIdentity is not null || physicalAirframeId is not null))
                {
                    throw new InvalidOperationException(
                        "The active FlightSession has a different aircraft assignment.");
                }
            }

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
                        aircraftIdentity: aircraftIdentity)
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

    private async Task<FlightSessionAircraftIdentity> ResolveAircraftIdentityAsync(
        string? canonicalAircraftId,
        AirframeId? physicalAirframeId,
        CancellationToken cancellationToken)
    {
        var identity = new FlightSessionAircraftIdentity(
            canonicalAircraftId ?? throw new InvalidOperationException("Fleet reservation has no canonical aircraft identity."),
            physicalAirframeId);

        // Re-read after acceptance/dispatch, against Fleet's actual reserved canonical model.
        // A newly grounded assignment leaves Accepted recovery state; it cannot start a session.
        await _physicalAirframes.RequireEligibleAsync(identity.CanonicalAircraftId, physicalAirframeId,
            cancellationToken).ConfigureAwait(false);
        return identity;
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
