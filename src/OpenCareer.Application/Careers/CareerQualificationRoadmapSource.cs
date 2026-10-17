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
    CareerQualificationKind Kind)
{
    public CareerQualificationIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);

        if (!Enum.IsDefined(typeof(CareerQualificationKind), Kind))
            throw new InvalidOperationException("Qualification kind is not recognized.");

        return this;
    }
}

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
        "Available means prerequisites are met, not that training is complete. Only an authoritative qualification-specific training operation may award; Student local-circuit training is supported and later qualifications remain pending.";

    private readonly PlayerCareerRuntimeState _runtime;
    private readonly ICivilianQualificationCatalog _catalog;

    public CareerQualificationRoadmapSource(
        PlayerCareerRuntimeState runtime)
        : this(runtime, CivilianQualificationCatalog.Default)
    {
    }

    public CareerQualificationRoadmapSource(
        PlayerCareerRuntimeState runtime,
        ICivilianQualificationCatalog catalog)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
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

        var items =
            ImmutableArray.CreateBuilder<CareerQualificationRoadmapItem>(
                _catalog.Definitions.Count);

        foreach (CivilianQualificationDefinition definition in _catalog.Definitions)
        {
            bool isEarned = definition.IsEarned(earned);
            ImmutableArray<CareerQualificationPrerequisite> prerequisites =
                definition.Prerequisites
                    .Select(prerequisite =>
                    {
                        if (!_catalog.TryGet(
                                prerequisite,
                                out CivilianQualificationDefinition? required)
                            || required is null)
                        {
                            throw new InvalidOperationException(
                                $"Qualification prerequisite '{prerequisite.Value}' is not in the shared catalog.");
                        }

                        return
                        new CareerQualificationPrerequisite(
                            prerequisite,
                            required.Title,
                            required.IsEarned(earned));
                    })
                    .ToImmutableArray();
            bool prerequisitesMet = _catalog.ArePrerequisitesMet(definition, earned);

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
}
