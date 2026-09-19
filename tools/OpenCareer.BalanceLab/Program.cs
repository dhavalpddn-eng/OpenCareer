using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Finance;
using OpenCareer.Domain.Maintenance;
using OpenCareer.Domain.Ownership;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.BalanceLab;

internal static class Program
{
    private const int CareerHours = 64;
    private const int MonteCarloRuns = 1_000;
    private const int OffersPerHour = 6;
    private const decimal CalibrationStorageCost = 250m;

    private static readonly ContractKind[] ContractKinds = Enum.GetValues<ContractKind>();
    private static readonly string[] Airports =
    [
        "KRME", "KALB", "KSYR", "KBUF", "KBOS",
        "KBDL", "KPIT", "KPHL", "KTEB", "KROC",
        "KITH", "KBGM", "KSWF", "KAVP", "KERI",
        "KMHT", "KPVD", "KBTV", "KABE", "KPSM"
    ];

    private static readonly string[] Markets =
    [
        "general", "cargo", "medical", "passenger", "government", "industrial"
    ];

    public static int Main()
    {
        try
        {
            var policy = JobEconomyBalancePolicy.Default;
            policy.Validate();

            var cashThreshold = CashOwnershipThreshold();
            var financedThreshold = FinancedOwnershipThreshold();

            var adversarial = RunAdversarialRotation(policy, CareerHours, cashThreshold, financedThreshold);
            var spam = RunBestSingleKindSpam(policy, CareerHours);
            var shortHop = RunShortHopSpam(policy, CareerHours);
            var monteCarlo = RunMonteCarlo(policy, cashThreshold);
            var progression = RunProgressionCalibration(policy, cashThreshold);
            var carryingCosts = RunOwnershipCarryingCostPreview();

            Require(
                adversarial.HourlyAverage <=
                policy.TargetNetPerCareerCreditHour * (decimal)policy.MaximumTotalPlayerNetFactor,
                "Adversarial rotation exceeded the total player-net hourly ceiling.");
            Require(
                adversarial.CashAcquisitionHour is >= 50 and <= 80,
                "Adversarial cash acquisition escaped the 50-80 hour target.");
            Require(
                adversarial.FinancedAcquisitionHour is >= 50 and <= 80,
                "Adversarial financed acquisition escaped the 50-80 hour target.");
            Require(
                spam.TotalNet < adversarial.TotalNet,
                "Single mission-kind spam matched or beat diversified adversarial play.");
            Require(
                shortHop.HourlyAverage < adversarial.HourlyAverage * 0.90m,
                "Short-hop spam is too competitive with diversified play.");
            Require(
                monteCarlo.MinimumAcquisitionHour >= 50,
                "Synthetic greedy careers found an acquisition path below 50 hours.");
            Require(
                monteCarlo.MaximumAcquisitionHour <= 80,
                "Synthetic greedy careers failed to reach the ownership target by 80 hours.");
            Require(
                monteCarlo.Maximum64HourNet <=
                policy.TargetNetPerCareerCreditHour
                * CareerHours
                * (decimal)policy.MaximumTotalPlayerNetFactor,
                "Synthetic greedy career exceeded the hard 64-hour player-net ceiling.");
            Require(
                progression.Strong.AcquisitionHour is >= 50 and <= 60,
                "Strong progression escaped the intended 50-60 hour acquisition band.");
            Require(
                progression.Ordinary.AcquisitionHour is >= 55 and <= 70,
                "Ordinary progression escaped the intended 55-70 hour acquisition band.");
            Require(
                progression.Struggling.AcquisitionHour is >= 65 and <= 80,
                "Struggling progression escaped the intended 65-80 hour acquisition band.");
            Require(
                progression.Strong.HourlyAverage > progression.Ordinary.HourlyAverage
                && progression.Ordinary.HourlyAverage > progression.Struggling.HourlyAverage,
                "Strong/ordinary/struggling progression ordering collapsed.");
            Require(
                progression.Strong.HourlyAverage >= policy.TargetNetPerCareerCreditHour * 1.05m
                && progression.Strong.HourlyAverage <= policy.TargetNetPerCareerCreditHour * 1.15m,
                "Strong progression left the calibrated hourly band.");
            Require(
                progression.Ordinary.HourlyAverage >= policy.TargetNetPerCareerCreditHour * 0.90m
                && progression.Ordinary.HourlyAverage <= policy.TargetNetPerCareerCreditHour * 1.05m,
                "Ordinary progression left the calibrated hourly band.");
            Require(
                progression.Struggling.HourlyAverage >= policy.TargetNetPerCareerCreditHour * 0.75m
                && progression.Struggling.HourlyAverage <= policy.TargetNetPerCareerCreditHour * 0.95m,
                "Struggling progression left the calibrated hourly band.");
            Require(
                carryingCosts.FinancingApproved,
                "Representative light-aircraft financing was unexpectedly declined.");
            Require(
                carryingCosts.CashMonthlyFixedCost <= carryingCosts.RequiredOperatingReserve * 0.25m,
                "Cash-ownership fixed monthly carrying cost consumed more than 25% of the operating reserve.");
            Require(
                carryingCosts.FinancedMonthlyFixedCost <= carryingCosts.RequiredOperatingReserve * 0.35m,
                "Financed fixed monthly carrying cost consumed more than 35% of the operating reserve.");
            Require(
                carryingCosts.FiftyHourMaintenanceCost <= carryingCosts.RequiredOperatingReserve * 0.20m,
                "Routine 50-hour maintenance consumed more than 20% of the operating reserve.");
            Require(
                carryingCosts.FinancedMonthlyFixedCost + carryingCosts.FiftyHourMaintenanceCost
                <= carryingCosts.RequiredOperatingReserve * 0.50m,
                "One financed billing cycle plus routine 50-hour maintenance consumed more than half the operating reserve.");

            Console.WriteLine("OpenCareer Economy BalanceLab");
            Console.WriteLine(
                $"Adversarial max-context rotation: {Money(adversarial.TotalNet)} / {CareerHours} h " +
                $"({Money(adversarial.HourlyAverage)}/h), cash ownership h={adversarial.CashAcquisitionHour}, " +
                $"financed ownership h={adversarial.FinancedAcquisitionHour}.");
            Console.WriteLine(
                $"Best single-kind spam: {spam.Kind}, {Money(spam.TotalNet)} / {CareerHours} h " +
                $"({Money(spam.HourlyAverage)}/h).");
            Console.WriteLine(
                $"10-minute same-route Express Cargo spam: {Money(shortHop.TotalNet)} / {CareerHours} h " +
                $"({Money(shortHop.HourlyAverage)}/h).");
            Console.WriteLine(
                $"Greedy synthetic careers ({MonteCarloRuns} runs, {OffersPerHour} offers/h): " +
                $"64h net p05={Money(monteCarlo.NetP05)}, median={Money(monteCarlo.NetP50)}, " +
                $"p95={Money(monteCarlo.NetP95)}; ownership h p05={monteCarlo.AcquisitionP05:F0}, " +
                $"median={monteCarlo.AcquisitionP50:F0}, p95={monteCarlo.AcquisitionP95:F0}.");
            Console.WriteLine(
                $"Progression calibration: strong={Money(progression.Strong.NetAt64)} / 64 h " +
                $"({Money(progression.Strong.HourlyAverage)}/h, ownership h={progression.Strong.AcquisitionHour}); " +
                $"ordinary={Money(progression.Ordinary.NetAt64)} / 64 h " +
                $"({Money(progression.Ordinary.HourlyAverage)}/h, ownership h={progression.Ordinary.AcquisitionHour}); " +
                $"struggling={Money(progression.Struggling.NetAt64)} / 64 h " +
                $"({Money(progression.Struggling.HourlyAverage)}/h, ownership h={progression.Struggling.AcquisitionHour}).");
            Console.WriteLine(
                $"Ownership carrying-cost preview: cash fixed={Money(carryingCosts.CashMonthlyFixedCost)}/month; " +
                $"financed fixed={Money(carryingCosts.FinancedMonthlyFixedCost)}/month " +
                $"(loan {Money(carryingCosts.MonthlyLoanPayment)}); " +
                $"routine 50h maintenance={Money(carryingCosts.FiftyHourMaintenanceCost)}; " +
                $"reserve={Money(carryingCosts.RequiredOperatingReserve)}.");
            Console.WriteLine(
                $"PASS all balance gates. Cash threshold={Money(cashThreshold)}; " +
                $"financed threshold={Money(financedThreshold)}.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BALANCE FAILURE: {exception.Message}");
            return 1;
        }
    }

