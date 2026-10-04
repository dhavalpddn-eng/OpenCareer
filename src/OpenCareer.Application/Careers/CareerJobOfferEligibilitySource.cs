using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public enum CareerJobOfferEligibilityState
{
    Ready = 0,
    NoCareer = 1,
    NoBoard = 2,
    AircraftDiscoveryUnavailable = 3,
    NoEligibleOffers = 4
}

public sealed record CareerJobEligibleOffer(
    JobMarketOfferDraft Offer,
    IReadOnlyList<CareerJobAircraftOption> QualifyingAircraft);

public sealed record CareerJobOfferEligibilitySnapshot(
    CareerJobOfferEligibilityState State,
    IReadOnlyList<CareerJobEligibleOffer> Offers,
    string Detail)
{
    public bool IsReady =>
        State == CareerJobOfferEligibilityState.Ready;
}

public sealed class CareerJobOfferEligibilitySource
{
    private readonly IJobBoardStateStore _jobBoards;
    private readonly PlayerCareerRuntimeState _career;
    private readonly CareerJobAircraftSelectionSource _aircraftSelection;
    private readonly ICareerJobStartAction _startAction;
    private readonly TimeProvider _timeProvider;

    public CareerJobOfferEligibilitySource(
        IJobBoardStateStore jobBoards,
        PlayerCareerRuntimeState career,
        CareerJobAircraftSelectionSource aircraftSelection,
        ICareerJobStartAction startAction,
        TimeProvider timeProvider)
    {
        _jobBoards =
            jobBoards
            ?? throw new ArgumentNullException(nameof(jobBoards));
        _career =
            career
            ?? throw new ArgumentNullException(nameof(career));
        _aircraftSelection =
            aircraftSelection
            ?? throw new ArgumentNullException(nameof(aircraftSelection));
        _startAction =
            startAction
            ?? throw new ArgumentNullException(nameof(startAction));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CareerJobOfferEligibilitySnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfileStoreRecord? careerRecord =
            await _career
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);

        if (careerRecord is null)
        {
            return Blocked(
                CareerJobOfferEligibilityState.NoCareer,
                "Career onboarding must be complete before job eligibility can be read.");
        }

        string currentAirport =
            careerRecord.Profile.Location.CurrentAirportIcao;

        JobBoardState? board =
            await _jobBoards
                .GetAsync(
                    currentAirport,
                    cancellationToken)
                .ConfigureAwait(false);

        if (board is null)
        {
            return Blocked(
                CareerJobOfferEligibilityState.NoBoard,
                $"No persisted job board is available for {currentAirport}.");
        }

        board.Validate();
        if (!string.Equals(
                board.AirportIcao,
                currentAirport,
                StringComparison.Ordinal))
        {
            return Blocked(
                CareerJobOfferEligibilityState.NoBoard,
                $"No authoritative local job board is available for {currentAirport}.");
        }

        CareerJobAircraftSelectionSnapshot aircraft =
            await _aircraftSelection
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);

        if (!aircraft.IsAvailable)
        {
            return Blocked(
                CareerJobOfferEligibilityState.AircraftDiscoveryUnavailable,
                aircraft.Detail);
        }

        DateTimeOffset now =
            _timeProvider.GetUtcNow();

        JobMarketOfferDraft[] activeOffers =
            board.Offers
                .Where(
                    offer =>
                        !offer.IsLockedPreview
                        && offer.OfferedAt <= now
                        && offer.ExpiresAt > now)
                .ToArray();

        var eligible =
            new List<CareerJobEligibleOffer>();

        foreach (JobMarketOfferDraft offer in activeOffers)
        {
            var qualifyingAircraft =
                new List<CareerJobAircraftOption>();

            foreach (CareerJobAircraftOption option in aircraft.Aircraft)
            {
                cancellationToken.ThrowIfCancellationRequested();

                CareerJobStartActionAvailability availability =
                    await _startAction
                        .ReadAvailabilityAsync(
                            offer.OfferId,
                            option.AircraftId,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (availability.CanStart)
                    qualifyingAircraft.Add(option);
            }

            if (qualifyingAircraft.Count > 0)
            {
                eligible.Add(
                    new CareerJobEligibleOffer(
                        offer,
                        qualifyingAircraft.ToArray()));
            }
        }

        if (eligible.Count == 0)
        {
            return Blocked(
                CareerJobOfferEligibilityState.NoEligibleOffers,
                "No persisted local offers are startable with the currently installed aircraft.");
        }

        return new(
            CareerJobOfferEligibilityState.Ready,
            eligible.ToArray(),
            $"{eligible.Count} persisted local job offer(s) are currently startable.");
    }

    private static CareerJobOfferEligibilitySnapshot Blocked(
        CareerJobOfferEligibilityState state,
        string detail) =>
        new(
            state,
            Array.Empty<CareerJobEligibleOffer>(),
            detail);
}
