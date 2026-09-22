using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.App.ViewModels;

public sealed class JobsViewModel : INotifyPropertyChanged
{
    private readonly IJobBoardStateStore _jobBoards;
    private readonly PlayerCareerRuntimeState _career;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private IReadOnlyList<JobOfferItemViewModel> _offers =
        Array.Empty<JobOfferItemViewModel>();
    private string _airportText = "Career location unavailable";
    private string _statusText = "Jobs have not been loaded yet.";
    private string _acceptanceStatus =
        "Accept & Start is unavailable until authoritative offer-to-contract terms and dispatch inputs are composed.";

    public JobsViewModel(
        IJobBoardStateStore jobBoards,
        PlayerCareerRuntimeState career,
        TimeProvider timeProvider)
    {
        _jobBoards =
            jobBoards
            ?? throw new ArgumentNullException(nameof(jobBoards));
        _career =
            career
            ?? throw new ArgumentNullException(nameof(career));
        _timeProvider =
            timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<JobOfferItemViewModel> Offers => _offers;
    public string AirportText => _airportText;
    public string StatusText => _statusText;
    public string AcceptanceStatus => _acceptanceStatus;

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _refreshGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);

        try
        {
            PlayerCareerProfileStoreRecord? career =
                await _career
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(true);

            if (career is null)
            {
                SetOffers(Array.Empty<JobOfferItemViewModel>());
                SetField(
                    ref _airportText,
                    "Career onboarding required",
                    nameof(AirportText));
                SetField(
                    ref _statusText,
                    "Complete career onboarding before local job offers can be shown.",
                    nameof(StatusText));
                return;
            }

            string currentAirport =
                career.Profile.Location.CurrentAirportIcao;

            JobBoardState? board =
                await _jobBoards
                    .GetAsync(
                        currentAirport,
                        cancellationToken)
                    .ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            SetField(
                ref _airportText,
                $"LOCAL BOARD · {currentAirport}",
                nameof(AirportText));

            if (board is null)
            {
                SetOffers(Array.Empty<JobOfferItemViewModel>());
                SetField(
                    ref _statusText,
                    $"No persisted job board exists for {currentAirport}.",
                    nameof(StatusText));
                return;
            }

            board.Validate();

            DateTimeOffset now =
                _timeProvider.GetUtcNow();

            JobOfferItemViewModel[] offers =
                board.Offers
                    .OrderBy(static offer => offer.IsLockedPreview)
                    .ThenBy(static offer => offer.ExpiresAt)
                    .ThenBy(static offer => offer.OfferId)
                    .Select(offer =>
                        new JobOfferItemViewModel(
                            offer,
                            now))
                    .ToArray();

            SetOffers(offers);

            int actionable =
                offers.Count(static offer => offer.IsActive);

            int locked =
                offers.Count(static offer => offer.IsLockedPreview);

            int expired =
                offers.Count(static offer => offer.IsExpired);

            SetField(
                ref _statusText,
                offers.Length == 0
                    ? $"The persisted {currentAirport} board currently has no offers."
                    : $"{offers.Length} persisted offer{(offers.Length == 1 ? string.Empty : "s")} · {actionable} active · {locked} locked preview{(locked == 1 ? string.Empty : "s")} · {expired} expired.",
                nameof(StatusText));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void SetOffers(
        IReadOnlyList<JobOfferItemViewModel> offers)
    {
        _offers = offers;
        OnPropertyChanged(nameof(Offers));
    }

    private void SetField(
        ref string field,
        string value,
        string propertyName)
    {
        if (string.Equals(
                field,
                value,
                StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed class JobOfferItemViewModel
{
    public JobOfferItemViewModel(
        JobMarketOfferDraft offer,
        DateTimeOffset now)
    {
        Offer =
            offer
            ?? throw new ArgumentNullException(nameof(offer));

        IsLockedPreview =
            offer.IsLockedPreview;

        IsExpired =
            now < offer.OfferedAt
            || now >= offer.ExpiresAt;

        IsActive =
            !IsLockedPreview
            && !IsExpired;
    }

    public JobMarketOfferDraft Offer { get; }
    public Guid OfferId => Offer.OfferId;
    public bool IsLockedPreview { get; }
    public bool IsExpired { get; }
    public bool IsActive { get; }

    public string Route =>
        $"{Offer.OriginIcao} → {Offer.DestinationIcao}";

    public string KindText =>
        Friendly(Offer.Kind);

    public string TrackText =>
        Friendly(Offer.ServiceTrack);

    public string ScenarioText =>
        Friendly(Offer.Scenario);

    public string DistanceText =>
        $"{Offer.DistanceNm:0} nm";

    public string EstimatedTimeText =>
        Offer.EstimatedFlightHours is { } hours
            ? $"{hours:0.0} hr est."
            : "Time estimate unavailable";

    public string ExpirationText =>
        $"Expires {Offer.ExpiresAt.ToLocalTime():g}";

    public string AvailabilityText =>
        IsLockedPreview
            ? "LOCKED PREVIEW"
            : IsExpired
                ? "EXPIRED"
                : "ACTIVE OFFER";

    public string ActionText =>
        IsLockedPreview
            ? "Career access is not yet unlocked for this preview."
            : IsExpired
                ? "This persisted offer is no longer active."
                : "Accept & Start is blocked until authoritative contract terms and dispatch inputs are composed.";

    private static string Friendly<T>(
        T value)
        where T : struct, Enum =>
        string.Concat(
            value
                .ToString()
                .Select(
                    (character, index) =>
                        index > 0
                        && char.IsUpper(character)
                            ? $" {character}"
                            : character.ToString()));
}
