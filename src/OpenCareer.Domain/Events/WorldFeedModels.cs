namespace OpenCareer.Domain.Events;

public enum WorldSignalType
{
    PassengerTrend,
    CargoTrend,
    PublicEvent,
    WeatherDisruption,
    AirportDisruption,
    CarrierServiceChange,
    PublicServiceNeed,
    RegionalSecurity
}

public enum WorldSignalSourceKind
{
    DeterministicSimulation,
    OfficialDataset,
    PublicWebSource,
    AiSummarizedSource
}

public enum WorldSignalValidationStatus
{
    Pending,
    Validated,
    Rejected,
    Expired
}

/// <summary>
/// Normalized external/simulated signal. Only validated, unexpired signals may be mapped into bounded
/// deterministic game inputs; raw AI text never directly changes the career simulation.
/// </summary>
public sealed record WorldSignal(
    Guid SignalId,
    WorldSignalType Type,
    string ScopeId,
    double Magnitude,
    DateTimeOffset ObservedAt,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    WorldSignalSourceKind SourceKind,
    WorldSignalValidationStatus ValidationStatus,
    double Confidence,
    string? SourceReference = null,
    string? SourcePublisher = null)
{
    public bool IsUsableAt(DateTimeOffset time) =>
        ValidationStatus == WorldSignalValidationStatus.Validated
        && FetchedAt <= time
        && time < ExpiresAt;

    public WorldSignal MarkValidated(double confidence)
    {
        Validate();
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        if (ValidationStatus == WorldSignalValidationStatus.Rejected)
            throw new InvalidOperationException("A rejected signal cannot be silently validated.");
        return this with
        {
            ValidationStatus = WorldSignalValidationStatus.Validated,
            Confidence = confidence
        };
    }

    public WorldSignal MarkRejected() => this with
    {
        ValidationStatus = WorldSignalValidationStatus.Rejected,
        Confidence = 0
    };

    public void Validate()
    {
        if (SignalId == Guid.Empty || string.IsNullOrWhiteSpace(ScopeId))
            throw new ArgumentException("World signal identity and scope are required.");
        if (!Enum.IsDefined(Type) || !Enum.IsDefined(SourceKind) || !Enum.IsDefined(ValidationStatus))
            throw new ArgumentOutOfRangeException(nameof(Type));
        if (!double.IsFinite(Magnitude) || Magnitude is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Magnitude));
        if (!double.IsFinite(Confidence) || Confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Confidence));
        if (FetchedAt < ObservedAt || ExpiresAt <= FetchedAt)
            throw new ArgumentException("World signal timestamps are inconsistent.");

        if (SourceKind != WorldSignalSourceKind.DeterministicSimulation
            && (string.IsNullOrWhiteSpace(SourceReference) || string.IsNullOrWhiteSpace(SourcePublisher)))
        {
            throw new ArgumentException("External world signals require source provenance.");
        }
    }
}

public enum WorldFeedPostCategory
{
    Market,
    Travel,
    Cargo,
    Company,
    Airport,
    Weather,
    PublicService,
    Security,
    CareerNetwork
}

/// <summary>
/// Player-facing narrative. It may explain authoritative world state but has no authority itself.
/// </summary>
public sealed record WorldFeedPost(
    Guid PostId,
    DateTimeOffset CreatedAt,
    WorldFeedPostCategory Category,
    string Headline,
    string Body,
    string? ScopeId = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<Guid>? RelatedSignalIds = null,
    IReadOnlyList<string>? RelatedWorldEventIds = null,
    bool IsAiGenerated = false,
    string? SourceDisclosure = null)
{
    public void Validate()
    {
        if (PostId == Guid.Empty || string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Body))
            throw new ArgumentException("World-feed post identity and content are required.");
        if (!Enum.IsDefined(Category))
            throw new ArgumentOutOfRangeException(nameof(Category));
        if (ExpiresAt is { } expiry && expiry <= CreatedAt)
            throw new ArgumentException("World-feed post expiry must follow creation.");
        if (IsAiGenerated && string.IsNullOrWhiteSpace(SourceDisclosure))
            throw new ArgumentException("AI-generated feed posts require a disclosure/provenance note.");
        if (RelatedSignalIds?.Any(id => id == Guid.Empty) == true)
            throw new ArgumentException("Related world-signal identities must be valid.");
    }
}

public static class WorldSignalMarketMapper
{
    /// <summary>
    /// Converts a validated trend signal into a deliberately bounded demand multiplier. This mapping,
    /// not the AI/source text, controls the simulation impact.
    /// </summary>
    public static double DemandMultiplier(WorldSignal signal, DateTimeOffset time, double maximumEffect = 0.25)
    {
        ArgumentNullException.ThrowIfNull(signal);
        signal.Validate();
        if (!double.IsFinite(maximumEffect) || maximumEffect is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumEffect));
        if (!signal.IsUsableAt(time))
            return 1;
        if (signal.Type is not WorldSignalType.PassengerTrend and not WorldSignalType.CargoTrend)
            return 1;

        var effect = signal.Magnitude * signal.Confidence * maximumEffect;
        return 1 + effect;
    }
}
