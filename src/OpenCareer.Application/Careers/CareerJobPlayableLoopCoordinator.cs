using OpenCareer.Application.Economy;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobPlayableStartRequest(
    JobContractCreationRequest Contract,
    ContractDispatchContext DispatchContext,
    OperationDispatchRequirements DispatchRequirements);

public sealed record CareerJobPlayableStartResult(
    AcceptedJobDispatchResult Dispatch,
    StartedJobFlightSessionResult StartedFlight);

public sealed record CareerJobPlayableCompletionRequest(
    Guid ContractId,
    DateTimeOffset FlightCompletionTime,
    bool MissionConditionsVerified,
    bool PostFlightTasksVerified,
    ContractSettlementCosts ActualCosts,
    DateTimeOffset SettledAt,
    SettledJobLogbookContext LogbookContext,
    DateTimeOffset LogbookCommittedAt,
    DateTimeOffset ExperienceSavedAt)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        ArgumentNullException.ThrowIfNull(ActualCosts);
        ArgumentNullException.ThrowIfNull(LogbookContext);
        ArgumentNullException.ThrowIfNull(LogbookContext.Aircraft);
        ArgumentNullException.ThrowIfNull(LogbookContext.Payload);

        if (FlightCompletionTime == default
            || SettledAt == default
            || LogbookCommittedAt == default
            || ExperienceSavedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FlightCompletionTime),
                "Career-flight terminal timestamps are required.");
        }

        if (SettledAt < FlightCompletionTime
            || LogbookCommittedAt < SettledAt
            || ExperienceSavedAt < LogbookCommittedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SettledAt),
                "Career-flight terminal timestamps must be monotonic.");
        }

        ActualCosts.Validate();
        LogbookContext.Aircraft.Validate();
        LogbookContext.Payload.Validate();

        if (!double.IsFinite(LogbookContext.ReputationDelta))
        {
            throw new ArgumentOutOfRangeException(
                nameof(LogbookContext.ReputationDelta));
        }

        if (LogbookContext.Events is not null)
        {
            foreach (FlightDebriefEvent item in LogbookContext.Events)
            {
                ArgumentNullException.ThrowIfNull(item);
                item.Validate();
            }
        }
    }
}

public sealed record CareerJobPlayableCompletionResult(
    FlightSession? CompletedFlight,
    PersistedJobContract CompletedContract,
    CareerFlightTerminalWorkflowResult Terminal);

public sealed class CareerJobPlayableLoopCoordinator
{
    private readonly AcceptedJobDispatchBridge _dispatch;
    private readonly AcceptedJobFlightSessionBridge _flightStart;
    private readonly JobFlightSessionCompletionBridge _flightCompletion;
    private readonly CompletedJobContractBridge _contractCompletion;
    private readonly IJobContractStore _contractStore;
    private readonly CareerFlightTerminalWorkflowCoordinator _terminal;
    private readonly ICareerJobTerminalRecoveryStore _terminalRecovery;
    private readonly SemaphoreSlim _completionGate = new(1, 1);

    public CareerJobPlayableLoopCoordinator(
        AcceptedJobDispatchBridge dispatch,
        AcceptedJobFlightSessionBridge flightStart,
        JobFlightSessionCompletionBridge flightCompletion,
        CompletedJobContractBridge contractCompletion,
        IJobContractStore contractStore,
        CareerFlightTerminalWorkflowCoordinator terminal,
        ICareerJobTerminalRecoveryStore terminalRecovery)
    {
        _dispatch =
            dispatch
            ?? throw new ArgumentNullException(nameof(dispatch));
        _flightStart =
            flightStart
            ?? throw new ArgumentNullException(nameof(flightStart));
        _flightCompletion =
            flightCompletion
            ?? throw new ArgumentNullException(nameof(flightCompletion));
        _contractCompletion =
            contractCompletion
            ?? throw new ArgumentNullException(nameof(contractCompletion));
        _contractStore =
            contractStore
            ?? throw new ArgumentNullException(nameof(contractStore));
        _terminal =
            terminal
            ?? throw new ArgumentNullException(nameof(terminal));
        _terminalRecovery =
            terminalRecovery
            ?? throw new ArgumentNullException(nameof(terminalRecovery));
    }

    public async Task<CareerJobPlayableStartResult> AcceptAndStartAsync(
        CareerJobPlayableStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Contract);
        ArgumentNullException.ThrowIfNull(request.DispatchContext);
        ArgumentNullException.ThrowIfNull(request.DispatchRequirements);

        AcceptedJobDispatchResult dispatch =
            await _dispatch
                .AcceptReserveAndEvaluateAsync(
                    request.Contract,
                    request.DispatchContext,
                    request.DispatchRequirements,
                    cancellationToken)
                .ConfigureAwait(false);

