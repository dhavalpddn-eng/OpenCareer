#pragma warning disable OPENAI001

using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using OpenCareer.Application.Ai;

namespace OpenCareer.Infrastructure.Ai;

public sealed class OpenAiNarrativeProvider : IAiNarrativeProvider
{
    private const string SystemInstructions =
        """
        You are the optional narrative layer for OpenCareer, a Microsoft Flight Simulator 2024 career companion.
        Generate immersive text only.
        Treat supplied OpenCareer context as authoritative facts.
        Do not decide, alter, or invent money, rewards, reputation, mission completion, aircraft ownership,
        qualifications, maintenance state, progression, or FlightSession state.
        Do not invent missing operational facts.
        If context is insufficient, write around the uncertainty instead of fabricating data.
        Return plain text suitable for an in-game companion UI.
        """;

    private readonly OpenAiNarrativeOptions _options;
    private readonly ILogger<OpenAiNarrativeProvider> _logger;
    private readonly ResponsesClient? _client;

    public OpenAiNarrativeProvider(
        OpenAiNarrativeOptions options,
        ILogger<OpenAiNarrativeProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _options.Validate();

        if (_options.IsConfigured)
        {
            _client = new ResponsesClient(apiKey: _options.ApiKey!);
        }
    }

    public string ProviderName => "OpenAI";

    public bool IsConfigured => _client is not null;

    public async Task<AiProviderResponse> GenerateAsync(
        AiNarrativeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (_client is null)
        {
            return new AiProviderResponse(
                false,
                null,
                ProviderName,
                _options.Model,
                "not_configured");
        }

        try
        {
            var options = new CreateResponseOptions
            {
                Model = _options.Model,
                Instructions = SystemInstructions,
                MaxOutputTokenCount = request.MaxOutputTokens,
                StoredOutputEnabled = false
            };

            options.InputItems.Add(
                ResponseItem.CreateUserMessageItem(
                    BuildUserInput(request)));

            ResponseResult response = await _client
                .CreateResponseAsync(options, cancellationToken)
                .ConfigureAwait(false);

            string? text = response.GetOutputText();

            return new AiProviderResponse(
                !string.IsNullOrWhiteSpace(text),
                text,
                ProviderName,
                _options.Model,
                string.IsNullOrWhiteSpace(text)
                    ? "empty_response"
                    : null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OpenAI narrative generation failed for purpose {Purpose}.",
                request.Purpose);

            return new AiProviderResponse(
                false,
                null,
                ProviderName,
                _options.Model,
                "provider_error");
        }
    }

    private static string BuildUserInput(AiNarrativeRequest request)
    {
        string playerPrompt =
            string.IsNullOrWhiteSpace(request.PlayerPrompt)
                ? "None."
                : request.PlayerPrompt.Trim();

        return $"""
            Purpose: {request.Purpose}

            OpenCareer context:
            {request.Context.Trim()}

            Player request:
            {playerPrompt}

            Write only the requested narrative text.
            """;
    }
}
