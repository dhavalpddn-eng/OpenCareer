using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Dashboard;

public sealed class PlayerCareerDashboardSnapshotSource(
    PlayerCareerRuntimeState career,
    IEconomyLedgerStore ledger,
    ILogbookSource logbook,
    TimeProvider timeProvider)
    : IDashboardSnapshotSource
{
    private const int RecentTransactionLimit = 500;
    private const int RecentActivityLimit = 6;

    private readonly PlayerCareerRuntimeState _career =
        career ?? throw new ArgumentNullException(nameof(career));
    private readonly IEconomyLedgerStore _ledger =
        ledger ?? throw new ArgumentNullException(nameof(ledger));
    private readonly ILogbookSource _logbook =
        logbook ?? throw new ArgumentNullException(nameof(logbook));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<DashboardSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfileStoreRecord? current = await _career
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
        decimal cash = await _ledger
            .ReadCashBalanceAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<EconomyLedgerTransaction> recent = await _ledger
            .ReadRecentAsync(RecentTransactionLimit, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<LogbookEntry> logbookEntries = await _logbook
            .QueryAsync(
                new LogbookQuery(Limit: RecentActivityLimit),
                cancellationToken)
            .ConfigureAwait(false);

        DashboardCareerSummary? careerSummary = null;
        DashboardWorldSummary? worldSummary = null;

        if (current is not null)
        {
            current.Validate();
            PlayerCareerProfile profile = current.Profile;

            careerSummary = new DashboardCareerSummary(
                Level: null,
                CurrentXp: null,
                XpForNextLevel: null,
                LicenseSummary: FormatQualifications(profile.Qualifications),
                TotalFlightHours: profile.Experience.CareerCreditTime.TotalHours,
                AircraftOwned: null,
                NextMilestone: null,
                RecentAchievement: null);
            worldSummary = new DashboardWorldSummary(
                PlayerLocation: profile.Location.CurrentAirportIcao,
                HomeBase: profile.Location.HomeAirportIcao,
                NearbyOpportunityCount: null,
                ActiveWorldEventCount: null,
                ActiveMarketSignalCount: null,
                ActiveGovernmentSignalCount: null);
        }

        DateTime localToday = _timeProvider.GetLocalNow().Date;
        decimal todayNet = recent
            .Where(transaction =>
                TimeZoneInfo.ConvertTime(
                    transaction.OccurredAt,
                    _timeProvider.LocalTimeZone).Date == localToday)
            .Sum(transaction => transaction.CashChange);

        return DashboardSnapshot.Empty with
        {
            Career = careerSummary,
            Finances = new DashboardFinanceSummary(
                Cash: cash,
                TodayNet: todayNet,
                UpcomingObligations: null),
            World = worldSummary,
            RecentActivity = logbookEntries
                .OrderByDescending(static entry => entry.Debrief.EndedAt)
                .ThenBy(static entry => entry.EntryId)
                .Select(ProjectActivity)
                .ToArray()
        };
    }

    private static DashboardRecentActivity ProjectActivity(
        LogbookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        FlightDebrief debrief = entry.Debrief;

        return new DashboardRecentActivity(
            debrief.EndedAt,
            FormatCategory(debrief.EntryKind),
            $"{debrief.Aircraft.DisplayName} • {FormatRoute(debrief.Route)} • {FormatOutcome(debrief)}",
            DashboardActionTarget.Logbook);
    }

    private static string FormatCategory(LogbookEntryKind kind) =>
        kind switch
        {
            LogbookEntryKind.CareerJob => "Career flight",
            LogbookEntryKind.FreeFlight => "Free flight",
            LogbookEntryKind.Training => "Training flight",
            LogbookEntryKind.Reposition => "Reposition flight",
            LogbookEntryKind.Other => "Other flight",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static string FormatRoute(FlightRouteDebrief route)
    {
        string? actualDeparture = Known(route.ActualDeparture);
        string? actualArrival = Known(route.ActualArrival);
        string? plannedOrigin = Known(route.PlannedOrigin);
        string? plannedDestination = Known(route.PlannedDestination);

        if (actualDeparture is not null || actualArrival is not null)
        {
            return $"Actual {actualDeparture ?? "unknown departure"} → " +
                   $"{actualArrival ?? "unknown arrival"}";
        }

        if (plannedOrigin is not null || plannedDestination is not null)
        {
            return $"Planned {plannedOrigin ?? "unknown origin"} → " +
                   $"{plannedDestination ?? "unknown destination"}";
        }

        return "Route unknown";
    }

    private static string? Known(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FormatOutcome(FlightDebrief debrief)
    {
        string safety = debrief.SafetyOutcome switch
        {
            FlightSafetyOutcome.CompletedNormally => "Completed normally",
            FlightSafetyOutcome.CompletedWithIncident => "Completed with incident",
            FlightSafetyOutcome.DivertedSafely => "Diverted safely",
            FlightSafetyOutcome.Interrupted => "Interrupted",
            FlightSafetyOutcome.Crashed => "Crashed",
            _ => throw new ArgumentOutOfRangeException(nameof(debrief))
        };

        return debrief.MissionOutcome switch
        {
            MissionOutcome.NotApplicable => safety,
            MissionOutcome.Pending => $"Mission pending; {safety.ToLowerInvariant()}",
            MissionOutcome.Succeeded => $"Mission succeeded; {safety.ToLowerInvariant()}",
            MissionOutcome.PartiallySucceeded => $"Mission partially succeeded; {safety.ToLowerInvariant()}",
            MissionOutcome.Rerouted => $"Mission rerouted; {safety.ToLowerInvariant()}",
            MissionOutcome.Failed => $"Mission failed; {safety.ToLowerInvariant()}",
            MissionOutcome.Cancelled => $"Mission cancelled; {safety.ToLowerInvariant()}",
            _ => throw new ArgumentOutOfRangeException(nameof(debrief))
        };
    }

    private static string FormatQualifications(
        PilotQualificationState qualifications)
    {
        qualifications.Validate();

        string license = qualifications.License switch
        {
            PilotLicenseLevel.None => "No pilot license",
            PilotLicenseLevel.Student => "Student pilot",
            PilotLicenseLevel.Private => "Private pilot",
            PilotLicenseLevel.Commercial => "Commercial pilot",
            PilotLicenseLevel.AirlineTransport => "Airline transport pilot",
            _ => throw new ArgumentOutOfRangeException(nameof(qualifications))
        };

        string[] ratings = qualifications.Ratings
            .OrderBy(static rating => rating)
            .Select(static rating => rating switch
            {
                PilotRating.AirplaneSingleEngineLand => "Airplane single-engine land",
                PilotRating.AirplaneMultiEngineLand => "Airplane multi-engine land",
                PilotRating.InstrumentAirplane => "Instrument airplane",
                _ => throw new ArgumentOutOfRangeException(nameof(rating))
            })
            .ToArray();

        return ratings.Length == 0
            ? license
            : $"{license} • {string.Join(", ", ratings)}";
    }
}
