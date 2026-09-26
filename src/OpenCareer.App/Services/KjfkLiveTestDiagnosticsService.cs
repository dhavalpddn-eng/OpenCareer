using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCareer.App.ViewModels;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.SimConnect;

namespace OpenCareer.App.Services;

public sealed class KjfkLiveTestDiagnosticsService : IAsyncDisposable
{
    private const int MaximumTrackedContractCount = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FinalCaptureTimeout = TimeSpan.FromSeconds(2);

    private readonly KjfkLiveTestDiagnosticJournal _journal;
    private readonly OpenCareerDataPaths _paths;
    private readonly ISimulatorConnection _connection;
    private readonly SimConnectInstalledAircraftObservationSource _simConnectAircraft;
    private readonly IInstalledAircraftDiscoverySource _aircraftDiscovery;
    private readonly JobsViewModel _jobs;
    private readonly FlightSessionCoordinator _flightSessions;
    private readonly FlightSessionPersistenceService _flightPersistence;
    private readonly JobContractRuntimeState _contracts;
    private readonly IAircraftReservationLookup _reservations;
    private readonly ILogger<KjfkLiveTestDiagnosticsService> _logger;
    private readonly SemaphoreSlim _captureGate = new(1, 1);

    private CancellationTokenSource? _stop;
    private Task _worker = Task.CompletedTask;
    private string? _latestSessionContractId;
    private long _logOffset;
    private bool _started;
    private bool _completed;

    public KjfkLiveTestDiagnosticsService(
        KjfkLiveTestDiagnosticJournal journal,
        OpenCareerDataPaths paths,
        ISimulatorConnection connection,
        SimConnectInstalledAircraftObservationSource simConnectAircraft,
        IInstalledAircraftDiscoverySource aircraftDiscovery,
        JobsViewModel jobs,
        FlightSessionCoordinator flightSessions,
        FlightSessionPersistenceService flightPersistence,
        JobContractRuntimeState contracts,
        IAircraftReservationLookup reservations,
        ILogger<KjfkLiveTestDiagnosticsService> logger)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _simConnectAircraft = simConnectAircraft ?? throw new ArgumentNullException(nameof(simConnectAircraft));
        _aircraftDiscovery = aircraftDiscovery ?? throw new ArgumentNullException(nameof(aircraftDiscovery));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _flightSessions = flightSessions ?? throw new ArgumentNullException(nameof(flightSessions));
        _flightPersistence = flightPersistence ?? throw new ArgumentNullException(nameof(flightPersistence));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Start()
    {
        if (_started || !_journal.Start())
            return _started;

        try
        {
            _logOffset = File.Exists(_paths.LogFile)
                ? new FileInfo(_paths.LogFile).Length
                : 0;

            _jobs.PropertyChanged += OnJobsPropertyChanged;
            _flightSessions.SessionChanged += OnFlightSessionChanged;

            CaptureConnection();
            CaptureAircraftDiscovery();
            CaptureJobs();
            CaptureFlightSession(_flightSessions.Current);
            CaptureRecovery();

            _stop = new CancellationTokenSource();
            _worker = MonitorAsync(_stop.Token);
            _started = true;
            return true;
        }
        catch (Exception ex)
        {
            _jobs.PropertyChanged -= OnJobsPropertyChanged;
            _flightSessions.SessionChanged -= OnFlightSessionChanged;
            _stop?.Cancel();
            _stop?.Dispose();
            _stop = null;
            _journal.RecordIssue(
                "diagnostic-observer",
                $"start:{ex.GetType().FullName}:{ex.Message}",
                $"Diagnostic observer start issue: {ex.GetType().Name}: {ex.Message}");
            _journal.Complete("DiagnosticStartFailed", ex.Message);
            return false;
        }
    }

