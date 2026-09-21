namespace OpenCareer.Application.Ai;

public interface IAiNarrativeProvider
{
    string ProviderName { get; }

    bool IsConfigured { get; }

    Task<AiProviderResponse> GenerateAsync(
        AiNarrativeRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAiNarrativeService
{
    Task<AiNarrativeResult> GenerateAsync(
        AiNarrativeRequest request,
        CancellationToken cancellationToken = default);
}
