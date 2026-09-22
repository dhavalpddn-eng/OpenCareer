using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Domain.Careers;

namespace OpenCareer.App.ViewModels;

public sealed class JobsViewModel : INotifyPropertyChanged
{
    private readonly IJobBoardStateStore _jobBoards;
    private readonly PlayerCareerRuntimeState _career;
    private readonly TimeProvider _timeProvider;
    private readonly CareerJobAircraftSelectionSource? _aircraftSelection;
    private readonly ICareerJobStartAction? _startAction;
    private readonly ILogger<JobsViewModel>? _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private IReadOnlyList<JobMarketOfferDraft> _boardOffers =
        Array.Empty<JobMarketOfferDraft>();
    private IReadOnlyList<JobOfferItemViewModel> _offers =
        Array.Empty<JobOfferItemViewModel>();
    private IReadOnlyList<CareerJobAircraftOption> _aircraftOptions =
        Array.Empty<CareerJobAircraftOption>();
    private string? _selectedAircraftId;
    private string _airportText = "Career location unavailable";
    private string _statusText = "Jobs have not been loaded yet.";
    private string _aircraftStatus =
        "Installed-aircraft catalog has not been checked yet.";
    private string _acceptanceStatus =
        "Select an installed aircraft to verify an active offer for dispatch.";

    public JobsViewModel(
        IJobBoardStateStore jobBoards,
        PlayerCareerRuntimeState career,
        TimeProvider timeProvider)
        : this(
            jobBoards,
            career,
            timeProvider,
            aircraftSelection:
                null,
            startAction:
                null,
            logger:
                null)
    {
    }

    public JobsViewModel(
        IJobBoardStateStore jobBoards,
        PlayerCareerRuntimeState career,
        TimeProvider timeProvider,
        CareerJobAircraftSelectionSource? aircraftSelection,
        ICareerJobStartAction? startAction,
        ILogger<JobsViewModel>? logger)
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
        _aircraftSelection =
            aircraftSelection;
        _startAction =
            startAction;
        _logger =
            logger;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<JobOfferItemViewModel> Offers => _offers;
    public IReadOnlyList<CareerJobAircraftOption> AircraftOptions => _aircraftOptions;
    public string? SelectedAircraftId => _selectedAircraftId;
    public string AirportText => _airportText;
    public string StatusText => _statusText;
    public string AircraftStatus => _aircraftStatus;
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
                _boardOffers =
                    Array.Empty<JobMarketOfferDraft>();
                SetOffers(
                    Array.Empty<JobOfferItemViewModel>());
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

            SetField(
                ref _airportText,
                $"LOCAL BOARD · {currentAirport}",
                nameof(AirportText));

            _boardOffers =
                board?.Offers.ToArray()
                ?? Array.Empty<JobMarketOfferDraft>();

            if (board is not null)
                board.Validate();

            await RefreshAircraftLockedAsync(
                cancellationToken);

            await RebuildOffersLockedAsync(
                cancellationToken);

            int actionable =
                _offers.Count(static offer => offer.IsActive);
            int locked =
                _offers.Count(static offer => offer.IsLockedPreview);
            int expired =
                _offers.Count(static offer => offer.IsExpired);

            SetField(
                ref _statusText,
                board is null
                    ? $"No persisted job board exists for {currentAirport}."
                    : _offers.Count == 0
                        ? $"The persisted {currentAirport} board currently has no offers."
                        : $"{_offers.Count} persisted offer{(_offers.Count == 1 ? string.Empty : "s")} · {actionable} active · {locked} locked preview{(locked == 1 ? string.Empty : "s")} · {expired} expired.",
                nameof(StatusText));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task SelectAircraftAsync(
        string? aircraftId,
        CancellationToken cancellationToken = default)
    {
        await _refreshGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);

