using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.Application.Ai;
using OpenCareer.Infrastructure.Ai;

namespace OpenCareer.Tests;

public sealed class OpenAiNarrativeProviderTests
{
    [Fact]
    public void MissingApiKeyLeavesProviderUnconfigured()
    {
        var provider = new OpenAiNarrativeProvider(
            new OpenAiNarrativeOptions(
                null,
                OpenAiNarrativeOptions.DefaultModel),
            NullLogger<OpenAiNarrativeProvider>.Instance);

        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task MissingApiKeyReturnsNotConfiguredWithoutNetworkCall()
    {
        var provider = new OpenAiNarrativeProvider(
            new OpenAiNarrativeOptions(
                null,
                OpenAiNarrativeOptions.DefaultModel),
            NullLogger<OpenAiNarrativeProvider>.Instance);

        AiProviderResponse result = await provider.GenerateAsync(
            new AiNarrativeRequest(
                AiNarrativePurpose.DispatcherBriefing,
                "Deterministic test context."));

        Assert.False(result.Succeeded);
        Assert.Equal("OpenAI", result.Provider);
        Assert.Equal(OpenAiNarrativeOptions.DefaultModel, result.Model);
        Assert.Equal("not_configured", result.ErrorCode);
    }
}
