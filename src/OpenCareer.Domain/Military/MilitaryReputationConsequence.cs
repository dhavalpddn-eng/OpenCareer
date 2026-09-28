namespace OpenCareer.Domain.Military;

public interface IMilitaryReputationConsequenceRegistry
{
    bool TryRegister(OperationResolutionKey key);

    void Remove(OperationResolutionKey key);
}

public sealed class InMemoryMilitaryReputationConsequenceRegistry
    : IMilitaryReputationConsequenceRegistry
{
    private readonly object _gate = new();
    private readonly HashSet<OperationResolutionKey> _applied = [];

    public bool TryRegister(OperationResolutionKey key)
    {
        lock (_gate)
            return _applied.Add(key);
    }

    public void Remove(OperationResolutionKey key)
    {
        lock (_gate)
            _applied.Remove(key);
    }
}

public sealed class DuplicateMilitaryReputationConsequenceException
    : InvalidOperationException
{
    public DuplicateMilitaryReputationConsequenceException(
        OperationResolutionKey key)
        : base($"Military reputation consequence '{key}' has already been applied.")
    {
        ResolutionKey = key;
    }

    public OperationResolutionKey ResolutionKey { get; }
}

public sealed class MilitaryReputationConsequence
{
    private readonly IMilitaryReputationConsequenceRegistry _registry;

    public MilitaryReputationConsequence(
        IMilitaryReputationConsequenceRegistry registry)
    {
        _registry = registry
            ?? throw new ArgumentNullException(nameof(registry));
    }

    public void Release(OperationResolutionKey key) =>
        _registry.Remove(key);

    public MilitaryCareerState Apply(
        MilitaryCareerState current,
        OperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(outcome);

        current.Validate();
        outcome.Validate();

        if (current.Affiliation == MilitaryAffiliation.None)
        {
            throw new InvalidOperationException(
                "Military affiliation is required before reputation can change.");
        }

        OperationResolutionKey key =
            OperationResolutionKey.Create(
                outcome.OperationId,
                outcome.MissionId);

        if (!_registry.TryRegister(key))
            throw new DuplicateMilitaryReputationConsequenceException(key);

        try
        {
            (double trustDelta, int successDelta, int failureDelta) =
                outcome.Status switch
                {
                    OperationOutcomeStatus.Success =>
                        (0.020, 1, 0),

                    OperationOutcomeStatus.PartialSuccess =>
                        (0.010, 1, 0),

                    OperationOutcomeStatus.Failure =>
                        (-0.030, 0, 1),

                    OperationOutcomeStatus.Aborted =>
                        (-0.005, 0, 0),

                    _ => throw new ArgumentOutOfRangeException(
                        nameof(outcome.Status))
                };

            var updated = current with
            {
                Trust = Math.Clamp(
                    current.Trust + trustDelta,
                    0,
                    1),
                SuccessfulOperations = checked(
                    current.SuccessfulOperations + successDelta),
                FailedOperations = checked(
                    current.FailedOperations + failureDelta)
            };

            updated.Validate();
            return updated;
        }
        catch
        {
            _registry.Remove(key);
            throw;
        }
    }
}
