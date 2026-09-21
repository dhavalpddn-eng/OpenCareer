using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed class PersistedOperationConsequenceCoordinator
{
    private readonly OperationConsequenceOrchestrator _orchestrator;
    private readonly IOperationConsequenceStore _store;

    public PersistedOperationConsequenceCoordinator(
        OperationConsequenceOrchestrator orchestrator,
        IOperationConsequenceStore store)
    {
        _orchestrator = orchestrator
            ?? throw new ArgumentNullException(nameof(orchestrator));
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<OperationConsequenceStoreRecord?> LoadAsync(
        OperationResolutionKey key,
        CancellationToken cancellationToken = default) =>
        _store.LoadAsync(key, cancellationToken);

    public async Task<OperationConsequenceStoreRecord> ApplyAsync(
        OperationResolutionInput input,
        OperationConsequenceState current,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);

        input.Validate();
        current.Validate();

        if (savedAt == default)
            throw new ArgumentOutOfRangeException(nameof(savedAt));

        if (savedAt < input.ResolvedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedAt),
                "Persistence time cannot precede operation resolution.");
        }

        OperationResolutionKey key =
            OperationResolutionKey.Create(
                input.OperationId,
                input.MissionId);

        OperationConsequenceStoreRecord? existing =
            await _store.LoadAsync(
                    key,
                    cancellationToken)
                .ConfigureAwait(false);

        if (existing is not null)
            throw new DuplicateOperationResolutionException(key);

        OperationConsequenceResult result =
            _orchestrator.Apply(
                input,
                current);

        try
        {
            return await _store
                .SaveAsync(
                    result,
                    savedAt,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationConsequenceAlreadyExistsException)
        {
            _orchestrator.ReleaseReservations(key);
            throw new DuplicateOperationResolutionException(key);
        }
        catch
        {
            _orchestrator.ReleaseReservations(key);
            throw;
        }
    }
}
