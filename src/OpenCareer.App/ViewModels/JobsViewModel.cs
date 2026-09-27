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
    private readonly ICareerJobBoardRefillService? _boardRefill;
    private readonly DevelopmentFlightService? _developmentFlights;
    private readonly ICareerJobPreFlightSessionRecoveryService? _startRecovery;
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
    private bool _aircraftDiscoveryAvailable;
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
                null,
            boardRefill:
                null,
            startRecovery:
                null)
    {
    }

    public JobsViewModel(
        IJobBoardStateStore jobBoards,
        PlayerCareerRuntimeState career,
        TimeProvider timeProvider,
        CareerJobAircraftSelectionSource? aircraftSelection,
        ICareerJobStartAction? startAction,
        ILogger<JobsViewModel>? logger,
        ICareerJobBoardRefillService? boardRefill = null,
        ICareerJobPreFlightSessionRecoveryService? startRecovery = null,
        DevelopmentFlightService? developmentFlights = null)
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
        _boardRefill =
            boardRefill;
        _developmentFlights =
            developmentFlights;
        _startRecovery =
            startRecovery;
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
    public CareerJobAircraftOption? SelectedAircraftOption =>
        _aircraftOptions.FirstOrDefault(option => string.Equals(
            option.AircraftId, _selectedAircraftId, StringComparison.OrdinalIgnoreCase));
    public string AirportText => _airportText;
    public string StatusText => _statusText;
    public string AircraftStatus => _aircraftStatus;
    public string AcceptanceStatus => _acceptanceStatus;

    public async Task GenerateDevelopmentFlightAsync(
        bool positionPilotAtKjfk,
        CancellationToken cancellationToken = default)
    {
        await _startGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);

        try
        {
            if (_developmentFlights is null)
            {
                throw new InvalidOperationException(
                    "Development flight generation is unavailable.");
            }

            await _developmentFlights
                .GenerateAsync(
                    positionPilotAtKjfk,
                    cancellationToken)
                .ConfigureAwait(true);

            await RefreshAsync(cancellationToken)
                .ConfigureAwait(true);

            SetField(
                ref _acceptanceStatus,
                "TEST / DEVELOPMENT offer ready. Select an installed aircraft; normal dispatch and FlightSession validation apply. Pay and career progression are zero.",
                nameof(AcceptanceStatus));
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(
                ex,
                "KJFK development flight generation failed.");

            SetField(
                ref _acceptanceStatus,
                ex.Message,
                nameof(AcceptanceStatus));
        }
        finally
        {
            _startGate.Release();
        }
    }

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

            if (_startRecovery is not null)
            {
                try
                {
                    await _startRecovery
                        .RecoverAsync(
                            cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Pre-FlightSession career-job recovery failed during Jobs refresh; persisted recovery state was preserved.");
                }
            }

            string currentAirport =
                career.Profile.Location.CurrentAirportIcao;

            JobBoardState? board;

            if (_boardRefill is not null)
            {
                try
                {
                    board =
                        await _boardRefill
                            .RefillAsync(
                                career.Profile,
                                cancellationToken)
                            .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Career job-board refill failed at {AirportIcao}; preserving the last authoritative board.",
                        currentAirport);

                    board =
                        await _jobBoards
                            .GetAsync(
                                currentAirport,
                                cancellationToken)
                            .ConfigureAwait(true);
                }
            }
            else
            {
                board =
                    await _jobBoards
                        .GetAsync(
                            currentAirport,
                            cancellationToken)
                        .ConfigureAwait(true);
            }

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

    public async Task RefreshAircraftAndReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        await _refreshGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);

        try
        {
            bool changed = await RefreshAircraftLockedAsync(cancellationToken);
            if (changed)
                await RebuildOffersLockedAsync(cancellationToken);
            else
                RefreshOfferExpirationLocked();
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

            // ItemsSource replacement can queue a null SelectionChanged behind this gate.
            // Discovery removes unavailable selections; a picker reset must not remove a valid one.
            if (normalized is null
                && _selectedAircraftId is not null
                && _aircraftOptions.Any(
                    item => string.Equals(
                        item.AircraftId,
                        _selectedAircraftId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

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

            // Restoring SelectedValue can echo the retained ID through SelectionChanged.
            if (string.Equals(
                    _selectedAircraftId,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedAircraftId =
                normalized;
            OnPropertyChanged(
                nameof(SelectedAircraftId));

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
            // CanStart is a UI projection, not permission to use retained discovery after
            // a disconnect between timer ticks. Recheck before invoking the start authority.
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await RefreshAircraftLockedAsync(cancellationToken);
                if (!_aircraftDiscoveryAvailable
                    || !string.Equals(aircraftId, _selectedAircraftId, StringComparison.OrdinalIgnoreCase))
                {
                    await RebuildOffersLockedAsync(cancellationToken);
                    return;
                }
            }
            finally
            {
                _refreshGate.Release();
            }

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

    private async Task<bool> RefreshAircraftLockedAsync(
        CancellationToken cancellationToken)
    {
        if (_aircraftSelection is null)
        {
            bool changed = _aircraftDiscoveryAvailable || _aircraftOptions.Count != 0;
            _aircraftDiscoveryAvailable = false;
            SetAircraftOptions(
                Array.Empty<CareerJobAircraftOption>());
            SetField(
                ref _aircraftStatus,
                "Aircraft selection is not connected to this view.",
                nameof(AircraftStatus));
            return changed;
        }

        CareerJobAircraftSelectionSnapshot snapshot =
            await _aircraftSelection
                .ReadAsync(cancellationToken)
                .ConfigureAwait(true);

        bool discoveryChanged = _aircraftDiscoveryAvailable != snapshot.IsAvailable
            || (snapshot.IsAvailable && !_aircraftOptions.SequenceEqual(snapshot.Aircraft));
        _aircraftDiscoveryAvailable = snapshot.IsAvailable;
        // Unavailable is uncertainty, not removal. Retain the visual choice, but never
        // evaluate/start with it until authoritative current discovery is available again.
        if (snapshot.IsAvailable)
            SetAircraftOptions(snapshot.Aircraft);

        SetField(
            ref _aircraftStatus,
            snapshot.Detail,
            nameof(AircraftStatus));
        return discoveryChanged;
    }

    private void RefreshOfferExpirationLocked()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!_offers.Any(item => item.IsExpired != (now < item.Offer.OfferedAt || now >= item.Offer.ExpiresAt)))
            return;

        // The timer must still disable expired offers, without repeating airport/dispatch I/O.
        SetOffers(_offers.Select(item => new JobOfferItemViewModel(
            item.Offer, now, item.CanStart,
            now < item.Offer.OfferedAt || now >= item.Offer.ExpiresAt
                ? "This persisted offer is no longer active." : item.ActionText,
            item.StartInputState)).ToArray());
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

            CareerJobStartInputState? startState =
                null;

            string actionText =
                locked
                    ? "Career access is not yet unlocked for this preview."
                    : expired
                        ? "This persisted offer is no longer active."
                        : _selectedAircraftId is null
                            ? "Select an installed aircraft to verify this offer."
                            : !_aircraftDiscoveryAvailable
                                ? "Aircraft discovery is temporarily unavailable. Waiting for current installed-aircraft evidence."
                            : _startAction is null
                                ? "Accept & Start is not connected to the playable-loop action."
                                : "Checking authoritative dispatch readiness…";

            if (active
                && _aircraftDiscoveryAvailable
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
                    startState =
                        availability.State;
                    actionText =
                        availability.State == CareerJobStartInputState.Ready
                            ? availability.Detail
                            : $"{availability.State} — {availability.Detail}";
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
                    actionText,
                    startState));
        }

        SetOffers(
            projected);

        JobOfferItemViewModel? firstBlocked =
            projected.FirstOrDefault(
                static offer =>
                    offer.IsActive
                    && !offer.CanStart
                    && offer.StartInputState is not null);

        SetField(
            ref _acceptanceStatus,
            _selectedAircraftId is null
                ? "Select an installed aircraft to verify an active offer for dispatch."
                : !_aircraftDiscoveryAvailable
                    ? "Aircraft selection retained. Start is disabled until current installed-aircraft evidence returns."
                : projected.Any(static offer => offer.CanStart)
                    ? "Selected aircraft has at least one verified startable career offer."
                    : firstBlocked is not null
                        ? $"Blocked: {firstBlocked.ActionText}"
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
        _selectedAircraftId =
            aircraft.FirstOrDefault(
                item => string.Equals(
                    item.AircraftId,
                    _selectedAircraftId,
                    StringComparison.OrdinalIgnoreCase))?.AircraftId;
        OnPropertyChanged(
            nameof(AircraftOptions));
        // Reapply the ID after the new items arrive, even when the ID did not change.
        // This restores the visual selection without relying on refreshed object identity.
        OnPropertyChanged(
            nameof(SelectedAircraftId));
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
        string? actionText = null,
        CareerJobStartInputState? startInputState = null)
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

        StartInputState =
            startInputState;

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
    public CareerJobStartInputState? StartInputState { get; }
    public string ActionText { get; }

    public string Route =>
        $"{Offer.OriginIcao} → {Offer.DestinationIcao}";

    public string KindText =>
        DevelopmentFlight.IsDevelopment(Offer)
            ? DevelopmentFlight.Name
            : Friendly(
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
