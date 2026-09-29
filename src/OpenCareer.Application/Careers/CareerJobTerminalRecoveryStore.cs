namespace OpenCareer.Application.Careers;

public interface ICareerJobTerminalRecoveryStore
{
    Task<CareerJobPlayableCompletionRequest?> ReadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CareerJobPlayableCompletionRequest request,
        CancellationToken cancellationToken = default);

    Task ClearAsync(
        Guid contractId,
        CancellationToken cancellationToken = default);
}
