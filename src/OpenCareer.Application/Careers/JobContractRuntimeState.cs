using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class JobContractRuntimeState
    : IJobContractRuntimeSource
{
    private readonly JobContractRecoveryService _recoveryService;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    private IReadOnlyList<PersistedJobContract> _current =
        Array.Empty<PersistedJobContract>();

    private int _initialized;

    public JobContractRuntimeState(
        JobContractRecoveryService recoveryService)
    {
        _recoveryService =
            recoveryService
            ?? throw new ArgumentNullException(nameof(recoveryService));
    }

    public bool IsInitialized =>
        Volatile.Read(ref _initialized) == 1;

    public IReadOnlyList<PersistedJobContract> Current =>
        Volatile.Read(ref _current);

    public PersistedJobContract? Find(
        Guid contractId)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        IReadOnlyList<PersistedJobContract> snapshot =
            Current;

        for (int index = 0; index < snapshot.Count; index++)
        {
            PersistedJobContract candidate =
                snapshot[index];

            if (candidate.Contract.ContractId == contractId)
                return candidate;
        }

        return null;
    }

    public async Task PublishAuthoritativeAsync(
        PersistedJobContract persisted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        persisted.Validate();

        if (persisted.Contract.Status is not
            (ContractStatus.Accepted
            or ContractStatus.InProgress
            or ContractStatus.Completed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(persisted),
                "Only accepted, in-progress, or completed contracts belong in recoverable runtime state.");
        }

        if (!IsInitialized)
        {
            await InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            IReadOnlyList<PersistedJobContract> current =
                Current;

            int existingIndex = -1;

            for (int index = 0; index < current.Count; index++)
            {
                if (current[index].Contract.ContractId
                    == persisted.Contract.ContractId)
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                PersistedJobContract existing =
                    current[existingIndex];

                if (persisted.Version < existing.Version)
                {
                    throw new InvalidOperationException(
                        "Authoritative runtime publication cannot roll back a newer persisted contract version.");
                }

                if (persisted.Version == existing.Version)
                {
                    if (persisted.Contract != existing.Contract)
                    {
                        throw new InvalidOperationException(
                            "Equal job-contract versions cannot contain different authoritative state.");
                    }

                    return;
                }
            }

            PersistedJobContract[] next =
                current.ToArray();

            if (existingIndex >= 0)
            {
                next[existingIndex] =
                    persisted;
            }
            else
            {
                Array.Resize(
                    ref next,
                    next.Length + 1);

                next[^1] =
                    persisted;
            }

            Volatile.Write(
                ref _current,
                Array.AsReadOnly(next));
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<IReadOnlyList<PersistedJobContract>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
            return Current;

        await _initializationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (IsInitialized)
                return Current;

            IReadOnlyList<PersistedJobContract> recovered =
                await _recoveryService
                    .RecoverAsync(cancellationToken)
                    .ConfigureAwait(false);

            IReadOnlyList<PersistedJobContract> snapshot =
                Array.AsReadOnly(
                    recovered.ToArray());

            Volatile.Write(
                ref _current,
                snapshot);
            Volatile.Write(
                ref _initialized,
                1);

            return snapshot;
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}