    private static BalancePath RunAdversarialRotation(
        JobEconomyBalancePolicy policy,
        int hours,
        decimal cashThreshold,
        decimal financedThreshold)
    {
        var history = new List<HistoryEntry>();
        decimal total = 0m;
        int? cashHour = null;
        int? financedHour = null;

        for (var hour = 0; hour < hours; hour++)
        {
            Candidate? best = null;

            foreach (var kind in ContractKinds)
            {
                var candidate = new Candidate(
                    kind,
                    Origin: $"A{hour:D2}{(int)kind:D2}",
                    Destination: $"B{hour:D2}{(int)kind:D2}",
                    MarketId: $"M{hour:D2}{(int)kind:D2}",
                    DemandIndex: 2,
                    Urgency: 1,
                    Complexity: 1);

                var quote = Quote(policy, candidate, history, 1);
                if (best is null || quote.TargetPlayerNet > best.Value.PlayerNet)
                    best = candidate with { PlayerNet = quote.TargetPlayerNet };
            }

            var selected = best ?? throw new InvalidOperationException("No adversarial candidate was produced.");
            total += selected.PlayerNet;
            history.Add(ToHistory(selected));

            cashHour ??= total >= cashThreshold ? hour + 1 : null;
            financedHour ??= total >= financedThreshold ? hour + 1 : null;
        }

        return new(
            total,
            total / hours,
            cashHour ?? int.MaxValue,
            financedHour ?? int.MaxValue);
    }

