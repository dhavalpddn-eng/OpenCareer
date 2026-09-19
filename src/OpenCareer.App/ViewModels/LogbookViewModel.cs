using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.App.ViewModels;

public sealed class LogbookViewModel : INotifyPropertyChanged
{
    private readonly ILogbookSource _source;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private IReadOnlyList<LogbookEntryItemViewModel> _entries =
        Array.Empty<LogbookEntryItemViewModel>();
    private LogbookEntryItemViewModel? _selectedEntry;
    private LogbookStatistics _statistics = LogbookStatistics.Empty;
    private string _searchText = string.Empty;
    private LogbookEntryKind? _entryKindFilter;
    private FlightSafetyOutcome? _safetyOutcomeFilter;

    public LogbookViewModel(ILogbookSource source)
    {
        _source = source;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LogbookEntryItemViewModel> Entries => _entries;
    public LogbookEntryItemViewModel? SelectedEntry => _selectedEntry;
    public IReadOnlyList<LogbookLandingItemViewModel> SelectedLandings =>
        _selectedEntry?.Landings ?? Array.Empty<LogbookLandingItemViewModel>();
    public IReadOnlyList<LogbookEventItemViewModel> SelectedEvents =>
        _selectedEntry?.Events ?? Array.Empty<LogbookEventItemViewModel>();
    public IReadOnlyList<LogbookLegItemViewModel> SelectedLegs =>
        _selectedEntry?.Legs ?? Array.Empty<LogbookLegItemViewModel>();

    public string TotalFlightTimeText =>
        FormatDuration(_statistics.MovementFlightTime);

    public string CareerCreditText =>
        FormatDuration(_statistics.CareerCreditTime);

    public string ExperienceDimensionsText =>
        $"Night {FormatDuration(_statistics.NightCareerCreditTime)} • " +
        $"Actual instrument {FormatDuration(_statistics.ActualInstrumentCareerCreditTime)}";

    public string OperationsCountText =>
        $"{_statistics.TakeoffCount} takeoffs • {_statistics.LandingEpisodeCount} landings";

    public string LandingTypeCountText =>
        $"{_statistics.FullStopLandingCount} full-stop • " +
        $"{_statistics.TouchAndGoCount} touch-and-go • " +
        $"{_statistics.StopAndGoCount} stop-and-go";

    public string FilterSummaryText
    {
        get
        {
            var filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(_searchText))
                filters.Add($"Search: {_searchText.Trim()}");
            if (_entryKindFilter is { } kind)
                filters.Add($"Type: {Friendly(kind)}");
            if (_safetyOutcomeFilter is { } safety)
                filters.Add($"Outcome: {Friendly(safety)}");

            return filters.Count == 0
                ? "Showing all committed flights."
                : string.Join(" • ", filters);
        }
    }

    public string StatusText =>
        _entries.Count == 0
            ? "No committed flights yet. Career flights appear after authoritative settlement; free/practice flights appear only when you choose Log Flight."
            : $"{_entries.Count} committed flight{(_entries.Count == 1 ? string.Empty : "s")}.";

    public string SelectedTitle =>
        _selectedEntry?.Route ?? "No flight selected";

    public string SelectedSubtitle =>
        _selectedEntry is null
            ? "Select a committed flight to inspect its frozen postflight debrief."
            : $"{_selectedEntry.Date} • {_selectedEntry.Aircraft}";

    public string SelectedTimeSummary =>
        _selectedEntry?.TimeSummary ?? "Flight time —";

    public string SelectedExperienceSummary =>
        _selectedEntry?.ExperienceSummary ?? "Career credit —";

    public string SelectedOutcomeSummary =>
        _selectedEntry?.OutcomeSummary ?? "Outcome —";

    public string SelectedSettlementSummary =>
        _selectedEntry?.SettlementSummary ?? "Settlement —";

    public string SelectedEventSummary =>
        _selectedEntry?.EventSummary ?? "Events —";

    public string SelectedLandingSummary =>
        _selectedEntry?.LandingSummary ?? "Landings —";

    public string SelectedFuelSummary =>
        _selectedEntry?.FuelSummary ?? "Fuel —";

    public string SelectedPayloadSummary =>
        _selectedEntry?.PayloadSummary ?? "Payload —";

    public string SelectedAssistanceSummary =>
        _selectedEntry?.AssistanceSummary ?? "Assistance —";

