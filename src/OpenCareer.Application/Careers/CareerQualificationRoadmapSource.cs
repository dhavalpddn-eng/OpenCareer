using System.Collections.Immutable;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum CareerQualificationKind
{
    License = 0,
    Rating = 1
}

public enum CareerQualificationRoadmapStatus
{
    Earned = 0,
    Available = 1,
    Locked = 2
}

public sealed record CareerQualificationIdentity(
    string Value,
    CareerQualificationKind Kind);

public sealed record CareerQualificationPrerequisite(
    CareerQualificationIdentity Identity,
    string Title,
    bool IsMet);

public sealed record CareerQualificationRoadmapItem(
    int Sequence,
    CareerQualificationIdentity Identity,
    string Title,
    string Description,
    CareerQualificationRoadmapStatus Status,
    ImmutableArray<CareerQualificationPrerequisite> Prerequisites);

public sealed record CareerQualificationRoadmap(
    PilotQualificationState EarnedQualifications,
    ImmutableArray<CareerQualificationRoadmapItem> Items,
    string TrainingPolicyDetail);

public interface ICareerQualificationRoadmapSource
{
    Task<CareerQualificationRoadmap?> ReadAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CareerQualificationRoadmapSource
    : ICareerQualificationRoadmapSource
{
    private const string TrainingPolicy =
        "Training delivery and cost are not yet authoritative. Available means prerequisites are met; only a future authoritative training-completion workflow may award a qualification.";

    private readonly PlayerCareerRuntimeState _runtime;

    public CareerQualificationRoadmapSource(
        PlayerCareerRuntimeState runtime)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<CareerQualificationRoadmap?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfileStoreRecord? current =
            await _runtime
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

        if (current is null)
            return null;

        PilotQualificationState earned =
            current.Profile.Qualifications;
        earned.Validate();

        ImmutableArray<Definition> definitions = Definitions;
        var items =
            ImmutableArray.CreateBuilder<CareerQualificationRoadmapItem>(
                definitions.Length);

        foreach (Definition definition in definitions)
        {
            bool isEarned = definition.IsEarned(earned);
            ImmutableArray<CareerQualificationPrerequisite> prerequisites =
                definition.Prerequisites
                    .Select(prerequisite =>
                        new CareerQualificationPrerequisite(
                            prerequisite.Identity,
                            prerequisite.Title,
                            prerequisite.IsMet(earned)))
                    .ToImmutableArray();
            bool prerequisitesMet =
                prerequisites.All(static prerequisite => prerequisite.IsMet);

            items.Add(
                new CareerQualificationRoadmapItem(
                    definition.Sequence,
                    definition.Identity,
                    definition.Title,
                    definition.Description,
                    isEarned
                        ? CareerQualificationRoadmapStatus.Earned
                        : prerequisitesMet
                            ? CareerQualificationRoadmapStatus.Available
                            : CareerQualificationRoadmapStatus.Locked,
                    prerequisites));
        }

        return new CareerQualificationRoadmap(
            earned,
            items.MoveToImmutable(),
            TrainingPolicy);
    }

    private static readonly ImmutableArray<Definition> Definitions =
    [
        License(10, PilotLicenseLevel.Student, "Student pilot", "Entry certificate for the civilian training path."),
        License(20, PilotLicenseLevel.Private, "Private pilot", "Foundation certificate for independent civilian flying.", PilotLicenseLevel.Student),
        License(30, PilotLicenseLevel.Commercial, "Commercial pilot", "Certificate required for compensated civilian pilot work.", PilotLicenseLevel.Private),
        License(40, PilotLicenseLevel.AirlineTransport, "Airline transport pilot", "Highest supported civilian certificate in the initial ladder.", PilotLicenseLevel.Commercial),
        Rating(50, PilotRating.AirplaneSingleEngineLand, "Airplane single-engine land", "Single-engine land airplane class rating.", PilotLicenseLevel.Private),
        Rating(60, PilotRating.AirplaneMultiEngineLand, "Airplane multi-engine land", "Multi-engine land airplane class rating.", PilotLicenseLevel.Private),
        Rating(70, PilotRating.InstrumentAirplane, "Instrument airplane", "Airplane instrument rating.", PilotLicenseLevel.Private)
    ];

    private static Definition License(
        int sequence,
        PilotLicenseLevel license,
        string title,
        string description,
        PilotLicenseLevel? prerequisite = null) =>
        new(
            sequence,
            new CareerQualificationIdentity(
                $"license.{license.ToString().ToLowerInvariant()}",
                CareerQualificationKind.License),
            title,
            description,
            state => state.License >= license,
            prerequisite is null
                ? []
                :
                [
                    new PrerequisiteDefinition(
                        new CareerQualificationIdentity(
                            $"license.{prerequisite.Value.ToString().ToLowerInvariant()}",
                            CareerQualificationKind.License),
                        LicenseTitle(prerequisite.Value),
                        state => state.License >= prerequisite.Value)
                ]);

    private static Definition Rating(
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
            state => state.Ratings.Contains(rating),
            [
                new PrerequisiteDefinition(
                    new CareerQualificationIdentity(
                        $"license.{prerequisite.ToString().ToLowerInvariant()}",
                        CareerQualificationKind.License),
                    LicenseTitle(prerequisite),
                    state => state.License >= prerequisite)
            ]);

    private static string LicenseTitle(
        PilotLicenseLevel license) =>
        license switch
        {
            PilotLicenseLevel.Student => "Student pilot",
            PilotLicenseLevel.Private => "Private pilot",
            PilotLicenseLevel.Commercial => "Commercial pilot",
            PilotLicenseLevel.AirlineTransport => "Airline transport pilot",
            _ => throw new ArgumentOutOfRangeException(nameof(license))
        };

    private sealed record Definition(
        int Sequence,
        CareerQualificationIdentity Identity,
        string Title,
        string Description,
        Func<PilotQualificationState, bool> IsEarned,
        ImmutableArray<PrerequisiteDefinition> Prerequisites);

    private sealed record PrerequisiteDefinition(
        CareerQualificationIdentity Identity,
        string Title,
        Func<PilotQualificationState, bool> IsMet);
}