    public void RecordUnhandledException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _journal.RecordIssue(
            "unhandled-ui-exception",
            $"{exception.GetType().FullName}:{exception.Message}",
            $"{exception.GetType().Name}: {exception.Message}");
    }

    public async Task CompleteAsync(
        string outcome,
        string? failure = null)
    {
        if (!_started || _completed)
            return;

        _jobs.PropertyChanged -= OnJobsPropertyChanged;
        _flightSessions.SessionChanged -= OnFlightSessionChanged;

        _stop?.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _journal.RecordIssue(
                "diagnostic-observer",
                $"shutdown:{ex.GetType().FullName}:{ex.Message}",
                $"Diagnostic observer shutdown issue: {ex.GetType().Name}: {ex.Message}");
        }

        await _captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                using var finalCapture =
                    new CancellationTokenSource(FinalCaptureTimeout);
                CaptureConnection();
                CaptureAircraftDiscovery();
                CaptureJobs();
                CaptureFlightSession(_flightSessions.Current);
                CaptureRecovery();
                await CaptureContractsAndReservationsAsync(finalCapture.Token)
                    .ConfigureAwait(false);
                CaptureNewFacilityIssues();
            }
            catch (Exception ex)
            {
                _journal.RecordIssue(
                    "diagnostic-observer",
                    $"final:{ex.GetType().FullName}:{ex.Message}",
                    $"Final diagnostic observation issue: {ex.GetType().Name}: {ex.Message}");
            }

            _journal.Complete(outcome, failure);
            _completed = true;
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CompleteAsync("ServiceDisposed").ConfigureAwait(false);
        _stop?.Dispose();
        _captureGate.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            await CaptureCycleAsync(cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task CaptureCycleAsync(CancellationToken cancellationToken)
    {
        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CaptureConnection();
            CaptureAircraftDiscovery();
            CaptureJobs();
            CaptureFlightSession(_flightSessions.Current);
            CaptureRecovery();
            await CaptureContractsAndReservationsAsync(cancellationToken)
                .ConfigureAwait(false);
            CaptureNewFacilityIssues();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _journal.RecordIssue(
                "diagnostic-observer",
                $"{ex.GetType().FullName}:{ex.Message}",
                $"Diagnostic observer issue: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(
                ex,
                "KJFK live-test diagnostic observation failed; gameplay state was not affected.");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private void OnJobsPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        CaptureJobs();

    private void OnFlightSessionChanged(object? sender, FlightSessionChangedEventArgs e) =>
        CaptureFlightSession(e.Session);

    private void CaptureConnection()
    {
        SimulatorConnectionSnapshot current = _connection.Current;
        var projection = new
        {
            state = current.State.ToString(),
            issue = current.Issue.ToString(),
            simulator = current.Simulator is null
                ? null
                : new
                {
                    current.Simulator.Name,
                    applicationVersion = current.Simulator.ApplicationVersion.ToString(),
                    simConnectVersion = current.Simulator.SimConnectVersion.ToString()
                }
        };

        RecordProjection("simconnect", projection);
    }

    private void CaptureAircraftDiscovery()
    {
        InstalledAircraftDiscoverySnapshot current = _aircraftDiscovery.Current;
        var observations = current.Observations
            .OrderBy(static item => item.CanonicalAircraftId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ProviderId, StringComparer.Ordinal)
            .ThenBy(static item => item.ProviderRecordId, StringComparer.Ordinal)
            .Take(50)
            .Select(static item => new
            {
                canonicalAircraftId = item.CanonicalAircraftId,
                providerId = item.ProviderId,
                rawIdentity = item.ProviderRecordId,
                displayName = item.DisplayName
            })
            .ToArray();

        var projection = new
        {
            rawCurrentTitle = _simConnectAircraft.CurrentAircraftTitle,
            availability = current.Availability.ToString(),
            observationCount = current.Observations.Count,
            observationsTruncated = current.Observations.Count > observations.Length,
            observations
        };

        RecordProjection("aircraft-discovery", projection);
    }

    private void CaptureJobs()
    {
        var options = _jobs.AircraftOptions
            .OrderBy(static item => item.AircraftId, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .Select(static item => new
            {
                aircraftId = item.AircraftId,
                displayName = item.DisplayName
            })
            .ToArray();

        var offers = _jobs.Offers
            .OrderBy(static item => item.OfferId)
            .Take(20)
            .Select(static item => new
            {
                offerId = item.OfferId,
                active = item.IsActive,
                canStart = item.CanStart,
                startInputState = item.StartInputState?.ToString(),
                action = Truncate(item.ActionText, 400)
            })
            .ToArray();

        var projection = new
        {
            selectedAircraftId = _jobs.SelectedAircraftId,
            aircraftStatus = Truncate(_jobs.AircraftStatus, 400),
            readinessStatus = Truncate(_jobs.AcceptanceStatus, 500),
            optionCount = _jobs.AircraftOptions.Count,
            optionsTruncated = _jobs.AircraftOptions.Count > options.Length,
            options,
            offerCount = _jobs.Offers.Count,
            offersTruncated = _jobs.Offers.Count > offers.Length,
            offers
        };

        RecordProjection("jobs-readiness", projection);
    }

    private void CaptureFlightSession(FlightSession? session)
    {
        if (session is null)
        {
            RecordProjection("flight-session", new { present = false });
            return;
        }

        var projection = new
        {
            present = true,
            sessionId = session.SessionId,
            contractId = session.ContractId,
            status = session.Status.ToString(),
            operationState = session.OperationState.ToString(),
            trackingState = session.Tracking.State.ToString(),
            session.Tracking.TakeoffCount,
            session.Tracking.LandingEpisodeCount,
            session.Tracking.BounceCount,
            session.Tracking.TouchAndGoCount,
            session.Tracking.RejectedTakeoffCount,
            session.Tracking.CrashReported,
            milestones = new
            {
                aircraftReady = session.Milestones.AircraftReadyAt is not null,
                engineStart = session.Milestones.EngineStartAt is not null,
                taxiOut = session.Milestones.TaxiOutAt is not null,
                takeoff = session.Milestones.TakeoffAt is not null,
                firstTouchdown = session.Milestones.FirstTouchdownAt is not null,
                landing = session.Milestones.LandingAt is not null,
                taxiIn = session.Milestones.TaxiInAt is not null,
                parked = session.Milestones.ParkedAt is not null,
                shutdown = session.Milestones.ShutdownAt is not null,
                completed = session.Milestones.CompletedAt is not null,
                interrupted = session.Milestones.InterruptedAt is not null
            }
        };

        RecordProjection("flight-session", projection);

        if (session.ContractId is Guid contractId && contractId != Guid.Empty)
        {
            Volatile.Write(
                ref _latestSessionContractId,
                contractId.ToString("D"));
        }
    }

    private void CaptureRecovery()
    {
        var projection = new
        {
            attempted = _flightPersistence.RecoveryAttempted,
            recoveredSessionId = _flightPersistence.LastRecoveredSessionId
        };

        RecordProjection("flight-session-recovery", projection);
    }

    private async Task CaptureContractsAndReservationsAsync(
        CancellationToken cancellationToken)
    {
        PersistedJobContract[] allCurrent = _contracts.Current
            .OrderBy(static item => item.Contract.ContractId)
            .ToArray();

        PersistedJobContract[] current = allCurrent
            .OrderBy(static item => item.Contract.Status is ContractStatus.Accepted or ContractStatus.InProgress ? 0 : 1)
            .ThenByDescending(static item => item.Version)
            .ThenBy(static item => item.Contract.ContractId)
            .Take(MaximumTrackedContractCount)
            .ToArray();

        var observed = current
            .Select(static item => item.Contract.ContractId)
            .ToList();
        string? latestSessionContractId =
            Volatile.Read(ref _latestSessionContractId);
        if (Guid.TryParse(latestSessionContractId, out Guid latestContractId)
            && !observed.Contains(latestContractId))
        {
            if (observed.Count >= MaximumTrackedContractCount)
                observed.RemoveAt(observed.Count - 1);
            observed.Add(latestContractId);
        }

        Guid[] observedContractIds = observed
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();

        var reservationStates = new List<object>(observedContractIds.Length);
        foreach (Guid contractId in observedContractIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string reservationId = JobAcceptanceFleetBridge.GetReservationId(contractId);
            AircraftReservationOwnership? ownership = await _reservations
                .FindByReservationIdAsync(reservationId, cancellationToken)
                .ConfigureAwait(false);

            reservationStates.Add(new
            {
                contractId,
                reservationId,
                held = ownership is not null,
                canonicalAircraftId = ownership?.CanonicalAircraftId
            });
        }

        var projection = new
        {
            runtimeInitialized = _contracts.IsInitialized,
            contractCount = allCurrent.Length,
            contractsTruncated = allCurrent.Length > current.Length,
            contracts = current.Select(static item => new
            {
                contractId = item.Contract.ContractId,
                status = item.Contract.Status.ToString(),
                item.Version,
                item.Contract.OriginIcao,
                item.Contract.DestinationIcao
            }).ToArray(),
            reservations = reservationStates
        };

        RecordProjection("contracts-reservations", projection);
    }

    private void CaptureNewFacilityIssues()
    {
        if (!File.Exists(_paths.LogFile))
            return;

        var info = new FileInfo(_paths.LogFile);
        if (info.Length < _logOffset)
            _logOffset = 0;

        using var stream = new FileStream(
            _paths.LogFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(_logOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Contains("facility", StringComparison.OrdinalIgnoreCase)
                && line.Contains("SimConnect", StringComparison.OrdinalIgnoreCase))
            {
                string detail = Truncate(line, 600);
                _journal.RecordIssue(
                    "airport-facility",
                    detail,
                    detail);
            }
        }

        _logOffset = stream.Position;
    }

    private void RecordProjection(string category, object projection)
    {
        string signature = JsonSerializer.Serialize(projection);
        _journal.Record(category, signature, projection);
    }

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maximumLength
            ? value
            : value[..maximumLength];
    }
}