        StartedJobFlightSessionResult started =
            await _flightStart
                .StartAsync(
                    dispatch,
                    request.DispatchContext,
                    cancellationToken)
                .ConfigureAwait(false);

        return new(
            dispatch,
            started);
    }

    public async Task<CareerJobPlayableCompletionResult> CompleteAsync(
        CareerJobPlayableCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Validate();

        await _completionGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _terminalRecovery
                .SaveAsync(request, cancellationToken)
                .ConfigureAwait(false);

            PersistedJobContract authoritative =
                await _contractStore
                    .ReadJobContractAsync(
                        request.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Authoritative job contract was not found.");

            authoritative.Validate();

            FlightSession? completedFlight =
                null;

            PersistedJobContract completedContract =
                authoritative.Contract.Status switch
                {
                    ContractStatus.InProgress =>
                        await CompleteInProgressAsync(
                                request,
                                cancellationToken)
                            .ConfigureAwait(false),

                    ContractStatus.Completed =>
                        authoritative,

                    _ =>
                        throw new InvalidOperationException(
                            $"Career job cannot enter terminal workflow from contract state {authoritative.Contract.Status}.")
                };

            if (authoritative.Contract.Status
                == ContractStatus.InProgress)
            {
                completedFlight =
                    _lastCompletedFlight;
            }

            string settlementKey =
                ContractSettlementEngine.GetIdempotencyKey(
                    completedContract.Contract.ContractId);

            DateTimeOffset completedAt =
                completedContract.Contract.CompletedAt
                ?? throw new InvalidOperationException(
                    "Completed career contract has no authoritative completion timestamp.");
            DateTimeOffset settledAt =
                Latest(request.SettledAt, completedAt);
            DateTimeOffset logbookCommittedAt =
                Latest(request.LogbookCommittedAt, settledAt);
            DateTimeOffset experienceSavedAt =
                Latest(request.ExperienceSavedAt, logbookCommittedAt);

            var terminalRequest =
                new CareerFlightTerminalWorkflowRequest(
                    new SettlementPendingContractRequest(
                        new SettlementPendingContract(
                            completedContract,
                            settlementKey),
                        request.ActualCosts,
                        settledAt),
                    request.LogbookContext,
                    logbookCommittedAt,
                    experienceSavedAt);

            CareerFlightTerminalWorkflowResult terminal =
                await _terminal
                    .CompleteAsync(
                        terminalRequest,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (terminal.Settlement.PersistedContract
                    .Contract.ContractId
                != request.ContractId)
            {
                throw new InvalidOperationException(
                    "Terminal workflow returned a different career contract.");
            }

            var result = new CareerJobPlayableCompletionResult(
                completedFlight,
                completedContract,
                terminal);

            await _terminalRecovery
                .ClearAsync(request.ContractId, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        finally
        {
            _lastCompletedFlight =
                null;
            _completionGate.Release();
        }
    }

    public async Task<CareerJobPlayableCompletionResult?> ResumePendingAsync(
        CancellationToken cancellationToken = default)
    {
        CareerJobPlayableCompletionRequest? request =
            await _terminalRecovery
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);

        return request is null
            ? null
            : await CompleteAsync(request, cancellationToken)
                .ConfigureAwait(false);
    }

    private FlightSession? _lastCompletedFlight;

    private static DateTimeOffset Latest(
        DateTimeOffset first,
        DateTimeOffset second) =>
        first >= second
            ? first
            : second;

    private async Task<PersistedJobContract> CompleteInProgressAsync(
        CareerJobPlayableCompletionRequest request,
        CancellationToken cancellationToken)
    {
        FlightSession completedFlight =
            await _flightCompletion
                .CompleteAsync(
                    new JobFlightSessionCompletionRequest(
                        request.ContractId,
                        request.FlightCompletionTime,
                        request.MissionConditionsVerified,
                        request.PostFlightTasksVerified),
                    cancellationToken)
                .ConfigureAwait(false);

        if (completedFlight.ContractId
                != request.ContractId
            || completedFlight.Status
                != FlightSessionStatus.Completed)
        {
            throw new InvalidOperationException(
                "FlightSession completion did not produce the requested completed career flight.");
        }

        _lastCompletedFlight =
            completedFlight;

        PersistedJobContract completedContract =
            await _contractCompletion
                .CompleteAsync(
                    request.ContractId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (completedContract.Contract.Status
                != ContractStatus.Completed
            || completedContract.Contract.ContractId
                != request.ContractId)
        {
            throw new InvalidOperationException(
                "Job-contract completion did not produce the requested completed contract.");
        }

        return completedContract;
    }
}
