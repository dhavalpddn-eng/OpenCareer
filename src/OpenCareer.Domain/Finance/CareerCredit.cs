namespace OpenCareer.Domain.Finance;

// Game underwriting inputs from settled career history, never from a UI-entered score.
public sealed record CareerCreditHistory(
    decimal RealFlightHours, int CompletedJobs, int FailedJobs, int OnTimePayments,
    int MissedPayments, decimal SafetyScore, decimal EmployerTrust,
    decimal VerifiedMonthlyNetIncome, decimal ExistingMonthlyDebtPayments,
    decimal AvailableCash, decimal RequiredOperatingReserve, bool UnresolvedDefault)
{
    public void Validate()
    {
        if (RealFlightHours < 0 || CompletedJobs < 0 || FailedJobs < 0 || OnTimePayments < 0 || MissedPayments < 0 ||
            SafetyScore is < 0 or > 100 || EmployerTrust is < 0 or > 100 || VerifiedMonthlyNetIncome < 0 ||
            ExistingMonthlyDebtPayments < 0 || AvailableCash < 0 || RequiredOperatingReserve < 0)
            throw new ArgumentOutOfRangeException(nameof(CareerCreditHistory));
    }

    public int CreditScore
    {
        get
        {
            Validate();
            // Priors stop a single successful flight/payment producing a perfect score.
            var reliability = (CompletedJobs + 5m) / (CompletedJobs + (decimal)FailedJobs + 10m);
            var paymentHistory = (OnTimePayments + 3m) / (OnTimePayments + (decimal)MissedPayments + 6m);
            var score = 300m + 150m * reliability + 150m * paymentHistory + SafetyScore + EmployerTrust +
                50m * Math.Min(RealFlightHours / 80m, 1m) - Math.Min(MissedPayments * 25m, 150m);
            return (int)Math.Clamp(decimal.Round(score), 300m, 850m);
        }
    }
}

public sealed record LenderProfile(string Id, string Name, int MinimumScore,
    decimal BaseAnnualRate, decimal MaximumRiskPremium, decimal MaximumLoanToValue,
    decimal MaximumDebtServiceRatio, decimal MaximumPrincipal, int MaximumTermMonths)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (MinimumScore is < 300 or > 850 || BaseAnnualRate is < 0 or > 0.5m ||
            MaximumRiskPremium is < 0 or > 0.5m || MaximumLoanToValue is <= 0 or > 1 ||
            MaximumDebtServiceRatio is <= 0 or > 1 || MaximumPrincipal <= 0 || MaximumTermMonths is < 1 or > 240)
            throw new ArgumentOutOfRangeException(nameof(LenderProfile));
    }
}

public enum LoanDeclineReason { None, RestrictedAsset, UnresolvedDefault, InsufficientCareerStanding, NoAffordableCapacity, InsufficientDeposit }
public sealed record AircraftLoanRequest(decimal Price, decimal AppraisedValue, decimal Deposit,
    int TermMonths, bool CivilianOwnershipEligible);
public sealed record LoanDecision(bool Approved, LoanDeclineReason Reason, int CreditScore,
    decimal AnnualRate, decimal MaximumAvailablePrincipal, decimal RequestedPrincipal, decimal MonthlyPayment);

public static class CareerCredit
{
    public static LoanDecision Evaluate(CareerCreditHistory history, LenderProfile lender, AircraftLoanRequest request,
        decimal marketRateAdjustment = 0m)
    {
        history.Validate(); lender.Validate();
        if (request.Price <= 0 || request.AppraisedValue <= 0 || request.Deposit < 0 || request.Deposit >= request.Price ||
            request.TermMonths < 1 || request.TermMonths > lender.MaximumTermMonths || marketRateAdjustment is < -0.5m or > 0.5m)
            throw new ArgumentOutOfRangeException(nameof(request));
        var score = history.CreditScore;
        var annualRate = Math.Clamp(lender.BaseAnnualRate + marketRateAdjustment +
            lender.MaximumRiskPremium * (850m - score) / 550m, 0m, 0.5m);
        var factor = PaymentFactor(annualRate, request.TermMonths);
        var monthlyCapacity = Math.Max(0m, history.VerifiedMonthlyNetIncome * lender.MaximumDebtServiceRatio - history.ExistingMonthlyDebtPayments);
        var maximum = decimal.Floor(Math.Min(lender.MaximumPrincipal,
            Math.Min(Math.Min(request.Price, request.AppraisedValue) * lender.MaximumLoanToValue, monthlyCapacity / factor)) * 100m) / 100m;
        var principal = request.Price - request.Deposit;
        var payment = decimal.Ceiling(principal * factor * 100m) / 100m;
        var reason = !request.CivilianOwnershipEligible ? LoanDeclineReason.RestrictedAsset :
            history.UnresolvedDefault ? LoanDeclineReason.UnresolvedDefault :
            score < lender.MinimumScore ? LoanDeclineReason.InsufficientCareerStanding :
            principal > maximum || payment > monthlyCapacity ? LoanDeclineReason.NoAffordableCapacity :
            request.Deposit > Math.Max(0m, history.AvailableCash - history.RequiredOperatingReserve) ? LoanDeclineReason.InsufficientDeposit :
            LoanDeclineReason.None;
        var eligible = reason is not (LoanDeclineReason.RestrictedAsset or LoanDeclineReason.UnresolvedDefault or LoanDeclineReason.InsufficientCareerStanding);
        return new(reason == LoanDeclineReason.None, reason, score, annualRate, eligible ? maximum : 0m, principal, payment);
    }

    private static decimal PaymentFactor(decimal annualRate, int months)
    {
        var monthlyRate = annualRate / 12m;
        // Present-value sum avoids cancellation for near-zero rates and handles zero directly.
        decimal discount = 1m;
        decimal annuity = 0m;
        for (var month = 0; month < months; month++)
        {
            discount /= 1m + monthlyRate;
            annuity += discount;
        }
        return 1m / annuity;
    }
}

public static class InitialLenders
{
    // Fictional game institutions and tuning, not real credit products.
    public static LenderProfile Community { get; } = new("community", "Community Aviation Credit", 600, .06m, .06m, .8m, .30m, 150_000m, 120);
    public static LenderProfile Commercial { get; } = new("commercial", "Commercial Fleet Finance", 720, .045m, .045m, .85m, .35m, 5_000_000m, 180);
    public static LenderProfile Specialist { get; } = new("specialist", "Second Horizon Finance", 500, .11m, .09m, .65m, .25m, 300_000m, 120);
}