    public string SelectedLegSummary =>
        _selectedEntry?.LegSummary ?? "Legs —";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            IReadOnlyList<LogbookEntry> entries =
                await _source.QueryAsync(
                    new LogbookQuery(
                        SearchText: _searchText,
                        EntryKind: _entryKindFilter,
                        SafetyOutcome: _safetyOutcomeFilter,
                        Limit: 200),
                    cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            _statistics = LogbookStatisticsCalculator.Calculate(entries);

            _entries = entries
                .OrderByDescending(static entry => entry.Debrief.EndedAt)
                .ThenBy(static entry => entry.EntryId)
                .Select(static entry => new LogbookEntryItemViewModel(entry))
                .ToArray();

            if (_selectedEntry is not null)
            {
                _selectedEntry = _entries.FirstOrDefault(
                    item => item.EntryId == _selectedEntry.EntryId);
            }

            _selectedEntry ??= _entries.FirstOrDefault();
            RaiseAll();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void SetFilters(
        string? searchText,
        LogbookEntryKind? entryKind,
        FlightSafetyOutcome? safetyOutcome)
    {
        _searchText = searchText ?? string.Empty;
        _entryKindFilter = entryKind;
        _safetyOutcomeFilter = safetyOutcome;
        OnPropertyChanged(nameof(FilterSummaryText));
    }

    public void SelectEntry(LogbookEntryItemViewModel? entry)
    {
        if (ReferenceEquals(_selectedEntry, entry))
            return;

        _selectedEntry = entry;
        RaiseSelection();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FilterSummaryText));
        OnPropertyChanged(nameof(TotalFlightTimeText));
        OnPropertyChanged(nameof(CareerCreditText));
        OnPropertyChanged(nameof(ExperienceDimensionsText));
        OnPropertyChanged(nameof(OperationsCountText));
        OnPropertyChanged(nameof(LandingTypeCountText));
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedSubtitle));
        OnPropertyChanged(nameof(SelectedTimeSummary));
        OnPropertyChanged(nameof(SelectedExperienceSummary));
        OnPropertyChanged(nameof(SelectedOutcomeSummary));
        OnPropertyChanged(nameof(SelectedSettlementSummary));
        OnPropertyChanged(nameof(SelectedEventSummary));
        OnPropertyChanged(nameof(SelectedLandingSummary));
        OnPropertyChanged(nameof(SelectedLandings));
        OnPropertyChanged(nameof(SelectedEvents));
        OnPropertyChanged(nameof(SelectedLegs));
        OnPropertyChanged(nameof(SelectedFuelSummary));
        OnPropertyChanged(nameof(SelectedPayloadSummary));
        OnPropertyChanged(nameof(SelectedAssistanceSummary));
        OnPropertyChanged(nameof(SelectedLegSummary));
    }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}"
            : $"{value.Minutes}:{value.Seconds:00}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class LogbookEntryItemViewModel
{
    private readonly LogbookEntry _entry;

    public LogbookEntryItemViewModel(LogbookEntry entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));

        Landings = entry.Debrief.Landings
            .Select(static landing => new LogbookLandingItemViewModel(landing))
            .ToArray();

        Events = entry.Debrief.Events
            .Select(static item => new LogbookEventItemViewModel(item))
            .ToArray();

        Legs = entry.Debrief.Legs
            .Select(static leg => new LogbookLegItemViewModel(leg))
            .ToArray();

        RouteTracks = RouteTrackProjection.Project(entry.Debrief.Legs);
    }

    public IReadOnlyList<LogbookLandingItemViewModel> Landings { get; }
    public IReadOnlyList<LogbookEventItemViewModel> Events { get; }
    public IReadOnlyList<LogbookLegItemViewModel> Legs { get; }
    public IReadOnlyList<ProjectedRouteLeg> RouteTracks { get; }

    public Guid EntryId => _entry.EntryId;

    public string Date =>
        _entry.Debrief.EndedAt.ToLocalTime().ToString("g");

    public string Route
    {
        get
        {
            string origin =
                _entry.Debrief.Route.ActualDeparture ??
                _entry.Debrief.Route.PlannedOrigin ??
                "Unknown";

            string destination =
                _entry.Debrief.Route.ActualArrival ??
                _entry.Debrief.Route.PlannedDestination ??
                "Unknown";

            return $"{origin} → {destination}";
        }
    }

    public string Aircraft => _entry.Debrief.Aircraft.DisplayName;

    public string FlightTime =>
        FormatDuration(_entry.Debrief.Time.MovementFlightTime);

    public string TimeSummary =>
        $"Flight {FormatDuration(_entry.Debrief.Time.MovementFlightTime)} • " +
        $"Airborne {FormatDuration(_entry.Debrief.Time.AirborneTime)} • " +
        $"Taxi out {FormatDuration(_entry.Debrief.Time.TaxiOutTime)} • " +
        $"Taxi in {FormatDuration(_entry.Debrief.Time.TaxiInTime)}";

    public string ExperienceSummary =>
        $"Career credit {FormatDuration(_entry.Debrief.Time.CareerCreditTime)} • " +
        $"Night {FormatDuration(_entry.Debrief.Time.NightCareerCreditTime)} • " +
        $"Actual instrument {FormatDuration(_entry.Debrief.Time.ActualInstrumentCareerCreditTime)}";

    public string OutcomeSummary =>
        $"Flight: {Friendly(_entry.Debrief.SafetyOutcome)} • " +
        $"Mission: {Friendly(_entry.Debrief.MissionOutcome)}";

    public string SettlementSummary =>
        _entry.Debrief.Settlement.Status switch
        {
            SettlementRecordStatus.Settled =>
                $"{FormatSignedCurrency(_entry.Debrief.Settlement.CashDelta!.Value)} • " +
                $"{_entry.Debrief.Settlement.ReputationDelta!.Value:+0.#;-0.#;0} reputation",
            SettlementRecordStatus.Pending => "Settlement pending",
            _ => "No career settlement"
        };

    public string EventSummary =>
        _entry.Debrief.Events.Count == 0
            ? "No recorded incidents or notable events"
            : $"{_entry.Debrief.Events.Count} recorded event{(_entry.Debrief.Events.Count == 1 ? string.Empty : "s")}";

    public string FuelSummary
    {
        get
        {
            FlightFuelDebrief fuel = _entry.Debrief.Fuel;
            if (fuel.StartFuelPounds is null &&
                fuel.EndFuelPounds is null &&
                fuel.FuelUsedPounds is null)
            {
                return $"Fuel unavailable • {Friendly(fuel.EvidenceQuality)}";
            }

            var values = new List<string>();
            if (fuel.StartFuelPounds is { } start)
                values.Add($"Start {start:0} lb");
            if (fuel.EndFuelPounds is { } end)
                values.Add($"End {end:0} lb");
            if (fuel.FuelUsedPounds is { } used)
                values.Add($"Used {used:0} lb");

            values.Add(Friendly(fuel.EvidenceQuality));
            return string.Join(" • ", values);
        }
    }

    public string PayloadSummary
    {
        get
        {
            PayloadDebrief payload = _entry.Debrief.Payload;
            var values = new List<string>();

            if (payload.PassengerCount is { } passengers)
                values.Add($"{passengers} passenger{(passengers == 1 ? string.Empty : "s")}");
            if (payload.CargoMassPounds is { } mass)
                values.Add($"{mass:0} lb cargo");
            if (!string.IsNullOrWhiteSpace(payload.CargoDescription))
                values.Add(payload.CargoDescription);
            if (!string.IsNullOrWhiteSpace(payload.Outcome))
                values.Add(payload.Outcome);

            return values.Count == 0
                ? $"Payload unavailable • {Friendly(payload.EvidenceQuality)}"
                : string.Join(" • ", values);
        }
    }

    public string AssistanceSummary
    {
        get
        {
            FlightAssistanceDebrief assistance = _entry.Debrief.Assistance;
            var flags = new List<string>();

            if (assistance.PauseObserved)
                flags.Add("pause");
            if (assistance.TimeAccelerationObserved)
                flags.Add("time acceleration");
            if (assistance.SlewObserved)
                flags.Add("slew");
            if (assistance.PositionJumpObserved)
                flags.Add("position change");
            if (assistance.RouteEvidenceCompromised)
                flags.Add("route evidence compromised");

            return flags.Count == 0
                ? "No assistance flags"
                : string.Join(" • ", flags);
        }
    }

    public string LegSummary =>
        $"{Legs.Count} flight leg{(Legs.Count == 1 ? string.Empty : "s")}";

    public string LandingSummary =>
        $"{_entry.Debrief.Tracking.LandingEpisodeCount} landing episode{(_entry.Debrief.Tracking.LandingEpisodeCount == 1 ? string.Empty : "s")} • " +
        $"{_entry.Debrief.Tracking.BounceCount} bounce{(_entry.Debrief.Tracking.BounceCount == 1 ? string.Empty : "s")} • " +
        $"{_entry.Debrief.Tracking.TouchAndGoCount} touch-and-go";

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}"
            : $"{value.Minutes}:{value.Seconds:00}";

    private static string FormatSignedCurrency(decimal value) =>
        value switch
        {
            > 0 => $"+{value:C0}",
            < 0 => $"-{Math.Abs(value):C0}",
            _ => "$0"
        };

    private static string Friendly<T>(T value)
        where T : struct, Enum =>
        string.Concat(
            value.ToString().Select((character, index) =>
                index > 0 && char.IsUpper(character)
                    ? $" {character}"
                    : character.ToString()));
}


