using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public sealed record StartedJobFlightSessionResult(
    PersistedJobContract Contract,
    FlightSession FlightSession);

public sealed class AcceptedJobFlightSessionBridge
{
    private const string JobContractSourceProvider =
        "OpenCareer.JobContract";

    private readonly AcceptedJobStartBridge _contractStart;
    private readonly IJobContractStore _contractStore;
    private readonly FlightSessionPersistenceService _flightSessionPersistence;
    private readonly FlightSessionCoordinator _flightSessionCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AcceptedJobFlightSessionBridge(
        AcceptedJobStartBridge contractStart,
        IJobContractStore contractStore,
        FlightSessionPersistenceService flightSessionPersistence,
        FlightSessionCoordinator flightSessionCoordinator)
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
    }

    public async Task<StartedJobFlightSessionResult> StartAsync(
        AcceptedJobDispatchResult acceptedDispatch,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acceptedDispatch);
        ArgumentNullException.ThrowIfNull(context);

        PersistedJobContract retainedAccepted =
            acceptedDispatch.FleetResult.AcceptedContract
            ?? throw new InvalidOperationException(
                "A contract-linked flight session requires the retained accepted contract.");

        retainedAccepted.Validate();

        string expectedCanonicalAircraftId =
            RequireCanonicalAircraftId(
                acceptedDispatch.FleetResult.CanonicalAircraftId);

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

            FlightSession? current =
                _flightSessionCoordinator.Current;

            if (current is not null)
            {
                ValidateRetainedFlightSession(
                    current,
                    authoritative.Contract,
                    expectedCanonicalAircraftId,
                    context.Time);
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
                        JobContractSourceProvider,
                    SourceReference:
                        contractId.ToString("D"),
                    ExpectedCanonicalAircraftId:
                        expectedCanonicalAircraftId);

            FlightSession session =
                await _flightSessionPersistence
                    .StartAsync(
                        startedAt,
                        contractId,
                        sessionId:
                            GetFlightSessionId(contractId),
                        plan,
                        cancellationToken)
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

    private static string RequireCanonicalAircraftId(
        string? canonicalAircraftId)
    {
        if (string.IsNullOrWhiteSpace(canonicalAircraftId))
        {
            throw new InvalidOperationException(
                "A contract-linked FlightSession requires the reserved canonical aircraft identity.");
        }

        return canonicalAircraftId.Trim();
    }

    private static void ValidateRetainedFlightSession(
        FlightSession current,
        JobContract authoritativeContract,
        string expectedCanonicalAircraftId,
        DateTimeOffset expectedStartTime)
    {
        if (current.IsTerminal)
        {
            throw new InvalidOperationException(
                "The previous terminal FlightSession must complete its owning terminal workflow before another job flight can start.");
        }

        if (current.ContractId
            != authoritativeContract.ContractId)
        {
            throw new InvalidOperationException(
                "A different active FlightSession already owns the simulator operation.");
        }

        if (current.Plan is { } retainedPlan
            && (retainedPlan.SourceProvider is not null
                || retainedPlan.SourceReference is not null)
            && (!string.Equals(
                    retainedPlan.SourceProvider,
                    JobContractSourceProvider,
                    StringComparison.Ordinal)
                || !Guid.TryParse(
                    retainedPlan.SourceReference,
                    out Guid sourceContractId)
                || sourceContractId
                    != authoritativeContract.ContractId))
        {
            throw new InvalidOperationException(
                "The active FlightSession source does not match the authoritative job contract.");
        }

        if (current.Plan?.ExpectedCanonicalAircraftId is { } retainedIdentity
            && !string.Equals(
                retainedIdentity,
                expectedCanonicalAircraftId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active FlightSession aircraft identity does not match the retained reservation.");
        }

        if (current.Plan?.PlannedOrigin is { } retainedOrigin
            && !string.Equals(
                retainedOrigin,
                authoritativeContract.OriginIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active FlightSession origin does not match the authoritative job contract.");
        }

        if (current.Plan?.PlannedDestination is { } retainedDestination
            && !string.Equals(
                retainedDestination,
                authoritativeContract.DestinationIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The active FlightSession destination does not match the authoritative job contract.");
        }

        DateTimeOffset? authoritativeStartTime =
            authoritativeContract.Status switch
            {
                ContractStatus.Accepted =>
                    expectedStartTime,

                ContractStatus.InProgress =>
                    authoritativeContract.StartedAt
                    ?? throw new InvalidOperationException(
                        "An InProgress contract must retain its authoritative start time."),

                _ =>
                    null
            };

        if (authoritativeStartTime is { } startedAt
            && current.CreatedAt != startedAt)
        {
            throw new InvalidOperationException(
                "The active FlightSession start time does not match the authoritative job contract.");
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
