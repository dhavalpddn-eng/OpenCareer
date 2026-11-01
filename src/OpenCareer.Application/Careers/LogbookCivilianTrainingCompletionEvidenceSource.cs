using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public static class LogbookCivilianTrainingEvidenceIdentity
{
    private const string Prefix = "training-logbook:";

    public static CivilianTrainingEvidenceIdentity Create(Guid entryId)
    {
        if (entryId == Guid.Empty)
            throw new ArgumentException("Logbook entry id is required.", nameof(entryId));

        return new CivilianTrainingEvidenceIdentity($"{Prefix}{entryId:N}");
    }

    public static bool TryParse(
        CivilianTrainingEvidenceIdentity identity,
        out Guid entryId)
    {
        entryId = Guid.Empty;

        if (identity is null
            || string.IsNullOrWhiteSpace(identity.Value)
            || !identity.Value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string value = identity.Value[Prefix.Length..];
        return value.Length == 32
            && Guid.TryParseExact(value, "N", out entryId)
            && entryId != Guid.Empty
            && string.Equals(Create(entryId).Value, identity.Value, StringComparison.Ordinal);
    }
}

public sealed class LogbookCivilianTrainingCompletionEvidenceSource
    : ICivilianTrainingCompletionEvidenceSource
{
    private readonly ILogbookSource _logbook;
    private readonly ICivilianQualificationCatalog _catalog;

    public LogbookCivilianTrainingCompletionEvidenceSource(
        ILogbookSource logbook,
        ICivilianQualificationCatalog catalog)
    {
        _logbook = logbook ?? throw new ArgumentNullException(nameof(logbook));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<CivilianTrainingCompletionEvidence?> ReadAsync(
        CivilianTrainingEvidenceIdentity evidenceIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidenceIdentity);

        if (!LogbookCivilianTrainingEvidenceIdentity.TryParse(
                evidenceIdentity,
                out Guid entryId))
        {
            return null;
        }

        LogbookEntry? entry = await _logbook
            .GetAsync(entryId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null || entry.EntryId != entryId)
            return null;

        FlightDebrief debrief = entry.Debrief;
        CivilianQualificationTrainingMetadata? metadata =
            debrief.QualificationTraining;

        if (debrief.EntryKind != LogbookEntryKind.Training
            || metadata is null
            || debrief.MissionOutcome != MissionOutcome.Succeeded
            || debrief.SafetyOutcome is not (
                FlightSafetyOutcome.CompletedNormally
                or FlightSafetyOutcome.CompletedWithIncident)
            || debrief.Settlement.Status != SettlementRecordStatus.NotApplicable)
        {
            return null;
        }

        try
        {
            metadata.Validate();
            debrief.Settlement.Validate();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }

        CivilianQualificationDefinition? definition =
            _catalog.Definitions.SingleOrDefault(definition =>
                string.Equals(
                    definition.Identity.Value,
                    metadata.TargetQualificationIdentity,
                    StringComparison.Ordinal));

        if (definition is null)
            return null;

        var evidence = new CivilianTrainingCompletionEvidence(
            evidenceIdentity,
            metadata.TrainingOperationIdentity,
            metadata.CareerId,
            definition.Identity,
            CivilianTrainingCompletionStatus.Completed,
            debrief.EndedAt,
            metadata.SourceDetail);

        try
        {
            return evidence.Validate();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
