using OpenCareer.Application.Fleet;
using OpenCareer.Application.Ownership;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobAircraftOption(
    string SelectionId,
    string AircraftId,
    string DisplayName,
    string? OwnershipId = null);

public sealed record CareerJobAircraftSelectionSnapshot(
    bool IsAvailable,
    IReadOnlyList<CareerJobAircraftOption> Aircraft,
    string Detail);

public sealed class CareerJobAircraftSelectionSource
{
    private const string OwnershipSelectionPrefix =
        "ownership:";

    private readonly PlayerCareerRuntimeState _career;
    private readonly IOwnershipStore _ownership;
    private readonly IAircraftAvailabilityStore _availability;

    public CareerJobAircraftSelectionSource(
        PlayerCareerRuntimeState career,
        IOwnershipStore ownership,
        IAircraftAvailabilityStore availability)
    {
        _career =
            career
            ?? throw new ArgumentNullException(nameof(career));
        _ownership =
            ownership
            ?? throw new ArgumentNullException(nameof(ownership));
        _availability =
            availability
            ?? throw new ArgumentNullException(nameof(availability));
    }

    public async Task<CareerJobAircraftSelectionSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfileStoreRecord? career =
            await _career
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

        if (career is null)
        {
            return new(
                IsAvailable:
                    false,
                Array.Empty<CareerJobAircraftOption>(),
                "Complete career onboarding before selecting a career aircraft.");
        }

        string careerId =
            career.Profile.CareerId.ToString("D");

        OwnershipSnapshot snapshot =
            await _ownership
                .LoadSnapshotAsync(
                    careerId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!string.Equals(
                snapshot.Account.CareerId,
                careerId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Ownership snapshot does not belong to the active career.");
        }

        string currentAirport =
            career.Profile.Location.CurrentAirportIcao;

        var availabilityByAircraft =
            new Dictionary<string, AircraftAvailabilityState?>(
                StringComparer.OrdinalIgnoreCase);

        var ownershipIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        var aircraft =
            new List<CareerJobAircraftOption>();

        foreach (OwnedAircraft owned in snapshot.Aircraft)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owned.Validate();

            if (!string.Equals(
                    owned.CareerId,
                    careerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Owned aircraft does not belong to the active career.");
            }

            string ownershipId =
                owned.OwnershipId.Trim();

            if (!ownershipIds.Add(ownershipId))
            {
                throw new InvalidOperationException(
                    "Ownership snapshot contains a duplicate ownership identity.");
            }

            if (owned.Status != OwnedAircraftStatus.Active
                || !string.Equals(
                    owned.CurrentAirportIcao.Trim(),
                    currentAirport,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string aircraftId =
                owned.AircraftId.Trim();

            if (!availabilityByAircraft.TryGetValue(
                    aircraftId,
                    out AircraftAvailabilityState? availability))
            {
                availability =
                    await _availability
                        .FindAsync(
                            aircraftId,
                            cancellationToken)
                        .ConfigureAwait(false);

                availability?.Validate();

                if (availability is not null
                    && !string.Equals(
                        availability.CanonicalAircraftId.Trim(),
                        aircraftId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Fleet availability state does not belong to the requested canonical aircraft.");
                }

                availabilityByAircraft.Add(
                    aircraftId,
                    availability);
            }

            if (availability is not null
                && !availability.IsAvailable)
            {
                continue;
            }

            aircraft.Add(
                new(
                    $"{OwnershipSelectionPrefix}{ownershipId}",
                    aircraftId,
                    owned.DisplayName.Trim(),
                    ownershipId));
        }

        CareerJobAircraftOption[] ordered =
            aircraft
                .OrderBy(
                    static item => item.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static item => item.AircraftId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static item => item.OwnershipId,
                    StringComparer.Ordinal)
                .ToArray();

        string detail =
            ordered.Length == 0
                ? $"No available career-owned aircraft are located at {currentAirport}."
                : $"{ordered.Length} available career-owned aircraft at {currentAirport}.";

        return new(
            IsAvailable:
                true,
            ordered,
            detail);
    }
}
