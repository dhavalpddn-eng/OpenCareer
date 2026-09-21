using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed record FlightSessionCompletionRequest(
    DateTimeOffset Timestamp,
    bool MissionConditionsVerified,
    bool PostFlightTasksVerified);

public sealed class FlightSessionCompletionService
{
    private readonly FlightSessionCoordinator _coordinator;
    private readonly FlightSessionPersistenceService _persistence;

    public FlightSessionCompletionService(
        FlightSessionCoordinator coordinator,
        FlightSessionPersistenceService persistence)
    {
        _coordinator =
            coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));

        _persistence =
            persistence
            ?? throw new ArgumentNullException(nameof(persistence));
    }

    public async Task<FlightSession> CompleteAsync(
        FlightSessionCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FlightSession current =
            _coordinator.Current
            ?? throw new InvalidOperationException(
                "No FlightSession exists.");

        if (current.IsTerminal)
        {
            if (current.Status == FlightSessionStatus.Completed)
                return current;

            throw new InvalidOperationException(
                "Interrupted or cancelled flights cannot be completed.");
        }

        if (!request.MissionConditionsVerified)
        {
            throw new InvalidOperationException(
                "Mission conditions must be verified before a career flight can complete.");
        }

        if (!request.PostFlightTasksVerified)
        {
            throw new InvalidOperationException(
                "Required post-flight tasks must be verified before completion.");
        }

        if (current.Tracking.State
            != FlightTrackingState.Parked)
        {
            throw new InvalidOperationException(
                "FlightSession must be parked before completion.");
        }

        if (current.OperationState
            != FlightOperationState.Shutdown)
        {
            throw new InvalidOperationException(
                "FlightSession must be shut down before completion.");
        }

        if (request.Timestamp < current.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Completion cannot move backward in time.");
        }

        return await _persistence
            .AdvanceAsync(
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        request.Timestamp,
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: true,
                        OperationCompleteConfirmed: true),
                    ShutdownConfirmed: true),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

public static class FlightSessionContractBridge
{
    public static FlightSessionPlan CreatePlan(
        JobContract contract,
        string? routeText = null,
        string? sourceProvider = "OpenCareer",
        string? sourceReference = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();

        var plan =
            new FlightSessionPlan(
                contract.OriginIcao,
                contract.DestinationIcao,
                PlannedRoute: routeText,
                SourceProvider: sourceProvider,
                SourceReference:
                    sourceReference
                    ?? contract.ContractId.ToString("N"));

        plan.Validate();
        return plan;
    }

    public static JobContract CompleteContract(
        JobContract contract,
        FlightSession session)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(session);

        contract.Validate();

        if (session.ContractId != contract.ContractId)
        {
            throw new InvalidOperationException(
                "FlightSession does not belong to this contract.");
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

        DateTimeOffset completedAt =
            session.Milestones.CompletedAt
            ?? session.UpdatedAt;

        return contract.Complete(
            completedAt,
            flightCompletionVerified: true);
    }
}
