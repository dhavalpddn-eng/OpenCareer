namespace OpenCareer.Application.Ai;

public enum AiNarrativePurpose
{
    DispatcherBriefing = 0,
    PassengerDialogue = 1,
    CopilotDialogue = 2,
    PostFlightDebrief = 3,
    CareerStory = 4,
    RouteExplanation = 5,
    MissionFlavorText = 6
}

public enum AiNarrativeStatus
{
    Succeeded = 0,
    Disabled = 1,
    NotConfigured = 2,
    Failed = 3
}

public sealed record AiNarrativeRequest(
    AiNarrativePurpose Purpose,
    string Context,
    string? PlayerPrompt = null,
    int MaxOutputTokens = 350)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Context))
            throw new ArgumentException("AI narrative context is required.", nameof(Context));

        if (Context.Length > 50_000)
            throw new ArgumentOutOfRangeException(nameof(Context), "AI narrative context is too large.");

        if (PlayerPrompt is { Length: > 10_000 })
            throw new ArgumentOutOfRangeException(nameof(PlayerPrompt), "AI narrative player prompt is too large.");

        if (MaxOutputTokens is < 32 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens));
    }
}

public sealed record AiNarrativeResult(
    AiNarrativeStatus Status,
    string? Text,
    string Provider,
    string? Model,
    string? ErrorCode = null)
{
    public static AiNarrativeResult Disabled(string provider) =>
        new(AiNarrativeStatus.Disabled, null, provider, null);

    public static AiNarrativeResult NotConfigured(string provider) =>
        new(AiNarrativeStatus.NotConfigured, null, provider, null);
}

public sealed record AiProviderResponse(
    bool Succeeded,
    string? Text,
    string Provider,
    string? Model,
    string? ErrorCode = null);
