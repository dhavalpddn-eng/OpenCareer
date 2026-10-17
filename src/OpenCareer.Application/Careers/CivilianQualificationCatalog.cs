using System.Collections.Immutable;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record CivilianQualificationDefinition(
    int Sequence,
    CareerQualificationIdentity Identity,
    string Title,
    string Description,
    PilotLicenseLevel? License,
    PilotRating? Rating,
    ImmutableArray<CareerQualificationIdentity> Prerequisites)
{
    public bool IsEarned(PilotQualificationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return License is { } license
            ? state.License >= license
            : Rating is { } rating
                && state.Ratings.Contains(rating);
    }

    public PilotQualificationState ApplyTo(PilotQualificationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return License is { } license
            ? state with { License = license }
            : state with
            {
                Ratings = state.Ratings.Add(
                    Rating ?? throw new InvalidOperationException(
                        "Qualification definition has no award target."))
            };
    }
}

public interface ICivilianQualificationCatalog
{
    IReadOnlyList<CivilianQualificationDefinition> Definitions { get; }

    bool TryGet(
        CareerQualificationIdentity identity,
        out CivilianQualificationDefinition? definition);

    bool ArePrerequisitesMet(
        CivilianQualificationDefinition definition,
        PilotQualificationState state);
}

public sealed class CivilianQualificationCatalog
    : ICivilianQualificationCatalog
{
    public static CivilianQualificationCatalog Default { get; } = new();

    private readonly ImmutableArray<CivilianQualificationDefinition> _definitions;
    private readonly ImmutableDictionary<CareerQualificationIdentity, CivilianQualificationDefinition> _byIdentity;

    public CivilianQualificationCatalog()
    {
        _definitions =
        [
            License(10, PilotLicenseLevel.Student, "Student pilot", "Entry certificate for the civilian training path."),
            License(20, PilotLicenseLevel.Private, "Private pilot", "Foundation certificate for independent civilian flying.", PilotLicenseLevel.Student),
            License(30, PilotLicenseLevel.Commercial, "Commercial pilot", "Certificate required for compensated civilian pilot work.", PilotLicenseLevel.Private),
            License(40, PilotLicenseLevel.AirlineTransport, "Airline transport pilot", "Highest supported civilian certificate in the initial ladder.", PilotLicenseLevel.Commercial),
            Rating(50, PilotRating.AirplaneSingleEngineLand, "Airplane single-engine land", "Single-engine land airplane class rating.", PilotLicenseLevel.Private),
            Rating(60, PilotRating.AirplaneMultiEngineLand, "Airplane multi-engine land", "Multi-engine land airplane class rating.", PilotLicenseLevel.Private),
            Rating(70, PilotRating.InstrumentAirplane, "Instrument airplane", "Airplane instrument rating.", PilotLicenseLevel.Private)
        ];

        _byIdentity = _definitions.ToImmutableDictionary(
            static definition => definition.Identity);
    }

    public IReadOnlyList<CivilianQualificationDefinition> Definitions =>
        _definitions;

    public bool TryGet(
        CareerQualificationIdentity identity,
        out CivilianQualificationDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        return _byIdentity.TryGetValue(identity, out definition);
    }

    public bool ArePrerequisitesMet(
        CivilianQualificationDefinition definition,
        PilotQualificationState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();

        return definition.Prerequisites.All(prerequisite =>
            _byIdentity.TryGetValue(prerequisite, out CivilianQualificationDefinition? required)
            && required.IsEarned(state));
    }

    private static CivilianQualificationDefinition License(
        int sequence,
        PilotLicenseLevel license,
        string title,
        string description,
        PilotLicenseLevel? prerequisite = null) =>
        new(
            sequence,
            LicenseIdentity(license),
            title,
            description,
            license,
            Rating: null,
            prerequisite is null ? [] : [LicenseIdentity(prerequisite.Value)]);

    private static CivilianQualificationDefinition Rating(
        int sequence,
        PilotRating rating,
        string title,
        string description,
        PilotLicenseLevel prerequisite) =>
        new(
            sequence,
            new CareerQualificationIdentity(
                $"rating.{rating.ToString().ToLowerInvariant()}",
                CareerQualificationKind.Rating),
            title,
            description,
            License: null,
            rating,
            [LicenseIdentity(prerequisite)]);

    private static CareerQualificationIdentity LicenseIdentity(
        PilotLicenseLevel license) =>
        new(
            $"license.{license.ToString().ToLowerInvariant()}",
            CareerQualificationKind.License);
}