    private static SpamPath RunBestSingleKindSpam(
        JobEconomyBalancePolicy policy,
        int hours)
    {
        SpamPath? best = null;

        foreach (var kind in ContractKinds)
        {
            var history = new List<HistoryEntry>();
            decimal total = 0m;

            for (var hour = 0; hour < hours; hour++)
            {
                var candidate = new Candidate(
                    kind,
                    Origin: $"A{hour:D3}",
                    Destination: $"B{hour:D3}",
                    MarketId: $"M{hour:D3}",
                    DemandIndex: 2,
                    Urgency: 1,
                    Complexity: 1);

                var quote = Quote(policy, candidate, history, 1);
                total += quote.TargetPlayerNet;
                history.Add(ToHistory(candidate));
            }

            var path = new SpamPath(kind, total, total / hours);
            if (best is null || path.TotalNet > best.Value.TotalNet)
                best = path;
        }

        return best ?? throw new InvalidOperationException("No spam path was produced.");
    }

    private static SpamPath RunShortHopSpam(
        JobEconomyBalancePolicy policy,
        int hours)
    {
        const int jobsPerHour = 6;
        var history = new List<HistoryEntry>();
        decimal total = 0m;

        for (var job = 0; job < hours * jobsPerHour; job++)
        {
            var candidate = new Candidate(
                ContractKind.ExpressCargo,
                "KRME",
                "KALB",
                "cargo-ny",
                DemandIndex: 2,
                Urgency: 1,
                Complexity: 1);

            var quote = Quote(policy, candidate, history, 1d / jobsPerHour);
            total += quote.TargetPlayerNet;
            history.Add(ToHistory(candidate));
        }

        return new(ContractKind.ExpressCargo, total, total / hours);
    }

