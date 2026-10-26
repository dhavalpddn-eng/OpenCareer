using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Tutorials;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.App.ViewModels;

public enum JobsNavigationTarget
{
    Dispatch = 0,
    CurrentFlight
}

public sealed class JobsNavigationRequestedEventArgs(
    JobsNavigationTarget target)
    : EventArgs
{
    public JobsNavigationTarget Target { get; } = target;
}

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
    private readonly IAcceptedDispatchPlanningContextSource? _acceptedContext;
    private readonly IAcceptedJobCancellationService? _acceptedCancellation;
    private readonly CareerJobPlayableLoopReadinessSource? _playableReadiness;
    private readonly FlightSessionCoordinator? _flightSessions;
    private readonly ILogger<JobsViewModel>? _logger;
    private readonly FirstJobTutorialAutoTrigger? _firstJobTutorial;
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
    private bool _isRefreshing;
    private bool _isJobActionBusy;
    private bool _isCancellingAcceptedJobs;
    private IReadOnlyList<Guid> _acceptedContractIds = Array.Empty<Guid>();
    private bool _hasCurrentFlightSession;
    private string _currentFlightSessionStatus = "NO CURRENT FLIGHT SESSION";
    private string _currentFlightSessionDetail =
        "Accept and start a career job to create a FlightSession.";
    private bool _hasJobRecoveryAttention;
    private string _jobRecoveryStatus = "JOB RECOVERY · READY";
    private string _jobRecoveryDetail =
        "No accepted or in-progress job requires recovery attention.";

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
        DevelopmentFlightService? developmentFlights = null,
        FirstJobTutorialAutoTrigger? firstJobTutorial = null,
        IAcceptedDispatchPlanningContextSource? acceptedContext = null,
        IAcceptedJobCancellationService? acceptedCancellation = null,
        CareerJobPlayableLoopReadinessSource? playableReadiness = null,
        FlightSessionCoordinator? flightSessions = null)
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
        _acceptedContext =
            acceptedContext;
        _acceptedCancellation =
            acceptedCancellation;
        _playableReadiness =
            playableReadiness;
        _flightSessions =
            flightSessions;
        _logger =
            logger;
        _firstJobTutorial =
            firstJobTutorial;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<JobsNavigationRequestedEventArgs>? NavigationRequested;

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
    public bool IsRefreshing => _isRefreshing;
    public bool IsJobActionBusy => _isJobActionBusy;
    public bool CanRunJobActions => !_isJobActionBusy;
    public bool CanStartJobActions =>
        CanRunJobActions
        && !HasJobRecoveryAttention
        && !HasCurrentFlightSession;
    public bool HasCurrentFlightSession => _hasCurrentFlightSession;
    public string CurrentFlightSessionStatus => _currentFlightSessionStatus;
    public string CurrentFlightSessionDetail => _currentFlightSessionDetail;
    public bool HasJobRecoveryAttention => _hasJobRecoveryAttention;
    public string JobRecoveryStatus => _jobRecoveryStatus;
    public string JobRecoveryDetail => _jobRecoveryDetail;
    public bool CanCancelAcceptedJobs =>
        CanRunJobActions
        && !_isCancellingAcceptedJobs
        && _acceptedCancellation is not null
        && _acceptedContractIds.Count > 0;
    public string CancelAcceptedJobsButtonText =>
        _isCancellingAcceptedJobs
            ? "Cancelling…"
            : _acceptedContractIds.Count > 1
                ? $"Cancel All {_acceptedContractIds.Count} Accepted Jobs"
                : _acceptedContractIds.Count == 1
                    ? "Cancel Accepted Job"
                    : "No Accepted Job to Cancel";
    public string CancelAcceptedJobsDialogText =>
        _acceptedContractIds.Count > 1
            ? $"All {_acceptedContractIds.Count} accepted preflight contracts will be cancelled and their aircraft reservations released. No flights will be started."
            : "The accepted preflight contract will be cancelled, its prepared plan cleared, and its aircraft reservation released. No pay or logbook entry will be created.";

    public async Task GenerateDevelopmentFlightAsync(
        bool positionPilotAtKjfk,
        CancellationToken cancellationToken = default)
    {
        if (!await _startGate
                .WaitAsync(0, cancellationToken)
                .ConfigureAwait(true))
        {
            return;
        }

        SetJobActionBusy(true);

        try
        {
            if (BlockNewFlightAction())
                return;

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
                "ZERO-PAY CALIBRATION ready. It validates the flight pipeline only and will award $0. Choose an Available Contract to test earnings and career progression.",
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
            SetJobActionBusy(false);
            _startGate.Release();
        }
    }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _refreshGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);

        SetRefreshing(true);

        try
        {
            PlayerCareerProfileStoreRecord? career =
                await Task.Run(
                        () => _career.InitializeAsync(cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(true);

            if (career is null)
            {
                RefreshJobRecoveryAttention(
                    result:
                        null,
                    recoveryFailed:
                        false);
                RefreshCurrentFlightSession();
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

            CareerJobPreFlightSessionRecoveryResult? recoveryResult =
                null;
            bool recoveryFailed =
                false;

            if (_startRecovery is not null)
            {
                try
                {
                    recoveryResult =
                        await Task.Run(
                            () => _startRecovery.RecoverAsync(cancellationToken),
                            cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    recoveryFailed =
                        true;
                    _logger?.LogWarning(
                        ex,
                        "Pre-FlightSession career-job recovery failed during Jobs refresh; persisted recovery state was preserved.");
                }
            }

            RefreshJobRecoveryAttention(
                recoveryResult,
                recoveryFailed);
            RefreshCurrentFlightSession();

            string currentAirport =
                career.Profile.Location.CurrentAirportIcao;

            JobBoardState? board;

            if (_boardRefill is not null)
            {
                try
                {
                    board =
                        await Task.Run(
                                () => _boardRefill.RefillAsync(
                                career.Profile,
                                cancellationToken),
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
                        await Task.Run(
                                () => _jobBoards.GetAsync(
                                currentAirport,
                                cancellationToken),
                                cancellationToken)
                            .ConfigureAwait(true);
                }
            }
            else
            {
                board =
                    await Task.Run(
                            () => _jobBoards.GetAsync(
                            currentAirport,
                            cancellationToken),
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

            await RefreshAcceptedJobsAsync(
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

            if (_firstJobTutorial is not null)
            {
                await _firstJobTutorial
                    .TryStartAsync(cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        finally
        {
            SetRefreshing(false);
            _refreshGate.Release();
        }
    }

    public void ReportRefreshFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _logger?.LogError(
            exception,
            "Jobs view refresh failed.");

        SetField(
            ref _statusText,
            $"Jobs could not refresh: {exception.Message} Click Refresh Jobs to retry.",
            nameof(StatusText));
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

            RefreshCurrentFlightSession();
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
        if (!await _startGate
                .WaitAsync(0, cancellationToken)
                .ConfigureAwait(true))
        {
            return;
        }

        SetJobActionBusy(true);

        try
        {
            if (BlockNewFlightAction())
                return;

            await _refreshGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(true);

            try
            {
                if (BlockNewFlightAction())
                    return;

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
        finally
        {
            SetJobActionBusy(false);
            _startGate.Release();
        }
    }

    public async Task StartOfferAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        if (_startAction is null)
            return;

        if (!await _startGate
                .WaitAsync(0, cancellationToken)
                .ConfigureAwait(true))
        {
            return;
        }

        SetJobActionBusy(true);

        try
        {
            if (BlockNewFlightAction())
                return;

            string aircraftId =
                _selectedAircraftId
                ?? throw new InvalidOperationException(
                    "Select an installed aircraft before starting a career flight.");
            JobOfferItemViewModel selectedOffer = _offers
                .Single(offer => offer.OfferId == offerId);
            CareerJobActionKind actionKind = selectedOffer.ActionKind;

            // CanStart is a UI projection, not permission to use retained discovery after
            // a disconnect between timer ticks. Recheck before invoking the start authority.
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                if (BlockNewFlightAction())
                    return;

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

            if (actionKind == CareerJobActionKind.AcceptForDispatch)
            {
                SetField(
                    ref _acceptanceStatus,
                    "Accepting job and completing automatic aircraft, fuel and runway checks…",
                    nameof(AcceptanceStatus));

                await _startAction
                    .AcceptAsync(
                        offerId,
                        aircraftId,
                        cancellationToken);

                SetField(
                    ref _acceptanceStatus,
                    "Automatic preflight passed. Starting the persistent FlightSession…",
                    nameof(AcceptanceStatus));

                _ = await _startAction
                    .StartAsync(
                        offerId,
                        aircraftId,
                        cancellationToken);

                await RefreshAsync(cancellationToken);

                SetField(
                    ref _acceptanceStatus,
                    $"Career flight started: {selectedOffer.Offer.OriginIcao} → {selectedOffer.Offer.DestinationIcao}. Opening Current Flight.",
                    nameof(AcceptanceStatus));

                NavigationRequested?.Invoke(
                    this,
                    new JobsNavigationRequestedEventArgs(
                        JobsNavigationTarget.CurrentFlight));
                return;
            }

            SetField(
                ref _acceptanceStatus,
                actionKind == CareerJobActionKind.AcceptAndStartDevelopmentFlight
                    ? "Accepting the development flight, verifying dispatch, and starting its persistent FlightSession…"
                    : "Revalidating the prepared dispatch plan and starting the persistent FlightSession…",
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
                $"Career flight started: {result.StartedFlight.Contract.Contract.OriginIcao} → {result.StartedFlight.Contract.Contract.DestinationIcao}. Opening Current Flight.",
                nameof(AcceptanceStatus));

            NavigationRequested?.Invoke(
                this,
                new JobsNavigationRequestedEventArgs(
                    JobsNavigationTarget.CurrentFlight));
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

            await _refreshGate
                .WaitAsync(cancellationToken)
                .ConfigureAwait(true);

            try
            {
                await RebuildOffersLockedAsync(
                    cancellationToken);

                await RefreshAcceptedJobsAsync(
                    cancellationToken);
            }
            finally
            {
                _refreshGate.Release();
            }

            SetField(
                ref _acceptanceStatus,
                $"Job action failed: {ex.Message}",
                nameof(AcceptanceStatus));
        }
        finally
        {
            SetJobActionBusy(false);
            _startGate.Release();
        }
    }

    public void OpenCurrentFlight()
    {
        if (!HasCurrentFlightSession)
            return;

        NavigationRequested?.Invoke(
            this,
            new JobsNavigationRequestedEventArgs(
                JobsNavigationTarget.CurrentFlight));
    }

    public async Task CancelAcceptedJobsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _startGate
                .WaitAsync(0, cancellationToken)
                .ConfigureAwait(true))
        {
            return;
        }

        SetJobActionBusy(true);

        try
        {
            if (_acceptedCancellation is not { } cancellation
                || _acceptedContractIds.Count == 0)
            {
                SetField(
                    ref _acceptanceStatus,
                    "No accepted preflight job is available to cancel.",
                    nameof(AcceptanceStatus));
                return;
            }

            SetCancellingAcceptedJobs(true);
            Guid[] contractIds = _acceptedContractIds.ToArray();

            foreach (Guid contractId in contractIds)
            {
                await cancellation
                    .CancelAsync(contractId, cancellationToken)
                    .ConfigureAwait(true);
            }

            await RefreshAsync(cancellationToken)
                .ConfigureAwait(true);

            SetField(
                ref _acceptanceStatus,
                contractIds.Length == 1
                    ? "Accepted job cancelled. Its prepared plan was cleared and aircraft reservation released."
                    : $"All {contractIds.Length} accepted jobs were cancelled. Their prepared plans were cleared and aircraft reservations released.",
                nameof(AcceptanceStatus));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(
                ex,
                "Accepted career-job cancellation failed from Jobs.");

            await RefreshAcceptedJobsAsync(CancellationToken.None)
                .ConfigureAwait(true);

            SetField(
                ref _acceptanceStatus,
                $"Accepted job cancellation failed: {ex.Message}",
                nameof(AcceptanceStatus));
        }
        finally
        {
            SetCancellingAcceptedJobs(false);
            SetJobActionBusy(false);
            _startGate.Release();
        }
    }

    private async Task RefreshAcceptedJobsAsync(
        CancellationToken cancellationToken)
    {
        if (_acceptedContext is null)
        {
            SetAcceptedContractIds(Array.Empty<Guid>());
            return;
        }

        AcceptedDispatchPlanningContext context;
        try
        {
            context = await _acceptedContext
                .ReadAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(
                ex,
                "Accepted career-job cancellation state could not be refreshed from Jobs.");
            return;
        }

        IReadOnlyList<Guid> contractIds = context.AcceptedContractIds?
            .Where(static contractId => contractId != Guid.Empty)
            .Distinct()
            .OrderBy(static contractId => contractId)
            .ToArray()
            ?? (context.ContractId is { } contractId
                ? [contractId]
                : Array.Empty<Guid>());

        SetAcceptedContractIds(contractIds);
    }

    private void RefreshCurrentFlightSession()
    {
        CareerJobPlayableReadinessSnapshot? snapshot =
            _playableReadiness?.Current;
        FlightSession? session =
            _flightSessions?.Current;

        bool hasCareerFlight =
            snapshot?.ContractId is not null
            && snapshot.State != CareerJobPlayableReadinessState.NoCareerFlight;
        bool hasCurrentFlightSession =
            hasCareerFlight
            || session is not null;

        if (_hasCurrentFlightSession != hasCurrentFlightSession)
        {
            _hasCurrentFlightSession = hasCurrentFlightSession;
            OnPropertyChanged(nameof(HasCurrentFlightSession));
            OnPropertyChanged(nameof(CanStartJobActions));
        }

        SetField(
            ref _currentFlightSessionStatus,
            hasCareerFlight
                ? snapshot!.StatusText
                : session is not null
                    ? $"FLIGHT SESSION · {session.Status.ToString().ToUpperInvariant()}"
                    : "NO CURRENT FLIGHT SESSION",
            nameof(CurrentFlightSessionStatus));
        SetField(
            ref _currentFlightSessionDetail,
            hasCareerFlight
                ? snapshot!.Detail
                : session is not null
                    ? "A current FlightSession already occupies the playable loop. Open Current Flight to resume it or use its available completion, logging, discard, or abandonment recovery action before accepting another job."
                    : "Accept and start a career job to create a FlightSession.",
            nameof(CurrentFlightSessionDetail));
    }

    private void RefreshJobRecoveryAttention(
        CareerJobPreFlightSessionRecoveryResult? result,
        bool recoveryFailed)
    {
        string? status =
            recoveryFailed
                ? "JOB RECOVERY · RETRY REQUIRED"
                : result?.State switch
                {
                    CareerJobPreFlightSessionRecoveryState.WaitingForReservation =>
                        "JOB RECOVERY · RESERVATION MISSING",
                    CareerJobPreFlightSessionRecoveryState.WaitingForCareer =>
                        "JOB RECOVERY · CAREER STATE REQUIRED",
                    CareerJobPreFlightSessionRecoveryState.WaitingForAircraft =>
                        "JOB RECOVERY · AIRCRAFT UNAVAILABLE",
                    CareerJobPreFlightSessionRecoveryState.WaitingForDispatch
                        or CareerJobPreFlightSessionRecoveryState.WaitingForPreparedDispatch =>
                        "JOB RECOVERY · DISPATCH REQUIRED",
                    CareerJobPreFlightSessionRecoveryState.ContractStartBlocked =>
                        "JOB RECOVERY · START BLOCKED",
                    CareerJobPreFlightSessionRecoveryState.MultipleAcceptedContracts =>
                        "JOB RECOVERY · CHOOSE ACCEPTED JOBS",
                    _ =>
                        null
                };

        string detail =
            recoveryFailed
                ? "Automatic job recovery did not complete. No persisted job was cleared. Refresh Jobs to retry; accepted preflight jobs remain available to cancel below. If a flight was already in progress, do not start another flight and create a diagnostic bundle from Settings."
                : status is not null
                    ? result!.Detail
                    : "No accepted or in-progress job requires recovery attention.";
        bool hasAttention =
            status is not null;

        if (_hasJobRecoveryAttention != hasAttention)
        {
            _hasJobRecoveryAttention =
                hasAttention;
            OnPropertyChanged(nameof(HasJobRecoveryAttention));
            OnPropertyChanged(nameof(CanStartJobActions));
        }

        SetField(
            ref _jobRecoveryStatus,
            status ?? "JOB RECOVERY · READY",
            nameof(JobRecoveryStatus));
        SetField(
            ref _jobRecoveryDetail,
            detail,
            nameof(JobRecoveryDetail));
    }

    private bool BlockNewFlightAction()
    {
        // Re-read the synchronous authority projections so a stale UI invocation cannot
        // race a FlightSession that appeared after the last Jobs refresh.
        RefreshCurrentFlightSession();

        if (HasJobRecoveryAttention)
        {
            SetField(
                ref _acceptanceStatus,
                "Resolve the job recovery attention above before starting or creating another flight. Refresh Jobs to retry recovery; accepted preflight jobs can still be cancelled below.",
                nameof(AcceptanceStatus));
            return true;
        }

        if (!HasCurrentFlightSession)
            return false;

        SetField(
            ref _acceptanceStatus,
            "Open Current Flight and finish, recover, log, discard, or abandon the existing FlightSession before starting or creating another flight. Accepted preflight jobs can still be cancelled below.",
            nameof(AcceptanceStatus));
        return true;
    }

    private void SetAcceptedContractIds(
        IReadOnlyList<Guid> contractIds)
    {
        _acceptedContractIds = contractIds;
        OnPropertyChanged(nameof(CanCancelAcceptedJobs));
        OnPropertyChanged(nameof(CancelAcceptedJobsButtonText));
        OnPropertyChanged(nameof(CancelAcceptedJobsDialogText));
    }

    private void SetCancellingAcceptedJobs(bool value)
    {
        if (_isCancellingAcceptedJobs == value)
            return;

        _isCancellingAcceptedJobs = value;
        OnPropertyChanged(nameof(CanCancelAcceptedJobs));
        OnPropertyChanged(nameof(CancelAcceptedJobsButtonText));
    }

    private void SetJobActionBusy(bool value)
    {
        if (_isJobActionBusy == value)
            return;

        _isJobActionBusy = value;
        OnPropertyChanged(nameof(IsJobActionBusy));
        OnPropertyChanged(nameof(CanRunJobActions));
        OnPropertyChanged(nameof(CanStartJobActions));
        OnPropertyChanged(nameof(CanCancelAcceptedJobs));
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

        bool availabilityChanged = _aircraftDiscoveryAvailable != snapshot.IsAvailable;
        bool optionsChanged = snapshot.IsAvailable
            && !_aircraftOptions.SequenceEqual(snapshot.Aircraft);
        bool discoveryChanged = availabilityChanged || optionsChanged;
        _aircraftDiscoveryAvailable = snapshot.IsAvailable;
        // Unavailable is uncertainty, not removal. Retain the visual choice, but never
        // evaluate/start with it until authoritative current discovery is available again.
        if (optionsChanged)
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
            item.StartInputState,
            item.ActionKind)).ToArray());
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
            CareerJobActionKind actionKind =
                CareerJobActionKind.StartPreparedFlight;

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
                                ? "Job acceptance and start actions are not connected to the playable loop."
                                : "Checking authoritative dispatch readiness…";

            if (active
                && _aircraftDiscoveryAvailable
                && _selectedAircraftId is not null
                && _startAction is not null)
            {
                try
                {
                    CareerJobStartActionAvailability availability =
                        await Task.Run(
                                () => _startAction.ReadAvailabilityAsync(
                                offer.OfferId,
                                _selectedAircraftId,
                                cancellationToken),
                                cancellationToken)
                            .ConfigureAwait(true);

                    canStart =
                        availability.CanStart;
                    startState =
                        availability.State;
                    actionKind =
                        availability.ActionKind;
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
                    startState,
                    actionKind));
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
                    ? "Selected aircraft has at least one verified actionable career offer."
                    : firstBlocked is not null
                        ? $"Blocked: {firstBlocked.ActionText}"
                        : "No active offer is currently actionable with the selected aircraft.",
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

    private void SetRefreshing(bool value)
    {
        if (_isRefreshing == value)
            return;

        _isRefreshing = value;
        OnPropertyChanged(nameof(IsRefreshing));
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
        CareerJobStartInputState? startInputState = null,
        CareerJobActionKind actionKind = CareerJobActionKind.StartPreparedFlight)
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

        ActionKind =
            actionKind;

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
    public CareerJobActionKind ActionKind { get; }
    public string ActionButtonText => ActionKind switch
    {
        CareerJobActionKind.AcceptForDispatch => "Accept & Start Flight",
        CareerJobActionKind.AcceptAndStartDevelopmentFlight => "Start $0 Calibration Flight",
        _ => "Start Prepared Flight"
    };
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

    public string AssignmentText =>
        CreateAssignmentText();

    public string CompensationText
    {
        get
        {
            if (DevelopmentFlight.IsDevelopment(Offer))
                return "$0 CALIBRATION PAY";

            ContractPayQuote? quote =
                CreatePayQuote();

            if (quote is null)
                return "PAY PENDING CONTRACT TERMS";

            ContractCompensation compensation =
                quote.Compensation;

            return compensation.Model switch
            {
                CompensationModel.CompanyRevenue =>
                    $"COMPANY REVENUE {FormatCurrency(compensation.GrossCustomerRevenue)}",
                CompensationModel.SalaryDuty =>
                    $"DUTY PAY {FormatCurrency(compensation.PilotCompensation)}",
                CompensationModel.MissionFee =>
                    $"MISSION PAY {FormatCurrency(compensation.PilotCompensation)}",
                CompensationModel.Reimbursement =>
                    $"REIMBURSEMENT {FormatCurrency(compensation.PilotCompensation)}",
                _ =>
                    $"PILOT PAY {FormatCurrency(compensation.PilotCompensation)}"
            };
        }
    }

    public string ProgressionText =>
        DevelopmentFlight.IsDevelopment(Offer)
            ? "$0 PROGRESSION"
            : Offer.ContractTerms is { } terms
                ? $"+{terms.ReputationReward:0.#} reputation on success"
                : "Progression pending contract terms";

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

    private ContractPayQuote? CreatePayQuote()
    {
        JobMarketContractTermsEnvelope? terms =
            Offer.ContractTerms;

        if (terms is null)
            return null;

        return ContractPayQuoteEngine.Quote(
            new ContractPayQuoteRequest(
                Offer.ServiceTrack,
                Offer.Kind,
                terms.EstimatedFlightHours,
                Offer.DistanceNm,
                terms.PayloadPounds,
                terms.DemandAttractiveness,
                terms.Urgency,
                terms.Difficulty,
                Offer.RelationshipStrength,
                terms.EstimatedPlayerOperatingCosts));
    }

    private string CreateAssignmentText()
    {
        if (DevelopmentFlight.IsDevelopment(Offer))
            return "ASSIGNMENT · Calibration circuit · no revenue payload";

        JobMarketContractTermsEnvelope? terms =
            Offer.ContractTerms;

        if (terms is null)
            return "ASSIGNMENT · Details unavailable";

        if (Offer.Kind == ContractKind.Passenger)
        {
            int passengerCount =
                terms.AircraftRequirements.MinimumPassengerCapacity;

            return passengerCount > 0
                ? $"MANIFEST · {passengerCount} {(passengerCount == 1 ? "passenger" : "passengers")} · {FormatWeight(terms.PayloadPounds)} incl. baggage"
                : terms.PayloadPounds > 0
                    ? $"PASSENGER LOAD · {FormatWeight(terms.PayloadPounds)} · count not specified"
                    : "PASSENGER LOAD · Manifest details unavailable";
        }

        if (Offer.Kind is ContractKind.Cargo
            or ContractKind.ExpressCargo
            or ContractKind.AogPartsDelivery)
        {
            var cargoLots =
                terms.EffectiveCargoLots;

            if (!cargoLots.IsEmpty)
            {
                string commodityNames =
                    string.Join(
                        ", ",
                        cargoLots
                            .Select(static lot => lot.CommodityName)
                            .Distinct(StringComparer.Ordinal));

                return $"CARGO · {commodityNames} · {FormatWeight(cargoLots.Sum(static lot => lot.MassPounds))}";
            }

            return terms.PayloadPounds > 0
                ? $"CARGO · {FormatWeight(terms.PayloadPounds)} · commodity not specified"
                : "CARGO · Manifest details unavailable";
        }

        return terms.PayloadPounds > 0
            ? $"PAYLOAD · {FormatWeight(terms.PayloadPounds)}"
            : "ASSIGNMENT · No assigned payload";
    }

    private static string FormatCurrency(decimal value) =>
        $"${value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}";

    private static string FormatWeight(double pounds) =>
        $"{pounds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} lb";

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
