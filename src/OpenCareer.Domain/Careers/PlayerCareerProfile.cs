namespace OpenCareer.Domain.Careers;

public sealed record PlayerCareerProfile(
    Guid CareerId,
    DateTimeOffset CreatedAt,
    CareerLocation Location)
{
    public PilotQualificationState Qualifications { get; init; } =
        PilotQualificationState.Entry;

    public static PlayerCareerProfile Start(
        Guid careerId,
        string homeAirportIcao,
        DateTimeOffset createdAt)
    {
        if (careerId == Guid.Empty)
            throw new ArgumentException("Career id is required.", nameof(careerId));

        if (createdAt == default)
            throw new ArgumentOutOfRangeException(nameof(createdAt));

        var profile = new PlayerCareerProfile(
            careerId,
            createdAt,
            CareerLocation.Start(homeAirportIcao, createdAt));

        profile.Validate();
        return profile;
    }

    public void Validate()
    {
        if (CareerId == Guid.Empty)
            throw new ArgumentException("Career id is required.", nameof(CareerId));

        if (CreatedAt == default)
            throw new ArgumentOutOfRangeException(nameof(CreatedAt));

        ArgumentNullException.ThrowIfNull(Location);
        Location.Validate();

        if (Location.UpdatedAt < CreatedAt)
        {
            throw new InvalidOperationException(
                "Career location cannot predate career creation.");
        }

        ArgumentNullException.ThrowIfNull(Qualifications);
        Qualifications.Validate();
    }
}
