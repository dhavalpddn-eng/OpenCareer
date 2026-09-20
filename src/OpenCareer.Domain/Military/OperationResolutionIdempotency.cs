namespace OpenCareer.Domain.Military;

public readonly record struct OperationResolutionKey
{
    private OperationResolutionKey(string operationId, Guid missionId)
    {
        OperationId = operationId;
        MissionId = missionId;
    }

    public string OperationId { get; }

    public Guid MissionId { get; }

    public static OperationResolutionKey Create(
        string operationId,
        Guid missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        if (missionId == Guid.Empty)
            throw new ArgumentException("Mission ID is required.", nameof(missionId));

        return new OperationResolutionKey(operationId, missionId);
    }

    public override string ToString() =>
        $"{OperationId}:{MissionId:N}";
}

public interface IOperationResolutionRegistry
{
    bool TryRegister(OperationResolutionKey key);

    void Remove(OperationResolutionKey key);
}

public sealed class InMemoryOperationResolutionRegistry
    : IOperationResolutionRegistry
{
    private readonly object _gate = new();
    private readonly HashSet<OperationResolutionKey> _resolved = [];

    public bool TryRegister(OperationResolutionKey key)
    {
        lock (_gate)
            return _resolved.Add(key);
    }

    public void Remove(OperationResolutionKey key)
    {
        lock (_gate)
            _resolved.Remove(key);
    }
}

public sealed class DuplicateOperationResolutionException
    : InvalidOperationException
{
    public DuplicateOperationResolutionException(OperationResolutionKey key)
        : base($"Military operation resolution '{key}' has already been processed.")
    {
        ResolutionKey = key;
    }

    public OperationResolutionKey ResolutionKey { get; }
}

public sealed class IdempotentOperationResolver : IOperationResolver
{
    private readonly IOperationResolver _inner;
    private readonly IOperationResolutionRegistry _registry;

    public IdempotentOperationResolver(
        IOperationResolver inner,
        IOperationResolutionRegistry registry)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public OperationOutcome Resolve(OperationResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();

        OperationResolutionKey key =
            OperationResolutionKey.Create(input.OperationId, input.MissionId);

        if (!_registry.TryRegister(key))
            throw new DuplicateOperationResolutionException(key);

        try
        {
            OperationOutcome outcome = _inner.Resolve(input);

            OperationResolutionKey outcomeKey =
                OperationResolutionKey.Create(
                    outcome.OperationId,
                    outcome.MissionId);

            if (outcomeKey != key)
            {
                throw new InvalidOperationException(
                    "Operation resolver changed the resolution identity.");
            }

            return outcome;
        }
        catch
        {
            _registry.Remove(key);
            throw;
        }
    }
}
