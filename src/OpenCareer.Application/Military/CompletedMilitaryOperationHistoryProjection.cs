using OpenCareer.Domain.Conflict;

namespace OpenCareer.Application.Military;

/// <summary>
/// Builds the stable, read-only application projection consumed by completed
/// military-operation presentation surfaces. Ordering is deterministic and
/// terminal faction postures remain sourced from persisted campaign history.
/// </summary>
public static class CompletedMilitaryOperationHistoryProjection
{
    public static IReadOnlyList<CompletedMilitaryOperationPresentation> Build(
        IEnumerable<ConflictCampaignHistoryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return history
            .OrderByDescending(static entry => entry.EndedAt)
            .ThenBy(static entry => entry.CampaignId, StringComparer.Ordinal)
            .Select(CompletedMilitaryOperationPresentationBuilder.Build)
            .ToArray();
    }
}
