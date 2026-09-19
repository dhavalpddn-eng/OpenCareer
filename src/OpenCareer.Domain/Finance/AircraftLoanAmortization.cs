using OpenCareer.Domain.Economy;

namespace OpenCareer.Domain.Finance;

public sealed record AircraftLoanInstallment(
    int Number,
    decimal OpeningPrincipal,
    decimal Interest,
    decimal Principal,
    decimal Payment,
    decimal ClosingPrincipal)
{
    public void Validate()
    {
        if (Number < 1)
            throw new ArgumentOutOfRangeException(nameof(Number));

        ValidateMoney(OpeningPrincipal, nameof(OpeningPrincipal));
        ValidateMoney(Interest, nameof(Interest));
        ValidateMoney(Principal, nameof(Principal));
        ValidateMoney(Payment, nameof(Payment));
        ValidateMoney(ClosingPrincipal, nameof(ClosingPrincipal));

        if (Principal > OpeningPrincipal
            || ClosingPrincipal != OpeningPrincipal - Principal
            || Payment != Principal + Interest)
        {
            throw new ArgumentException(
                "Loan installment does not reconcile.");
        }
    }

    private static void ValidateMoney(
        decimal value,
        string name)
    {
        if (value < 0m
            || decimal.Round(
                value,
                2,
                MidpointRounding.AwayFromZero)
                != value)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed record AircraftLoanAmortizationSchedule(
    Guid LoanId,
    IReadOnlyList<AircraftLoanInstallment> Installments)
{
    public decimal TotalPrincipal =>
        Installments.Sum(item => item.Principal);

    public decimal TotalInterest =>
        Installments.Sum(item => item.Interest);

    public decimal TotalPayments =>
        Installments.Sum(item => item.Payment);

    public void Validate(
        AircraftLoanAgreement agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        agreement.Validate();
        ArgumentNullException.ThrowIfNull(Installments);

        if (LoanId != agreement.LoanId
            || Installments.Count != agreement.TermMonths)
        {
            throw new ArgumentException(
                "Loan amortization schedule does not match the agreement.");
        }

        for (int index = 0; index < Installments.Count; index++)
        {
            AircraftLoanInstallment installment =
                Installments[index];
            installment.Validate();

            if (installment.Number != index + 1)
                throw new ArgumentException(
                    "Loan installments must be sequential.");

            if (index == 0
                && installment.OpeningPrincipal
                    != agreement.OriginalPrincipal)
            {
                throw new ArgumentException(
                    "Loan schedule opening principal is invalid.");
            }

            if (index > 0
                && installment.OpeningPrincipal
                    != Installments[index - 1].ClosingPrincipal)
            {
                throw new ArgumentException(
                    "Loan installment balances are discontinuous.");
            }
        }

        if (Installments.Count > 0
            && Installments[^1].ClosingPrincipal != 0m)
        {
            throw new ArgumentException(
                "Loan amortization schedule must finish at zero principal.");
        }

        if (TotalPrincipal != agreement.OriginalPrincipal)
        {
            throw new ArgumentException(
                "Loan amortization schedule principal does not reconcile.");
        }
    }
}

public sealed record OwnershipFixedCostCycle(
    decimal Insurance,
    decimal Storage,
    decimal OtherFixedCosts = 0m)
{
    public void Validate()
    {
        ValidateMoney(Insurance, nameof(Insurance));
        ValidateMoney(Storage, nameof(Storage));
        ValidateMoney(OtherFixedCosts, nameof(OtherFixedCosts));
    }

    private static void ValidateMoney(
        decimal value,
        string name)
    {
        if (value < 0m
            || decimal.Round(
                value,
                2,
                MidpointRounding.AwayFromZero)
                != value)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public static class AircraftLoanAmortization
{
    public static AircraftLoanAmortizationSchedule Build(
        AircraftLoanAgreement agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        agreement.Validate();

        decimal remaining =
            agreement.OriginalPrincipal;
        decimal monthlyRate =
            agreement.AnnualRate / 12m;

        var installments =
            new List<AircraftLoanInstallment>(
                agreement.TermMonths);

        for (int number = 1;
             number <= agreement.TermMonths;
             number++)
        {
            decimal opening = remaining;
            decimal interest =
                Money(
                    opening * monthlyRate);

            decimal principal;
            decimal payment;

            if (number == agreement.TermMonths)
            {
                principal = opening;
                payment =
                    Money(principal + interest);
            }
            else
            {
                payment =
                    agreement.ScheduledMonthlyPayment;
                principal =
                    Money(payment - interest);

                if (principal <= 0m)
                {
                    throw new InvalidOperationException(
                        "Scheduled payment does not amortize the loan.");
                }

                if (principal > opening)
                {
                    principal = opening;
                    payment =
                        Money(principal + interest);
                }
            }

            decimal closing =
                Money(opening - principal);

            var installment =
                new AircraftLoanInstallment(
                    number,
                    opening,
                    interest,
                    principal,
                    payment,
                    closing);
            installment.Validate();
            installments.Add(installment);

            remaining = closing;
        }

        var result =
            new AircraftLoanAmortizationSchedule(
                agreement.LoanId,
                installments);

        result.Validate(agreement);
        return result;
    }

    public static IReadOnlyList<RecurringOwnershipCostCycle>
        BuildActivePlayCostSchedule(
            AircraftLoanAmortizationSchedule? loanSchedule,
            OwnershipFixedCostCycle fixedCosts,
            int cycleCount)
    {
        ArgumentNullException.ThrowIfNull(fixedCosts);
        fixedCosts.Validate();

        if (cycleCount < 1)
            throw new ArgumentOutOfRangeException(nameof(cycleCount));

        if (loanSchedule is not null
            && loanSchedule.Installments.Count == 0)
        {
            throw new ArgumentException(
                "Loan schedule cannot be empty when supplied.",
                nameof(loanSchedule));
        }

        var result =
            new RecurringOwnershipCostCycle[cycleCount];

        for (int index = 0;
             index < cycleCount;
             index++)
        {
            AircraftLoanInstallment? installment =
                loanSchedule is not null
                    && index < loanSchedule.Installments.Count
                    ? loanSchedule.Installments[index]
                    : null;

            result[index] =
                new RecurringOwnershipCostCycle(
                    LoanPrincipal:
                        installment?.Principal ?? 0m,
                    LoanInterest:
                        installment?.Interest ?? 0m,
                    Insurance:
                        fixedCosts.Insurance,
                    Storage:
                        fixedCosts.Storage,
                    OtherFixedCosts:
                        fixedCosts.OtherFixedCosts);

            result[index].Validate();
        }

        return result;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}
