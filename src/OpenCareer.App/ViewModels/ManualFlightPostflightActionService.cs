using OpenCareer.Application.Careers;

namespace OpenCareer.App.ViewModels;

public interface IManualFlightPostflightAction
{
    Task<ManualFlightPostflightInputSnapshot> ReadAsync(
        CancellationToken cancellationToken = default);

    Task LogAsync(
        ManualFlightPostflightLogRequest request,
        CancellationToken cancellationToken = default);

    Task DiscardAsync(CancellationToken cancellationToken = default);
}

public sealed class ManualFlightPostflightActionService(
    ManualFlightPostflightInputSource inputSource,
    ManualFlightPostflightCoordinator coordinator)
    : IManualFlightPostflightAction
{
    private readonly ManualFlightPostflightInputSource _inputSource =
        inputSource ?? throw new ArgumentNullException(nameof(inputSource));

    private readonly ManualFlightPostflightCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public Task<ManualFlightPostflightInputSnapshot> ReadAsync(
        CancellationToken cancellationToken = default) =>
        _inputSource.ReadCurrentAsync(DateTimeOffset.UtcNow, cancellationToken);

    public async Task LogAsync(
        ManualFlightPostflightLogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _coordinator.LogAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task DiscardAsync(CancellationToken cancellationToken = default) =>
        _coordinator.DiscardAsync(cancellationToken);
}
