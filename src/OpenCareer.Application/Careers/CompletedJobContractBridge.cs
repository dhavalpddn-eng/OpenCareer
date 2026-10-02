using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public sealed class CompletedJobContractBridge
{
    private readonly IJobContractStore _contractStore;
    private readonly JobContractLifecycleService _lifecycle;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CompletedJobContractBridge(
        IJobContractStore contractStore,
        JobContractLifecycleService lifecycle,
        FlightSessionCoordinator flightSessions)
    {
        _contractStore =
            contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));
        _lifecycle =
            lifecycle
            ?? throw new ArgumentNullException(nameof(lifecycle));
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
    }

    public async Task<PersistedJobContract> CompleteAsync(
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        await _gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            FlightSession session =
                _flightSessions.Current
                ?? throw new InvalidOperationException(
                    "No FlightSession exists for contract completion.");

            DateTimeOffset completedAt =
                ValidateCompletedSession(
                    contractId,
                    session);

            PersistedJobContract authoritative =
                await _contractStore
                    .ReadJobContractAsync(
                        contractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Persisted job contract was not found.");

            authoritative.Validate();

            if (authoritative.Contract.Status
                == ContractStatus.Completed)
            {
                if (authoritative.Contract.CompletedAt
                    != completedAt)
                {
                    throw new InvalidOperationException(
                        "Completed contract timestamp does not match the authoritative FlightSession completion.");
                }

                return authoritative;
            }

            if (authoritative.Contract.Status
                != ContractStatus.InProgress)
            {
                throw new InvalidOperationException(
                    $"Contract {contractId:D} cannot complete from state {authoritative.Contract.Status}.");
            }

            return await _lifecycle
                .CompleteAsync(
                    contractId,
                    completedAt,
                    flightCompletionVerified:
                        true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DateTimeOffset ValidateCompletedSession(
        Guid contractId,
        FlightSession session)
    {
        if (session.ContractId
            != contractId)
        {
            throw new InvalidOperationException(
                "FlightSession does not belong to the requested contract.");
        }

        if (session.Status
                != FlightSessionStatus.Completed
            || session.OperationState
                != FlightOperationState.Complete
            || session.Tracking.State
                != FlightTrackingState.Complete)
        {
            throw new InvalidOperationException(
                "Contract completion requires a fully completed FlightSession.");
        }

        if (session.Tracking.CrashReported)
        {
            throw new InvalidOperationException(
                "A crashed FlightSession cannot verify contract completion.");
        }

        return session.Milestones.CompletedAt
            ?? session.UpdatedAt;
    }
}
