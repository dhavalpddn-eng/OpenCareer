namespace OpenCareer.Infrastructure.Ai;

public sealed record OpenAiNarrativeOptions(
    string? ApiKey,
    string Model)
{
    public const string DefaultModel = "gpt-5.6-luna";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey);

    public static OpenAiNarrativeOptions FromEnvironment()
    {
        string? model = Environment.GetEnvironmentVariable("OPENAI_MODEL");

        return new OpenAiNarrativeOptions(
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            string.IsNullOrWhiteSpace(model)
                ? DefaultModel
                : model.Trim());
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("OpenAI model is required.", nameof(Model));
    }
}
