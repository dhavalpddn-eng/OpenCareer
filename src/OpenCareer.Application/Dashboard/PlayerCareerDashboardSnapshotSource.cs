using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Application.Dashboard;

public sealed class PlayerCareerDashboardSnapshotSource(
    PlayerCareerRuntimeState career,
    IEconomyLedgerStore ledger,
    TimeProvider timeProvider)
    : IDashboardSnapshotSource
{
    private const int RecentTransactionLimit = 500;

    private readonly PlayerCareerRuntimeState _career =
        career ?? throw new ArgumentNullException(nameof(career));
    private readonly IEconomyLedgerStore _ledger =
        ledger ?? throw new ArgumentNullException(nameof(ledger));
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
            World = worldSummary
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