    private static MonteCarloReport RunMonteCarlo(
        JobEconomyBalancePolicy policy,
        decimal cashThreshold)
    {
        var totalsAt64 = new decimal[MonteCarloRuns];
        var acquisitionHours = new int[MonteCarloRuns];

        for (var run = 0; run < MonteCarloRuns; run++)
        {
            var random = DeterministicSeed.CreateStream(0x4F50454E43415245UL, $"balance-run-{run}");
            var history = new List<HistoryEntry>();
            decimal total = 0m;
            decimal totalAt64 = 0m;
            int? acquisitionHour = null;

            for (var hour = 0; hour < 80; hour++)
            {
                Candidate? best = null;

                for (var offer = 0; offer < OffersPerHour; offer++)
                {
                    var kind = ContractKinds[random.NextInt(0, ContractKinds.Length)];
                    var originIndex = random.NextInt(0, Airports.Length);
                    var destinationIndex = random.NextInt(0, Airports.Length - 1);
                    if (destinationIndex >= originIndex)
                        destinationIndex++;

                    var candidate = new Candidate(
                        kind,
                        Airports[originIndex],
                        Airports[destinationIndex],
                        Markets[random.NextInt(0, Markets.Length)],
                        DemandIndex: Math.Clamp(1 + random.NextNormal(0, 0.18), 0.5, 1.5),
                        Urgency: Math.Pow(random.NextDouble(), 2),
                        Complexity: Math.Pow(random.NextDouble(), 1.5));

                    var quote = Quote(policy, candidate, history, 1);
                    if (best is null || quote.TargetPlayerNet > best.Value.PlayerNet)
                        best = candidate with { PlayerNet = quote.TargetPlayerNet };
                }

                var selected = best ?? throw new InvalidOperationException("No Monte Carlo offer was produced.");
                total += selected.PlayerNet;
                history.Add(ToHistory(selected));

                if (hour == CareerHours - 1)
                    totalAt64 = total;

                acquisitionHour ??= total >= cashThreshold ? hour + 1 : null;
            }

            totalsAt64[run] = totalAt64;
            acquisitionHours[run] = acquisitionHour ?? int.MaxValue;
        }

        Array.Sort(totalsAt64);
        Array.Sort(acquisitionHours);

        return new(
            Percentile(totalsAt64, 0.05),
            Percentile(totalsAt64, 0.50),
            Percentile(totalsAt64, 0.95),
            totalsAt64[^1],
            acquisitionHours[0],
            acquisitionHours[^1],
            Percentile(acquisitionHours, 0.05),
            Percentile(acquisitionHours, 0.50),
            Percentile(acquisitionHours, 0.95));
    }

    private static ProgressionCalibrationReport RunProgressionCalibration(
        JobEconomyBalancePolicy policy,
        decimal cashThreshold)
    {
        var strong = RunProgressionProfile(
            policy,
            cashThreshold,
            new ProgressionProfile(
                [
                    ContractKind.Medical,
                    ContractKind.Agricultural,
                    ContractKind.GovernmentCourier,
                    ContractKind.ExpressCargo,
                    ContractKind.Charter,
                    ContractKind.Cargo,
                    ContractKind.Survey,
                    ContractKind.BannerTow,
                    ContractKind.Ferry
                ],
                DemandIndex: 1.35,
                Urgency: 0.75,
                Complexity: 0.70,
                RouteCycle: 0,
                MarketCycle: 0));

        var ordinary = RunProgressionProfile(
            policy,
            cashThreshold,
            new ProgressionProfile(
                [
                    ContractKind.Cargo,
                    ContractKind.Passenger,
                    ContractKind.Survey,
                    ContractKind.BannerTow,
                    ContractKind.Agricultural,
                    ContractKind.GovernmentCourier
                ],
                DemandIndex: 1.00,
                Urgency: 0.25,
                Complexity: 0.35,
                RouteCycle: 0,
                MarketCycle: 6));

        var struggling = RunProgressionProfile(
            policy,
            cashThreshold,
            new ProgressionProfile(
                [
                    ContractKind.Ferry,
                    ContractKind.Cargo,
                    ContractKind.Passenger,
                    ContractKind.Survey
                ],
                DemandIndex: 0.70,
                Urgency: 0.10,
                Complexity: 0.20,
                RouteCycle: 4,
                MarketCycle: 3));

        return new(strong, ordinary, struggling);
    }

