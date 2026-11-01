using OpenCareer.Application.Flights;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public enum CareerJobPlayableReadinessState
{
    NoCareerFlight = 0,
    ContractStateUnavailable = 1,
    FlightInProgress = 2,
    FlightSuspended = 3,
    AwaitingFlightEvidence = 4,
    AwaitingVerifiedCompletionInputs = 5,
    CompletedAwaitingTerminalWorkflow = 6,
    FailedOrCancelled = 7
}

public sealed record CareerJobPlayableReadinessSnapshot(
    CareerJobPlayableReadinessState State,
    Guid? ContractId,
    string StatusText,
    string Detail);

public sealed class CareerJobPlayableLoopReadinessSource
{
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly IJobContractRuntimeSource _contracts;
    private readonly JobFlightCompletionEvidenceTracker _evidence;

    public CareerJobPlayableLoopReadinessSource(
        FlightSessionCoordinator flightSessions,
        IJobContractRuntimeSource contracts,
        JobFlightCompletionEvidenceTracker evidence)
    {
        _flightSessions =
            flightSessions
            ?? throw new ArgumentNullException(nameof(flightSessions));
        _contracts =
            contracts
            ?? throw new ArgumentNullException(nameof(contracts));
        _evidence =
            evidence
            ?? throw new ArgumentNullException(nameof(evidence));
    }

    public CareerJobPlayableReadinessSnapshot Current =>
        BuildSnapshot();

    private CareerJobPlayableReadinessSnapshot BuildSnapshot()
    {
        FlightSession? session =
            _flightSessions.Current;

        if (session is null
            || session.ContractId is null)
        {
            return new(
                CareerJobPlayableReadinessState.NoCareerFlight,
                ContractId:
                    null,
                "NO ACTIVE CAREER JOB",
                session is null
                    ? "Accept and start a career job to create a contract-linked FlightSession."
                    : "The current FlightSession is not linked to a career job contract.");
        }

        Guid contractId =
            session.ContractId.Value;

        PersistedJobContract? persisted =
            _contracts.Find(
                contractId);

        if (persisted is null)
        {
            return new(
                CareerJobPlayableReadinessState.ContractStateUnavailable,
                contractId,
                "CAREER CONTRACT STATE UNAVAILABLE",
                "The current FlightSession is contract-linked, but its authoritative job-contract runtime state is not available.");
        }

        persisted.Validate();

        if (session.Status
                is FlightSessionStatus.Interrupted
                    or FlightSessionStatus.Cancelled
            || session.Tracking.CrashReported
            || persisted.Contract.Status
                is ContractStatus.Failed
                    or ContractStatus.Cancelled
                    or ContractStatus.Expired)
        {
            return new(
                CareerJobPlayableReadinessState.FailedOrCancelled,
                contractId,
                "CAREER FLIGHT CANNOT COMPLETE",
                "The authoritative flight or contract state is failed, interrupted, cancelled, expired, or crashed.");
        }

        if (persisted.Contract.Status
                == ContractStatus.Completed
            && session.Status
                == FlightSessionStatus.Completed)
        {
            return new(
                CareerJobPlayableReadinessState.CompletedAwaitingTerminalWorkflow,
                contractId,
                "FLIGHT COMPLETE · TERMINAL WORKFLOW READY",
                "The contract and FlightSession are complete. Settlement, logbook, career progression, Fleet release, and checkpoint cleanup must run through the playable-loop coordinator.");
        }

        if (session.Status
            == FlightSessionStatus.Suspended)
        {
            return new(
                CareerJobPlayableReadinessState.FlightSuspended,
                contractId,
                "CAREER FLIGHT SUSPENDED",
                "OpenCareer preserved this career flight. Trustworthy simulator continuity must resume before completion evidence can advance.");
        }

        JobFlightCompletionEvidenceSnapshot? evidence =
            _evidence.Current;

        bool matchingEvidence =
            evidence is not null
            && evidence.ContractId
                == contractId
            && evidence.FlightSessionId
                == session.SessionId;

        if (session.OperationState
            == FlightOperationState.Shutdown)
        {
            if (!matchingEvidence
                || !evidence!.CoreFlightSequenceObserved)
            {
                return new(
                    CareerJobPlayableReadinessState.AwaitingFlightEvidence,
                    contractId,
                    "POST-FLIGHT EVIDENCE INCOMPLETE",
                    "The aircraft is shut down, but the authoritative takeoff, airborne, landing, parking, and shutdown sequence is not complete.");
            }

            return new(
                CareerJobPlayableReadinessState.AwaitingVerifiedCompletionInputs,
                contractId,
                "FLIGHT EVIDENCE READY",
                "Core flight evidence is complete. Final career completion still requires authoritative mission verification, actual settlement costs, and debrief context; the UI will not fabricate those inputs.");
        }

        return new(
            CareerJobPlayableReadinessState.FlightInProgress,
            contractId,
            "CAREER FLIGHT IN PROGRESS",
            $"OpenCareer is tracking the contract-linked flight at {session.OperationState}.");
    }
}
