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

    public LogbookViewModel(ILogbookSource source)
    {
        _source = source;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LogbookEntryItemViewModel> Entries => _entries;
    public LogbookEntryItemViewModel? SelectedEntry => _selectedEntry;

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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(true))
            return;

        try
        {
            IReadOnlyList<LogbookEntry> entries =
                await _source.QueryAsync(
                    new LogbookQuery(Limit: 200),
                    cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

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
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class LogbookEntryItemViewModel
{
    private readonly LogbookEntry _entry;

    public LogbookEntryItemViewModel(LogbookEntry entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

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