    private static ProgressionPath RunProgressionProfile(
        JobEconomyBalancePolicy policy,
        decimal cashThreshold,
        ProgressionProfile profile)
    {
        if (profile.Kinds.Count == 0 || profile.RouteCycle < 0 || profile.MarketCycle < 0)
            throw new ArgumentException("Invalid progression profile.", nameof(profile));

        var history = new List<HistoryEntry>();
        decimal total = 0m;
        decimal totalAt64 = 0m;
        int? acquisitionHour = null;

        for (var hour = 0; hour < 80; hour++)
        {
            var kind = profile.Kinds[hour % profile.Kinds.Count];
            var routeIndex = profile.RouteCycle == 0 ? hour : hour % profile.RouteCycle;
            var marketIndex = profile.MarketCycle == 0 ? hour : hour % profile.MarketCycle;
            var candidate = new Candidate(
                kind,
                Origin: $"P{routeIndex:D2}A",
                Destination: $"P{routeIndex:D2}B",
                MarketId: $"profile-{marketIndex:D2}",
                profile.DemandIndex,
                profile.Urgency,
                profile.Complexity);

            var quote = Quote(policy, candidate, history, 1);
            total += quote.TargetPlayerNet;
            history.Add(ToHistory(candidate));

            if (hour == CareerHours - 1)
                totalAt64 = total;

            acquisitionHour ??= total >= cashThreshold ? hour + 1 : null;
        }

        return new(totalAt64, totalAt64 / CareerHours, acquisitionHour ?? int.MaxValue);
    }

