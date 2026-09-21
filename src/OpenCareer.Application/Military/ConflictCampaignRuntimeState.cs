namespace OpenCareer.Application.Military;

public sealed class ConflictCampaignRuntimeState
{
    private readonly IConflictCampaignRecoverySource _recoverySource;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly object _sync = new();

    private ConflictCampaignStoreRecord? _current;
    private bool _initialized;

    public ConflictCampaignRuntimeState(
        IConflictCampaignRecoverySource recoverySource)
    {
        _recoverySource = recoverySource
            ?? throw new ArgumentNullException(nameof(recoverySource));
    }

    public bool IsInitialized
    {
        get
        {
            lock (_sync)
                return _initialized;
        }
    }

    public ConflictCampaignStoreRecord? Current
    {
        get
        {
            lock (_sync)
                return _current;
        }
    }

    public async Task<ConflictCampaignStoreRecord?> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_initialized)
                return _current;
        }

        await _initializeGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            lock (_sync)
            {
                if (_initialized)
                    return _current;
            }

            ConflictCampaignStoreRecord? recovered =
                await _recoverySource
                    .LoadMostRecentlySavedAsync(cancellationToken)
                    .ConfigureAwait(false);

            recovered?.Validate();

            lock (_sync)
            {
                // A replacement published while recovery was loading takes precedence.
                if (!_initialized)
                {
                    _current = recovered;
                    _initialized = true;
                }

                return _current;
            }
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public void Replace(ConflictCampaignStoreRecord current)
    {
        ArgumentNullException.ThrowIfNull(current);
        current.Validate();

        lock (_sync)
        {
            _current = current;
            _initialized = true;
        }
    }
}
