using OpenCareer.Application.Careers;
using OpenCareer.Application.Economy;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Dashboard;

public sealed class PlayerCareerDashboardSnapshotSource(
    PlayerCareerRuntimeState career,
    IEconomyLedgerStore ledger,
    ILogbookSource logbook,
    IJobContractRuntimeSource contracts,
    FlightSessionCoordinator flightSessions,
    CareerJobPlayableLoopReadinessSource readiness,
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
    private readonly IJobContractRuntimeSource _contracts =
        contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly FlightSessionCoordinator _flightSessions =
        flightSessions ?? throw new ArgumentNullException(nameof(flightSessions));
    private readonly CareerJobPlayableLoopReadinessSource _readiness =
        readiness ?? throw new ArgumentNullException(nameof(readiness));
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
            ActiveOperation = ProjectActiveOperation(),
            World = worldSummary,
            RecentActivity = logbookEntries
                .OrderByDescending(static entry => entry.Debrief.EndedAt)
                .ThenBy(static entry => entry.EntryId)
                .Select(ProjectActivity)
                .ToArray()
        };
    }

    private DashboardActiveOperationSummary? ProjectActiveOperation()
    {
        FlightSession? session = _flightSessions.Current;
        PersistedJobContract? persisted = session?.ContractId is { } contractId
            ? _contracts.Find(contractId)
            : _contracts.Current
                .Where(static item => item.Contract.Status is
                    ContractStatus.Accepted or ContractStatus.InProgress)
                .OrderByDescending(static item => item.Contract.AcceptedAt)
                .ThenBy(static item => item.Contract.ContractId)
                .FirstOrDefault();

        if (persisted is null)
            return null;

        persisted.Validate();
        JobContract contract = persisted.Contract;
        CareerJobPlayableReadinessSnapshot readiness = _readiness.Current;
        bool matchingSession = session?.ContractId == contract.ContractId;
        ActiveOperationStage stage = MapStage(contract, session, readiness, matchingSession);
        bool blocked = readiness.State is
            CareerJobPlayableReadinessState.NoCareerFlight or
            CareerJobPlayableReadinessState.ContractStateUnavailable or
            CareerJobPlayableReadinessState.FlightSuspended or
            CareerJobPlayableReadinessState.AwaitingFlightEvidence or
            CareerJobPlayableReadinessState.FailedOrCancelled;
        DashboardActionTarget target = matchingSession
            ? DashboardActionTarget.CurrentFlight
            : DashboardActionTarget.Jobs;

        return new DashboardActiveOperationSummary(
            contract.ContractId.ToString("D"),
            Friendly(contract.Kind),
            contract.OriginIcao,
            contract.DestinationIcao,
            stage,
            target,
            target == DashboardActionTarget.CurrentFlight
                ? "Open Current Flight"
                : "Open Jobs",
            readiness.Detail,
            ChecklistRequired: false,
            IsBlocked: blocked,
            BlockingReason: blocked ? readiness.Detail : null,
            GrossPay: ContractPay(contract.Compensation),
            EstimatedNetPay: null);
    }

    private static ActiveOperationStage MapStage(
        JobContract contract,
        FlightSession? session,
        CareerJobPlayableReadinessSnapshot readiness,
        bool matchingSession)
    {
        if (readiness.State ==
            CareerJobPlayableReadinessState.CompletedAwaitingTerminalWorkflow)
        {
            return ActiveOperationStage.AwaitingSettlement;
        }

        if (readiness.State is
            CareerJobPlayableReadinessState.AwaitingFlightEvidence or
            CareerJobPlayableReadinessState.AwaitingVerifiedCompletionInputs)
        {
            return ActiveOperationStage.PostFlight;
        }

        if (!matchingSession || session is null)
        {
            return contract.Status == ContractStatus.Accepted
                ? ActiveOperationStage.Accepted
                : ActiveOperationStage.PreparationRequired;
        }

        return session.OperationState switch
        {
            FlightOperationState.Accepted => ActiveOperationStage.Accepted,
            FlightOperationState.Preparation or
            FlightOperationState.Servicing or
            FlightOperationState.Loading => ActiveOperationStage.PreparationRequired,
            FlightOperationState.ReadyForStart => ActiveOperationStage.ReadyToStart,
            FlightOperationState.Shutdown or
            FlightOperationState.Complete => ActiveOperationStage.PostFlight,
            _ => ActiveOperationStage.InProgress
        };
    }

    private static decimal ContractPay(ContractCompensation compensation) =>
        compensation.Model == CompensationModel.CompanyRevenue
            ? compensation.GrossCustomerRevenue
            : compensation.PilotCompensation;

    private static string Friendly<T>(T value)
        where T : struct, Enum =>
        string.Concat(
            value.ToString().Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $" {character}"
                        : character.ToString()));

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