    private static OwnershipCarryingCostPreview RunOwnershipCarryingCostPreview()
    {
        var reserve = CareerProgressionPolicy.Default.MinimumOperatingReserve;
        var storage = 250m;
        var insurance = InitialAircraftInsurance.StandardHull.MonthlyPremium;
        var cashMonthlyFixed = storage + insurance;

        var history = new CareerCreditHistory(
            RealFlightHours: 70,
            CompletedJobs: 45,
            FailedJobs: 1,
            OnTimePayments: 18,
            MissedPayments: 0,
            SafetyScore: 96,
            EmployerTrust: 92,
            VerifiedMonthlyNetIncome: 12_000m,
            ExistingMonthlyDebtPayments: 250m,
            AvailableCash: 120_000m,
            RequiredOperatingReserve: reserve,
            UnresolvedDefault: false);

        var loan = CareerCredit.Evaluate(
            history,
            InitialLenders.Community,
            new AircraftLoanRequest(
                Price: 60_000m,
                AppraisedValue: 58_000m,
                Deposit: 20_000m,
                TermMonths: 120,
                CivilianOwnershipEligible: true));

        var program = InitialMaintenancePrograms.LightAircraftFallback;
        var maintenance = AircraftMaintenanceEngine.CreateInitial(
            "balance-preview-light",
            acquiredConditionPercent: 82m,
            program,
            new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
        maintenance = AircraftMaintenanceEngine.ApplyUsage(
            maintenance,
            program,
            new MaintenanceUsage(
                AirframeHours: 50,
                EngineHours: 50,
                LandingCycles: 50,
                OverspeedMinutes: 0,
                EngineStressMinutes: 0,
                HardLandingSeverity: 0,
                MaximumPositiveG: 1.5,
                ExcessGSeconds: 0),
            new DateTimeOffset(2026, 9, 20, 14, 0, 0, TimeSpan.Zero));
        var service = AircraftMaintenanceEngine.QuoteService(
            maintenance,
            program,
            new DateTimeOffset(2026, 9, 20, 14, 0, 0, TimeSpan.Zero));

        return new(
            loan.Approved,
            reserve,
            cashMonthlyFixed,
            cashMonthlyFixed + loan.MonthlyPayment,
            loan.MonthlyPayment,
            service.Cost);
    }

    private static JobEconomyQuote Quote(
        JobEconomyBalancePolicy policy,
        Candidate candidate,
        IReadOnlyList<HistoryEntry> history,
        double hours)
    {
        var recent = history.Count <= JobRepeatExposureCalculator.DefaultRecentSettlementWindow
            ? history
            : history.Skip(history.Count - JobRepeatExposureCalculator.DefaultRecentSettlementWindow).ToArray();

        var family = JobEconomyBalancePolicy.FamilyFor(candidate.Kind);
        var sameFamily = recent.Count(x => x.Family == family);
        var sameRoute = recent.Count(x => SameCorridor(
            x.Origin,
            x.Destination,
            candidate.Origin,
            candidate.Destination));
        var sameMarket = recent.Count(x =>
            string.Equals(x.MarketId, candidate.MarketId, StringComparison.OrdinalIgnoreCase));

        return policy.Quote(new(
            candidate.Kind,
            ServiceTrack.CivilianEmployment,
            hours,
            EstimatedDirectOperatingCost: 0,
            candidate.DemandIndex,
            candidate.Urgency,
            candidate.Complexity,
            new JobRepeatExposure(sameFamily, sameRoute, sameMarket)));
    }

    private static HistoryEntry ToHistory(Candidate candidate) =>
        new(
            JobEconomyBalancePolicy.FamilyFor(candidate.Kind),
            candidate.Origin,
            candidate.Destination,
            candidate.MarketId);

    private static bool SameCorridor(
        string firstOrigin,
        string firstDestination,
        string secondOrigin,
        string secondDestination) =>
        (string.Equals(firstOrigin, secondOrigin, StringComparison.OrdinalIgnoreCase)
         && string.Equals(firstDestination, secondDestination, StringComparison.OrdinalIgnoreCase))
        || (string.Equals(firstOrigin, secondDestination, StringComparison.OrdinalIgnoreCase)
            && string.Equals(firstDestination, secondOrigin, StringComparison.OrdinalIgnoreCase));

    private static decimal CashOwnershipThreshold()
    {
        var policy = CareerProgressionPolicy.Default;
        var dealer = InitialDealers.FleetBroker;
        var bestSalePrice = decimal.Round(
            policy.TypicalUsedLightAircraftPrice
            * (1m + dealer.MarkupRate)
            * (1m - dealer.MaximumDiscountRate),
            2);

        return bestSalePrice
            + policy.MinimumOperatingReserve
            + CalibrationStorageCost
            + InitialAircraftInsurance.StandardHull.MonthlyPremium;
    }

    private static decimal FinancedOwnershipThreshold()
    {
        var policy = CareerProgressionPolicy.FinancedLargerAircraft;
        var dealer = InitialDealers.FleetBroker;
        var bestSalePrice = decimal.Round(
            policy.TypicalUsedLightAircraftPrice
            * (1m + dealer.MarkupRate)
            * (1m - dealer.MaximumDiscountRate),
            2);

        return decimal.Round(bestSalePrice * policy.MinimumDownPaymentRate, 2)
            + policy.MinimumOperatingReserve
            + CalibrationStorageCost
            + InitialAircraftInsurance.StandardHull.MonthlyPremium;
    }

    private static decimal Percentile(decimal[] sorted, double percentile)
    {
        var index = (int)Math.Round(
            (sorted.Length - 1) * percentile,
            MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static double Percentile(int[] sorted, double percentile)
    {
        var index = (int)Math.Round(
            (sorted.Length - 1) * percentile,
            MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string Money(decimal value) => value.ToString("C2");

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private readonly record struct Candidate(
        ContractKind Kind,
        string Origin,
        string Destination,
        string MarketId,
        double DemandIndex,
        double Urgency,
        double Complexity,
        decimal PlayerNet = 0m);

    private readonly record struct HistoryEntry(
        MissionFamily Family,
        string Origin,
        string Destination,
        string MarketId);

    private readonly record struct BalancePath(
        decimal TotalNet,
        decimal HourlyAverage,
        int CashAcquisitionHour,
        int FinancedAcquisitionHour);

    private readonly record struct SpamPath(
        ContractKind Kind,
        decimal TotalNet,
        decimal HourlyAverage);

    private readonly record struct OwnershipCarryingCostPreview(
        bool FinancingApproved,
        decimal RequiredOperatingReserve,
        decimal CashMonthlyFixedCost,
        decimal FinancedMonthlyFixedCost,
        decimal MonthlyLoanPayment,
        decimal FiftyHourMaintenanceCost);

    private sealed record ProgressionProfile(
        IReadOnlyList<ContractKind> Kinds,
        double DemandIndex,
        double Urgency,
        double Complexity,
        int RouteCycle,
        int MarketCycle);

    private readonly record struct ProgressionPath(
        decimal NetAt64,
        decimal HourlyAverage,
        int AcquisitionHour);

    private readonly record struct ProgressionCalibrationReport(
        ProgressionPath Strong,
        ProgressionPath Ordinary,
        ProgressionPath Struggling);

    private readonly record struct MonteCarloReport(
        decimal NetP05,
        decimal NetP50,
        decimal NetP95,
        decimal Maximum64HourNet,
        int MinimumAcquisitionHour,
        int MaximumAcquisitionHour,
        double AcquisitionP05,
        double AcquisitionP50,
        double AcquisitionP95);
}
