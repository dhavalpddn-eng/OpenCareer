using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed class PlayerCareerExperienceCoordinator
{
    private readonly IPlayerCareerProfileStore _store;
    private readonly PlayerCareerRuntimeState _runtime;
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    public PlayerCareerExperienceCoordinator(
        IPlayerCareerProfileStore store,
        PlayerCareerRuntimeState runtime)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<PlayerCareerProfileStoreRecord> ApplyCommittedAsync(
        LogbookEntry entry,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        LogbookEntry.Commit(
            entry.EntryId,
            entry.Debrief,
            entry.CommittedAt,
            entry.CommitKind);

        PilotExperienceTotals increment =
            ExperienceIncrement(entry.Debrief);
        increment.Validate();

        await _applyGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PlayerCareerProfileStoreRecord current =
                await _runtime
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Player career onboarding must be completed before flight experience can be applied.");

            Guid debriefId = entry.Debrief.DebriefId;
            if (current.Profile.AppliedExperienceDebriefIds.Contains(debriefId))
            {
                if (current.SavedAt >= entry.CommittedAt)
                    return current;

                DateTimeOffset reconciledSavedAt =
                    savedAt < entry.CommittedAt
                        ? entry.CommittedAt
                        : savedAt;

                PlayerCareerProfileStoreRecord reconciled =
                    await _store
                        .SaveAsync(
                            current.Profile,
                            expectedRevision: current.Revision,
                            reconciledSavedAt,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (reconciled.SavedAt < entry.CommittedAt)
                {
                    throw new InvalidOperationException(
                        "Persisted Career/Profile application marker predates its authoritative logbook commit.");
                }

                _runtime.Replace(reconciled);
                return reconciled;
            }

            PilotExperienceTotals existing =
                current.Profile.Experience;

            var updatedExperience =
                new PilotExperienceTotals(
                    FlightCount:
                        checked(existing.FlightCount + increment.FlightCount),
                    CareerCreditTime:
                        existing.CareerCreditTime + increment.CareerCreditTime,
                    NightCareerCreditTime:
                        existing.NightCareerCreditTime
                        + increment.NightCareerCreditTime,
                    ActualInstrumentCareerCreditTime:
                        existing.ActualInstrumentCareerCreditTime
                        + increment.ActualInstrumentCareerCreditTime,
                    TakeoffCount:
                        checked(existing.TakeoffCount + increment.TakeoffCount),
                    LandingEpisodeCount:
                        checked(
                            existing.LandingEpisodeCount
                            + increment.LandingEpisodeCount));
            updatedExperience.Validate();

            PlayerCareerProfile updatedProfile =
                current.Profile with
                {
                    Experience = updatedExperience,
                    AppliedExperienceDebriefIds =
                        current.Profile.AppliedExperienceDebriefIds.Add(
                            debriefId)
                };
            updatedProfile.Validate();

            PlayerCareerProfileStoreRecord saved =
                await _store
                    .SaveAsync(
                        updatedProfile,
                        expectedRevision: current.Revision,
                        savedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (saved.SavedAt < entry.CommittedAt)
            {
                throw new InvalidOperationException(
                    "Persisted Career/Profile application marker predates its authoritative logbook commit.");
            }

            _runtime.Replace(saved);
            return saved;
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private static PilotExperienceTotals ExperienceIncrement(
        FlightDebrief debrief)
    {
        ArgumentNullException.ThrowIfNull(debrief);
        ArgumentNullException.ThrowIfNull(debrief.Time);
        ArgumentNullException.ThrowIfNull(debrief.Tracking);

        if (debrief.DebriefId == Guid.Empty)
        {
            throw new ArgumentException(
                "Debrief id is required for career experience credit.",
                nameof(debrief));
        }

        return new PilotExperienceTotals(
            FlightCount: 1,
            CareerCreditTime: debrief.Time.CareerCreditTime,
            NightCareerCreditTime: debrief.Time.NightCareerCreditTime,
            ActualInstrumentCareerCreditTime:
                debrief.Time.ActualInstrumentCareerCreditTime,
            TakeoffCount: debrief.Tracking.TakeoffCount,
            LandingEpisodeCount:
                debrief.Tracking.LandingEpisodeCount);
    }
}