        try
        {
            string? normalized =
                string.IsNullOrWhiteSpace(aircraftId)
                    ? null
                    : aircraftId.Trim();

            if (normalized is not null
                && !_aircraftOptions.Any(
                    item => string.Equals(
                        item.AircraftId,
                        normalized,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Selected aircraft is not present in the authoritative installed-aircraft catalog.");
            }

            if (!string.Equals(
                    _selectedAircraftId,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                _selectedAircraftId =
                    normalized;
                OnPropertyChanged(
                    nameof(SelectedAircraftId));
            }

            await RebuildOffersLockedAsync(
                cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task StartOfferAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        if (_startAction is null)
            return;

        string aircraftId =
            _selectedAircraftId
            ?? throw new InvalidOperationException(
                "Select an installed aircraft before starting a career flight.");

        await _startGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);

        try
        {
            SetField(
                ref _acceptanceStatus,
                "Accepting offer, reserving aircraft, verifying dispatch, and starting the persistent FlightSession…",
                nameof(AcceptanceStatus));

            CareerJobPlayableStartResult result =
                await _startAction
                    .StartAsync(
                        offerId,
                        aircraftId,
                        cancellationToken);

            await RefreshAsync(
                cancellationToken);

            SetField(
                ref _acceptanceStatus,
                $"Career flight started: {result.StartedFlight.Contract.Contract.OriginIcao} → {result.StartedFlight.Contract.Contract.DestinationIcao}. Open Current Flight to fly the operation.",
                nameof(AcceptanceStatus));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Career job start failed for offer {OfferId}.",
                offerId);

            SetField(
                ref _acceptanceStatus,
                $"Accept & Start failed: {ex.Message}",
                nameof(AcceptanceStatus));

            await _refreshGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(true);

            try
            {
                await RebuildOffersLockedAsync(
                    cancellationToken);
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task RefreshAircraftLockedAsync(
        CancellationToken cancellationToken)
    {
        if (_aircraftSelection is null)
        {
            SetAircraftOptions(
                Array.Empty<CareerJobAircraftOption>());
            SetField(
                ref _aircraftStatus,
                "Aircraft selection is not connected to this view.",
                nameof(AircraftStatus));
            return;
        }

        CareerJobAircraftSelectionSnapshot snapshot =
            await _aircraftSelection
                .ReadAsync(cancellationToken)
                .ConfigureAwait(true);

        SetAircraftOptions(
            snapshot.Aircraft);

        SetField(
            ref _aircraftStatus,
            snapshot.Detail,
            nameof(AircraftStatus));

        if (_selectedAircraftId is not null
            && !_aircraftOptions.Any(
                item => string.Equals(
                    item.AircraftId,
                    _selectedAircraftId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _selectedAircraftId =
                null;
            OnPropertyChanged(
                nameof(SelectedAircraftId));
        }
    }

    private async Task RebuildOffersLockedAsync(
        CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            _timeProvider.GetUtcNow();

        var projected =
            new List<JobOfferItemViewModel>(
                _boardOffers.Count);

        foreach (JobMarketOfferDraft offer
            in _boardOffers
                .OrderBy(static offer => offer.IsLockedPreview)
                .ThenBy(static offer => offer.ExpiresAt)
                .ThenBy(static offer => offer.OfferId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool locked =
                offer.IsLockedPreview;
            bool expired =
                now < offer.OfferedAt
                || now >= offer.ExpiresAt;
            bool active =
                !locked
                && !expired;

            bool canStart =
                false;

            string actionText =
                locked
                    ? "Career access is not yet unlocked for this preview."
                    : expired
                        ? "This persisted offer is no longer active."
                        : _selectedAircraftId is null
                            ? "Select an installed aircraft to verify this offer."
                            : _startAction is null
                                ? "Accept & Start is not connected to the playable-loop action."
                                : "Checking authoritative dispatch readiness…";

            if (active
                && _selectedAircraftId is not null
                && _startAction is not null)
            {
                try
                {
                    CareerJobStartActionAvailability availability =
                        await _startAction
                            .ReadAvailabilityAsync(
                                offer.OfferId,
                                _selectedAircraftId,
                                cancellationToken)
                            .ConfigureAwait(true);

                    canStart =
                        availability.CanStart;
                    actionText =
                        availability.Detail;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Career job start readiness failed for offer {OfferId}.",
                        offer.OfferId);

                    actionText =
                        "Dispatch readiness could not be verified.";
                }
            }

            projected.Add(
                new(
                    offer,
                    now,
                    canStart,
                    actionText));
        }

        SetOffers(
            projected);

        SetField(
            ref _acceptanceStatus,
            _selectedAircraftId is null
                ? "Select an installed aircraft to verify an active offer for dispatch."
                : projected.Any(static offer => offer.CanStart)
                    ? "Selected aircraft has at least one verified startable career offer."
                    : "No active offer is currently startable with the selected aircraft.",
            nameof(AcceptanceStatus));
    }

    private void SetOffers(
        IReadOnlyList<JobOfferItemViewModel> offers)
    {
        _offers =
            offers;
        OnPropertyChanged(
            nameof(Offers));
    }

    private void SetAircraftOptions(
        IReadOnlyList<CareerJobAircraftOption> aircraft)
    {
        _aircraftOptions =
            aircraft;
        OnPropertyChanged(
            nameof(AircraftOptions));
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

        field =
            value;
        OnPropertyChanged(
            propertyName);
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
}

public sealed class JobOfferItemViewModel
{
    public JobOfferItemViewModel(
        JobMarketOfferDraft offer,
        DateTimeOffset now,
        bool canStart = false,
        string? actionText = null)
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

        CanStart =
            IsActive
            && canStart;

        ActionText =
            actionText
            ?? (IsLockedPreview
                ? "Career access is not yet unlocked for this preview."
                : IsExpired
                    ? "This persisted offer is no longer active."
                    : "Select an installed aircraft to verify this offer.");
    }

    public JobMarketOfferDraft Offer { get; }
    public Guid OfferId => Offer.OfferId;
    public bool IsLockedPreview { get; }
    public bool IsExpired { get; }
    public bool IsActive { get; }
    public bool CanStart { get; }
    public string ActionText { get; }

    public string Route =>
        $"{Offer.OriginIcao} → {Offer.DestinationIcao}";

    public string KindText =>
        Friendly(
            Offer.Kind);

    public string TrackText =>
        Friendly(
            Offer.ServiceTrack);

    public string ScenarioText =>
        Friendly(
            Offer.Scenario);

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
                : CanStart
                    ? "READY TO START"
                    : "ACTIVE OFFER";

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