public sealed class LogbookLandingItemViewModel
{
    public LogbookLandingItemViewModel(LandingDebrief landing)
    {
        ArgumentNullException.ThrowIfNull(landing);

        Title = $"Landing {landing.EpisodeNumber} • {Friendly(landing.OperationType)}";
        When = landing.Timestamp.ToLocalTime().ToString("g");
        Evidence = Friendly(landing.EvidenceQuality);
        Bounces = $"{landing.BounceCount} bounce{(landing.BounceCount == 1 ? string.Empty : "s")}";

        var metrics = new List<string>();
        if (landing.VerticalSpeedFeetPerMinute is { } verticalSpeed)
            metrics.Add($"{verticalSpeed:0} fpm");
        if (landing.TouchdownG is { } touchdownG)
            metrics.Add($"{touchdownG:0.00} G");
        if (landing.IndicatedAirspeedKnots is { } airspeed)
            metrics.Add($"{airspeed:0} kt IAS");
        if (landing.PitchDegrees is { } pitch)
            metrics.Add($"{pitch:0.0}° pitch");
        if (landing.BankDegrees is { } bank)
            metrics.Add($"{bank:0.0}° bank");

        Metrics = metrics.Count == 0
            ? "Touchdown metrics unavailable"
            : string.Join(" • ", metrics);

        HardLanding = landing.HardLanding switch
        {
            true => "Hard landing",
            false => "No hard-landing classification",
            null => "Hard-landing classification unavailable"
        };
    }

