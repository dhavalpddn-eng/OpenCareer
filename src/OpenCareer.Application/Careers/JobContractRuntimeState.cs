namespace OpenCareer.Application.Careers;

public sealed class JobContractRuntimeState
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
