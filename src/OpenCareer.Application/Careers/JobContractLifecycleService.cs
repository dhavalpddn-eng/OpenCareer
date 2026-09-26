using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class JobContractLifecycleService
{
    private readonly IJobContractStore _store;
    private readonly JobContractRuntimeState? _runtimeState;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);

    public JobContractLifecycleService(
        IJobContractStore store)
    {
        _store =
            store
            ?? throw new ArgumentNullException(nameof(store));
    }

    public JobContractLifecycleService(
        IJobContractStore store,
        JobContractRuntimeState runtimeState)
        : this(store)
    {
        _runtimeState =
            runtimeState
            ?? throw new ArgumentNullException(nameof(runtimeState));
    }

    public Task<PersistedJobContract> AcceptAsync(
        Guid contractId,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return TransitionAsync(
            contractId,
            contract => contract.Accept(context),
            cancellationToken);
    }

    public async Task<PersistedJobContract> StartAsync(
        Guid contractId,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_runtimeState is not null
            && !_runtimeState.IsInitialized)
        {
            await _runtimeState
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        PersistedJobContract started =
            await TransitionAsync(
                contractId,
                contract => contract.Start(context),
                cancellationToken)
                .ConfigureAwait(false);

        if (_runtimeState is not null)
        {
            await _runtimeState
                .PublishAuthoritativeAsync(
                    started,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return started;
    }

    public async Task<PersistedJobContract> CompleteAsync(
        Guid contractId,
        DateTimeOffset time,
        bool flightCompletionVerified,
        CancellationToken cancellationToken = default)
    {
        if (_runtimeState is not null
            && !_runtimeState.IsInitialized)
        {
            await _runtimeState
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        PersistedJobContract completed =
            await TransitionAsync(
                contractId,
                contract =>
                    contract.Complete(
                        time,
                        flightCompletionVerified),
                cancellationToken)
                .ConfigureAwait(false);

        if (_runtimeState is not null)
        {
            await _runtimeState
                .PublishAuthoritativeAsync(
                    completed,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return completed;
    }

    public Task<PersistedJobContract> FailAsync(
        Guid contractId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            contractId,
            contract => contract.Fail(),
            cancellationToken);

    public Task<PersistedJobContract> CancelAsync(
        Guid contractId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            contractId,
            contract => contract.Cancel(),
            cancellationToken);

    public Task<PersistedJobContract> ExpireAsync(
        Guid contractId,
        DateTimeOffset time,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            contractId,
            contract => contract.Expire(time),
            cancellationToken);

    private async Task<PersistedJobContract> TransitionAsync(
        Guid contractId,
        Func<JobContract, JobContract> transition,
        CancellationToken cancellationToken)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        ArgumentNullException.ThrowIfNull(transition);

        await _transitionGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PersistedJobContract current =
                await _store
                    .ReadJobContractAsync(
                        contractId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Persisted job contract was not found.");

            current.Validate();

            if (current.Contract.ContractId != contractId)
            {
                throw new InvalidOperationException(
                    "Persisted job-contract identity does not match the requested transition.");
            }

            JobContract next =
                transition(current.Contract);

            next.Validate();

            JobContractSaveResult saveResult =
                await _store
                    .UpdateJobContractAsync(
                        next,
                        current.Version,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (saveResult == JobContractSaveResult.Updated)
            {
                var persisted =
                    new PersistedJobContract(
                        next,
                        checked(current.Version + 1));

                persisted.Validate();
                return persisted;
            }

            if (saveResult == JobContractSaveResult.AlreadySaved)
            {
                PersistedJobContract authoritative =
                    await _store
                        .ReadJobContractAsync(
                            contractId,
                            cancellationToken)
                        .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "Job contract disappeared after an idempotent lifecycle save.");

                authoritative.Validate();

                if (authoritative.Contract != next
                    || authoritative.Version < current.Version)
                {
                    throw new InvalidOperationException(
                        "Idempotent lifecycle save did not preserve the requested contract transition.");
                }

                return authoritative;
            }

            throw new InvalidOperationException(
                $"Unexpected lifecycle save result {saveResult}.");
        }
        finally
        {
            _transitionGate.Release();
        }
    }
}
