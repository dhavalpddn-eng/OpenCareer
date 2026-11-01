using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed record CivilianTrainingEvidenceIdentity(string Value)
{
    public CivilianTrainingEvidenceIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        return this;
    }
}

public enum CivilianTrainingCompletionStatus
{
    Incomplete = 0,
    Completed = 1
}

public sealed record CivilianTrainingCompletionEvidence(
    CivilianTrainingEvidenceIdentity EvidenceIdentity,
    string OperationIdentity,
    Guid CareerId,
    CareerQualificationIdentity TargetQualification,
    CivilianTrainingCompletionStatus Status,
    DateTimeOffset? CompletedAt,
    string SourceDetail)
{
    public CivilianTrainingCompletionEvidence Validate()
    {
        ArgumentNullException.ThrowIfNull(EvidenceIdentity);
        EvidenceIdentity.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(OperationIdentity);

        if (CareerId == Guid.Empty)
            throw new InvalidOperationException("Training evidence requires a career identity.");

        ArgumentNullException.ThrowIfNull(TargetQualification);
        TargetQualification.Validate();

        if (!Enum.IsDefined(typeof(CivilianTrainingCompletionStatus), Status))
            throw new InvalidOperationException("Training completion status is not recognized.");

        if (Status == CivilianTrainingCompletionStatus.Completed
            && CompletedAt is null)
        {
            throw new InvalidOperationException(
                "Completed training evidence requires a completion timestamp.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(SourceDetail);
        return this;
    }
}

public interface ICivilianTrainingCompletionEvidenceSource
{
    Task<CivilianTrainingCompletionEvidence?> ReadAsync(
        CivilianTrainingEvidenceIdentity evidenceIdentity,
        CancellationToken cancellationToken = default);
}

public sealed class UnavailableCivilianTrainingCompletionEvidenceSource
    : ICivilianTrainingCompletionEvidenceSource
{
    public Task<CivilianTrainingCompletionEvidence?> ReadAsync(
        CivilianTrainingEvidenceIdentity evidenceIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidenceIdentity);
        evidenceIdentity.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CivilianTrainingCompletionEvidence?>(null);
    }
}

public enum CivilianQualificationAwardStatus
{
    Awarded = 0,
    AlreadyEarned = 1,
    Blocked = 2,
    EvidenceUnavailable = 3
}

public sealed record CivilianQualificationAwardResult(
    CivilianQualificationAwardStatus Status,
    CareerQualificationIdentity? Qualification,
    string Detail);

public sealed class CivilianQualificationAwardService
{
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly ICivilianTrainingCompletionEvidenceSource _evidenceSource;
    private readonly ICivilianQualificationCatalog _catalog;
    private readonly PlayerCareerQualificationCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;

    public CivilianQualificationAwardService(
        PlayerCareerRuntimeState runtime,
        ICivilianTrainingCompletionEvidenceSource evidenceSource,
        ICivilianQualificationCatalog catalog,
        PlayerCareerQualificationCoordinator coordinator,
        TimeProvider timeProvider)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _evidenceSource = evidenceSource
            ?? throw new ArgumentNullException(nameof(evidenceSource));
        _catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CivilianQualificationAwardResult> AwardAsync(
        CivilianTrainingEvidenceIdentity evidenceIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidenceIdentity);
        evidenceIdentity.Validate();

        PlayerCareerProfileStoreRecord? current =
            await _runtime
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

        if (current is null)
        {
            return Blocked(
                qualification: null,
                "Player career onboarding must be completed before a qualification can be awarded.");
        }

        CivilianTrainingCompletionEvidence? evidence =
            await _evidenceSource
                .ReadAsync(evidenceIdentity, cancellationToken)
                .ConfigureAwait(false);

        if (evidence is null)
        {
            return new CivilianQualificationAwardResult(
                CivilianQualificationAwardStatus.EvidenceUnavailable,
                Qualification: null,
                "No authoritative civilian training completion evidence is available for this identity.");
        }

        try
        {
            evidence.Validate();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            return Blocked(
                evidence.TargetQualification,
                $"Training completion evidence is invalid: {exception.Message}");
        }

        if (evidence.EvidenceIdentity != evidenceIdentity)
        {
            return Blocked(
                evidence.TargetQualification,
                "Training evidence identity does not match the requested identity.");
        }

        if (evidence.CareerId != current.Profile.CareerId)
        {
            return Blocked(
                evidence.TargetQualification,
                "Training evidence belongs to a different career.");
        }

        if (!_catalog.TryGet(
                evidence.TargetQualification,
                out CivilianQualificationDefinition? definition)
            || definition is null)
        {
            return Blocked(
                evidence.TargetQualification,
                "Training evidence targets an unknown civilian qualification.");
        }

        if (evidence.Status != CivilianTrainingCompletionStatus.Completed)
        {
            return Blocked(
                definition.Identity,
                "Authoritative training evidence does not report completion.");
        }

        PilotQualificationState existing =
            current.Profile.Qualifications;

        if (!_catalog.ArePrerequisitesMet(definition, existing))
        {
            return Blocked(
                definition.Identity,
                "Qualification prerequisites are not currently met.");
        }

        if (definition.IsEarned(existing))
        {
            return new CivilianQualificationAwardResult(
                CivilianQualificationAwardStatus.AlreadyEarned,
                definition.Identity,
                "Qualification is already present in the authoritative career profile.");
        }

        PilotQualificationState awarded =
            definition.ApplyTo(existing);
        awarded.Validate();

        await _coordinator
            .ApplyEarnedAsync(
                awarded,
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        return new CivilianQualificationAwardResult(
            CivilianQualificationAwardStatus.Awarded,
            definition.Identity,
            "Authoritative training completion evidence was applied.");
    }

    private static CivilianQualificationAwardResult Blocked(
        CareerQualificationIdentity? qualification,
        string detail) =>
        new(
            CivilianQualificationAwardStatus.Blocked,
            qualification,
            detail);
}
