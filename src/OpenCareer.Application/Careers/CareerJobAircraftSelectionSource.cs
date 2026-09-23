using OpenCareer.Application.Fleet;
using OpenCareer.Application.Ownership;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobAircraftOption(
    string SelectionId,
    string AircraftId,
    string DisplayName,
    string? OwnershipId = null,
    Guid? ProviderAircraftInstanceId = null,
    Guid? ProviderOfferId = null,
    string? ProviderOriginIcao = null);

public sealed record CareerJobAircraftSelectionSnapshot(
    bool IsAvailable,
    IReadOnlyList<CareerJobAircraftOption> Aircraft,
    string Detail);

public sealed class CareerJobAircraftSelectionSource
{
    private const string OwnershipSelectionPrefix =
        "ownership:";
    private const string ProviderSelectionPrefix =
        "provider:";

    private readonly PlayerCareerRuntimeState _career;
    private readonly IOwnershipStore _ownership;
    private readonly IAircraftAvailabilityStore _availability;
    private readonly IJobBoardStateStore? _jobBoards;
    private readonly TimeProvider _timeProvider;

    public CareerJobAircraftSelectionSource(
        PlayerCareerRuntimeState career,
        IOwnershipStore ownership,
        IAircraftAvailabilityStore availability,
        IJobBoardStateStore? jobBoards = null,
        TimeProvider? timeProvider = null)
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
        _jobBoards =
            jobBoards;
        _timeProvider =
            timeProvider
            ?? TimeProvider.System;
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

        var providerInstanceIds =
            new HashSet<Guid>();

        int providerCount = 0;
        int ownedCount = 0;

        if (_jobBoards is not null)
        {
            JobBoardState? board =
                await _jobBoards
                    .GetAsync(
                        currentAirport,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (board is not null)
            {
                board.Validate();

                if (!string.Equals(
                        board.AirportIcao,
                        currentAirport,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Job board does not belong to the active career location.");
                }

                DateTimeOffset now =
                    _timeProvider.GetUtcNow();

                foreach (JobMarketOfferDraft offer
                    in board.Offers)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (offer.IsLockedPreview
                        || now < offer.OfferedAt
                        || now >= offer.ExpiresAt
                        || offer.ContractTerms?.ProviderAircraft
                            is not { } provider)
                    {
                        continue;
                    }

                    provider.Validate(
                        offer.OriginIcao);

                    if (!providerInstanceIds.Add(
                            provider.ProviderAircraftInstanceId))
                    {
                        throw new InvalidOperationException(
                            "Job board contains a duplicate provider-aircraft instance identity.");
                    }

                    string aircraftId =
                        provider.AircraftId.Trim();

                    if (!await IsAircraftAvailableAsync(
                            aircraftId)
                        .ConfigureAwait(false))
                    {
                        continue;
                    }

                    aircraft.Add(
                        new(
                            $"{ProviderSelectionPrefix}{provider.ProviderAircraftInstanceId:D}",
                            aircraftId,
                            $"{provider.DisplayName.Trim()} · Contract supplied · {offer.OriginIcao} → {offer.DestinationIcao}",
                            OwnershipId:
                                null,
                            ProviderAircraftInstanceId:
                                provider.ProviderAircraftInstanceId,
                            ProviderOfferId:
                                offer.OfferId,
                            ProviderOriginIcao:
                                provider.OriginIcao));

                    providerCount++;
                }
            }
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

            if (!await IsAircraftAvailableAsync(
                    aircraftId)
                .ConfigureAwait(false))
            {
                continue;
            }

            aircraft.Add(
                new(
                    $"{OwnershipSelectionPrefix}{ownershipId}",
                    aircraftId,
                    owned.DisplayName.Trim(),
                    ownershipId));

            ownedCount++;
        }

        CareerJobAircraftOption[] ordered =
            aircraft
                .OrderBy(
                    static item =>
                        item.ProviderAircraftInstanceId
                            is null
                            ? 1
                            : 0)
                .ThenBy(
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
                ? $"No contract-supplied or available career-owned aircraft are located at {currentAirport}."
                : $"{providerCount} contract-supplied and {ownedCount} career-owned aircraft available at {currentAirport}.";

        return new(
            IsAvailable:
                true,
            ordered,
            detail);

        async Task<bool> IsAircraftAvailableAsync(
            string aircraftId)
        {
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

            return availability is null
                || availability.IsAvailable;
        }
    }
}
