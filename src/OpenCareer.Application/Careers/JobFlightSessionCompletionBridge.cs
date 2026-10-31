using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public sealed record JobFlightSessionCompletionRequest(
    Guid ContractId,
    DateTimeOffset Timestamp,
    bool MissionConditionsVerified,
    bool PostFlightTasksVerified);

public sealed class JobFlightSessionCompletionBridge
{
    private readonly JobFlightCompletionEvidenceTracker _evidenceTracker;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly FlightSessionCompletionService _completionService;

    public JobFlightSessionCompletionBridge(
        JobFlightCompletionEvidenceTracker evidenceTracker,
        FlightSessionCoordinator flightSessions,
        FlightSessionCompletionService completionService)
    {
        _evidenceTracker =
            evidenceTracker
            ?? throw new ArgumentNullException(nameof(evidenceTracker));
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _completionService =
            completionService
            ?? throw new ArgumentNullException(nameof(completionService));
    }

    public async Task<FlightSession> CompleteAsync(
        JobFlightSessionCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ContractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(request));
        }

        FlightSession current =
            _flightSessions.Current
            ?? throw new InvalidOperationException(
                "No FlightSession exists for job completion.");

        JobFlightCompletionEvidenceSnapshot snapshot =
            _evidenceTracker.Current
            ?? throw new InvalidOperationException(
                "No correlated job-flight completion evidence is available.");

        if (current.ContractId
            != request.ContractId
            || snapshot.ContractId
                != request.ContractId
            || snapshot.FlightSessionId
                != current.SessionId)
        {
            throw new InvalidOperationException(
                "Job completion evidence does not belong to the requested contract FlightSession.");
        }

        if (current.Status
            == FlightSessionStatus.Completed)
        {
            return await _completionService
                .CompleteAsync(
                    new FlightSessionCompletionRequest(
                        request.Timestamp,
                        request.MissionConditionsVerified,
                        request.PostFlightTasksVerified),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (snapshot.IsFailedOrCancelled)
        {
            throw new InvalidOperationException(
                "Failed, interrupted, cancelled, or crashed job flights cannot complete.");
        }

        if (!snapshot.CoreFlightSequenceObserved)
        {
            throw new InvalidOperationException(
                "Job flight completion requires authoritative takeoff, airborne, landing, parking, and shutdown evidence.");
        }

        return await _completionService
            .CompleteAsync(
                new FlightSessionCompletionRequest(
                    request.Timestamp,
                    request.MissionConditionsVerified,
                    request.PostFlightTasksVerified),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
