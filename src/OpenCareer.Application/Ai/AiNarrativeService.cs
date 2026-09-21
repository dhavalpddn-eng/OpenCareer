using OpenCareer.Application.Settings;

namespace OpenCareer.Application.Ai;

public sealed class AiNarrativeService : IAiNarrativeService
{
    private readonly IAppSettingsService _settings;
    private readonly IAiNarrativeProvider _provider;

    public AiNarrativeService(
        IAppSettingsService settings,
        IAiNarrativeProvider provider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<AiNarrativeResult> GenerateAsync(
        AiNarrativeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (!_settings.Current.AllowOptionalOnlineServices)
            return AiNarrativeResult.Disabled(_provider.ProviderName);

        if (!_provider.IsConfigured)
            return AiNarrativeResult.NotConfigured(_provider.ProviderName);

        AiProviderResponse response = await _provider
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.Succeeded)
        {
            return new AiNarrativeResult(
                AiNarrativeStatus.Failed,
                null,
                response.Provider,
                response.Model,
                response.ErrorCode);
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            return new AiNarrativeResult(
                AiNarrativeStatus.Failed,
                null,
                response.Provider,
                response.Model,
                "empty_response");
        }

        return new AiNarrativeResult(
            AiNarrativeStatus.Succeeded,
            response.Text.Trim(),
            response.Provider,
            response.Model);
    }
}
