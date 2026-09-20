using OpenCareer.Application.Ai;
using OpenCareer.Application.Settings;

namespace OpenCareer.Tests;

public sealed class AiNarrativeServiceTests
{
    [Fact]
    public async Task DisabledOnlineServicesNeverCallProvider()
    {
        var provider = new FakeProvider
        {
            IsConfigured = true
        };

        var service = new AiNarrativeService(
            new FakeSettingsService(
                AppPreferences.Default with
                {
                    AllowOptionalOnlineServices = false
                }),
            provider);

        AiNarrativeResult result = await service.GenerateAsync(
            new AiNarrativeRequest(
                AiNarrativePurpose.DispatcherBriefing,
                "Cargo flight from KDFW to KIAH."));

        Assert.Equal(AiNarrativeStatus.Disabled, result.Status);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task MissingKeyReturnsNotConfiguredWithoutProviderCall()
    {
        var provider = new FakeProvider
        {
            IsConfigured = false
        };

        var service = new AiNarrativeService(
            EnabledSettings(),
            provider);

        AiNarrativeResult result = await service.GenerateAsync(
            new AiNarrativeRequest(
                AiNarrativePurpose.CareerStory,
                "Pilot completed first charter contract."));

        Assert.Equal(AiNarrativeStatus.NotConfigured, result.Status);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task SuccessfulProviderTextIsReturnedTrimmed()
    {
        var provider = new FakeProvider
        {
            IsConfigured = true,
            Response = new AiProviderResponse(
                true,
                "  Dispatch ready.  ",
                "Fake",
                "fake-model")
        };

        var service = new AiNarrativeService(
            EnabledSettings(),
            provider);

        AiNarrativeResult result = await service.GenerateAsync(
            new AiNarrativeRequest(
                AiNarrativePurpose.DispatcherBriefing,
                "Valid deterministic dispatch context."));

        Assert.Equal(AiNarrativeStatus.Succeeded, result.Status);
        Assert.Equal("Dispatch ready.", result.Text);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ProviderFailureDegradesWithoutThrowing()
    {
        var provider = new FakeProvider
        {
            IsConfigured = true,
            Response = new AiProviderResponse(
                false,
                null,
                "Fake",
                "fake-model",
                "synthetic_failure")
        };

        var service = new AiNarrativeService(
            EnabledSettings(),
            provider);

        AiNarrativeResult result = await service.GenerateAsync(
            new AiNarrativeRequest(
                AiNarrativePurpose.PostFlightDebrief,
                "Flight safely completed."));

        Assert.Equal(AiNarrativeStatus.Failed, result.Status);
        Assert.Equal("synthetic_failure", result.ErrorCode);
    }

    [Fact]
    public async Task InvalidRequestIsRejectedBeforeProviderCall()
    {
        var provider = new FakeProvider
        {
            IsConfigured = true
        };

        var service = new AiNarrativeService(
            EnabledSettings(),
            provider);

        await Assert.ThrowsAsync<ArgumentException>(
            async () =>
                await service.GenerateAsync(
                    new AiNarrativeRequest(
                        AiNarrativePurpose.CopilotDialogue,
                        " ")));

        Assert.Equal(0, provider.CallCount);
    }

    private static FakeSettingsService EnabledSettings() =>
        new(
            AppPreferences.Default with
            {
                AllowOptionalOnlineServices = true
            });

    private sealed class FakeProvider : IAiNarrativeProvider
    {
        public string ProviderName => "Fake";

        public bool IsConfigured { get; set; }

        public int CallCount { get; private set; }

        public AiProviderResponse Response { get; set; } =
            new(
                true,
                "Generated text.",
                "Fake",
                "fake-model");

        public Task<AiProviderResponse> GenerateAsync(
            AiNarrativeRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Response);
        }
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        public FakeSettingsService(AppPreferences preferences)
        {
            Current = preferences;
        }

        public AppPreferences Current { get; private set; }

        public event EventHandler? Changed;

        public Task InitializeAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(
            AppPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            Current = preferences;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task ResetAsync(
            CancellationToken cancellationToken = default) =>
            UpdateAsync(
                AppPreferences.Default,
                cancellationToken);
    }
}
