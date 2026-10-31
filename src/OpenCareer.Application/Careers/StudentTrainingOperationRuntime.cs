using Microsoft.Extensions.Logging;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

/// <summary>
/// Finalizes completed Student training from the authoritative FlightSession
/// lifecycle. UI actions can start training, but cannot complete it or award
/// its qualification.
/// </summary>
public sealed class StudentTrainingOperationRuntime : IDisposable
{
    private readonly FlightSessionCoordinator _sessions;
    private readonly StudentTrainingOperationService _operations;
    private readonly ILogger<StudentTrainingOperationRuntime> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _initialized;
    private bool _disposed;

    public StudentTrainingOperationRuntime(
        FlightSessionCoordinator sessions,
        StudentTrainingOperationService operations,
        ILogger<StudentTrainingOperationRuntime> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StudentTrainingOperationRuntime));

        if (Interlocked.Exchange(ref _initialized, 1) == 0)
            _sessions.SessionChanged += OnSessionChanged;

        return await TryFinalizeAsync(
                _sessions.Current,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryFinalizeCurrentAsync(
        CancellationToken cancellationToken = default) =>
        await TryFinalizeAsync(
                _sessions.Current,
                cancellationToken)
            .ConfigureAwait(false);

    private void OnSessionChanged(
        object? sender,
        FlightSessionChangedEventArgs args)
    {
        if (_disposed)
            return;

        _ = TryFinalizeSafelyAsync(
            args.Session,
            _lifetime.Token);
    }

    private async Task TryFinalizeSafelyAsync(
        FlightSession? session,
        CancellationToken cancellationToken)
    {
        try
        {
            await TryFinalizeAsync(session, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Authoritative Student training finalization failed and remains available for retry.");
        }
    }

    private async Task<bool> TryFinalizeAsync(
        FlightSession? observed,
        CancellationToken cancellationToken)
    {
        if (observed?.Status != FlightSessionStatus.Completed
            || !StudentTrainingOperationService.TryGetOperationAuthority(
                observed,
                out _,
                out Guid observedOperationId))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FlightSession? current = _sessions.Current;
            if (current?.Status != FlightSessionStatus.Completed
                || current.SessionId != observedOperationId
                || !StudentTrainingOperationService.TryGetOperationAuthority(
                    current,
                    out _,
                    out Guid currentOperationId))
            {
                return false;
            }

            await _operations
                .CompleteAsync(currentOperationId, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();
        _sessions.SessionChanged -= OnSessionChanged;
        _lifetime.Dispose();
    }
}