    public string Title { get; }
    public string When { get; }
    public string Metrics { get; }
    public string Bounces { get; }
    public string HardLanding { get; }
    public string Evidence { get; }

    private static string Friendly<T>(T value)
        where T : struct, Enum =>
        string.Concat(
            value.ToString().Select((character, index) =>
                index > 0 && char.IsUpper(character)
                    ? $" {character}"
                    : character.ToString()));
}

public sealed class LogbookEventItemViewModel
{
    public LogbookEventItemViewModel(FlightDebriefEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);

        When = item.Timestamp.ToLocalTime().ToString("g");
        Category = item.Category.ToUpperInvariant();
        Severity = item.Severity.ToString().ToUpperInvariant();
        Text = item.Text;
        Evidence = string.Concat(
            item.EvidenceQuality.ToString().Select((character, index) =>
                index > 0 && char.IsUpper(character)
                    ? $" {character}"
                    : character.ToString()));
    }

    public string When { get; }
    public string Category { get; }
    public string Severity { get; }
    public string Text { get; }
    public string Evidence { get; }
}


public sealed class LogbookLegItemViewModel
{
    public LogbookLegItemViewModel(FlightLegDebrief leg)
    {
        ArgumentNullException.ThrowIfNull(leg);

        Sequence = $"LEG {leg.Sequence}";
        string origin =
            leg.Route.ActualDeparture ??
            leg.Route.PlannedOrigin ??
            "Unknown";
        string destination =
            leg.Route.ActualArrival ??
            leg.Route.PlannedDestination ??
            "Unknown";

        Route = $"{origin} → {destination}";
        Time =
            $"Flight {FormatDuration(leg.Time.MovementFlightTime)} • " +
            $"Airborne {FormatDuration(leg.Time.AirborneTime)}";
        Track =
            leg.RouteTrack.Count == 0
                ? "Route track unavailable"
                : $"{leg.RouteTrack.Count} decimated route points";
        LandingReferences =
            leg.LandingEpisodeNumbers.Count == 0
                ? "No landing episodes"
                : $"Landing episodes {string.Join(", ", leg.LandingEpisodeNumbers)}";
    }

    public string Sequence { get; }
    public string Route { get; }
    public string Time { get; }
    public string Track { get; }
    public string LandingReferences { get; }

    private static string FormatDuration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
}
