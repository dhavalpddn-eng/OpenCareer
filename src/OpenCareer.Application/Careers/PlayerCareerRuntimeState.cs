namespace OpenCareer.Application.Careers;

public sealed class PlayerCareerRuntimeState
{
    private readonly IPlayerCareerProfileStore _store;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly object _sync = new();

    private PlayerCareerProfileStoreRecord? _current;
    private bool _initialized;

    public PlayerCareerRuntimeState(
        IPlayerCareerProfileStore store)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
    }

    public bool IsInitialized
    {
        get
        {
            lock (_sync)
                return _initialized;
        }
    }

    public bool OnboardingRequired
    {
        get
        {
            lock (_sync)
                return _initialized && _current is null;
        }
    }

    public PlayerCareerProfileStoreRecord? Current
    {
        get
        {
            lock (_sync)
                return _current;
        }
    }

    public async Task<PlayerCareerProfileStoreRecord?> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_initialized)
                return _current;
        }

        await _initializeGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            lock (_sync)
            {
                if (_initialized)
                    return _current;
            }

            PlayerCareerProfileStoreRecord? recovered =
                await _store
                    .LoadAsync(cancellationToken)
                    .ConfigureAwait(false);

            recovered?.Validate();

            lock (_sync)
            {
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

    public void Replace(
        PlayerCareerProfileStoreRecord current)
    {
        ArgumentNullException.ThrowIfNull(current);
        current.Validate();

        lock (_sync)
        {
            if (_current is { } existing)
            {
                if (existing.Profile.CareerId != current.Profile.CareerId)
                {
                    throw new PlayerCareerProfileConcurrencyException(
                        "A different career identity cannot replace the active profile.");
                }

                if (current.Revision < existing.Revision)
                {
                    throw new PlayerCareerProfileConcurrencyException(
                        "An older revision cannot replace the active career profile.");
                }
            }

            _current = current;
            _initialized = true;
        }
    }
}
