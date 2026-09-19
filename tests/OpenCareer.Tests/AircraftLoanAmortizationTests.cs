using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Finance;

namespace OpenCareer.Tests;

public sealed class AircraftLoanAmortizationTests
{
    [Fact]
    public void SixPercentTenYearScheduleMatchesIndependentReference()
    {
        AircraftLoanAgreement agreement =
            Agreement(
                principal: 80_000m,
                annualRate: 0.06m,
                termMonths: 120,
                payment: 888.17m);

        AircraftLoanAmortizationSchedule schedule =
            AircraftLoanAmortization.Build(
                agreement);

        Assert.Equal(120, schedule.Installments.Count);

        AircraftLoanInstallment first =
            schedule.Installments[0];
        Assert.Equal(400m, first.Interest);
        Assert.Equal(488.17m, first.Principal);
        Assert.Equal(888.17m, first.Payment);
        Assert.Equal(79_511.83m, first.ClosingPrincipal);

        AircraftLoanInstallment last =
            schedule.Installments[^1];
        Assert.Equal(4.41m, last.Interest);
        Assert.Equal(882.83m, last.Principal);
        Assert.Equal(887.24m, last.Payment);
        Assert.Equal(0m, last.ClosingPrincipal);

        // Independently checked with Wolfram using monthly rate 0.06/12
        // and cent-rounded installment accounting.
        Assert.Equal(80_000m, schedule.TotalPrincipal);
        Assert.Equal(26_579.47m, schedule.TotalInterest);
        Assert.Equal(106_579.47m, schedule.TotalPayments);
    }

    [Fact]
    public void ZeroRateLoanAmortizesWithoutSpecialCaseInstability()
    {
        AircraftLoanAgreement agreement =
            Agreement(
                principal: 12_000m,
                annualRate: 0m,
                termMonths: 12,
                payment: 1_000m);

        AircraftLoanAmortizationSchedule schedule =
            AircraftLoanAmortization.Build(
                agreement);

        Assert.All(
            schedule.Installments,
            installment =>
            {
                Assert.Equal(0m, installment.Interest);
                Assert.Equal(1_000m, installment.Principal);
                Assert.Equal(1_000m, installment.Payment);
            });

        Assert.Equal(0m, schedule.TotalInterest);
        Assert.Equal(12_000m, schedule.TotalPayments);
    }

    [Fact]
    public void ActivePlayCostScheduleUsesLoanComponentsThenKeepsFixedCosts()
    {
        AircraftLoanAgreement agreement =
            Agreement(
                principal: 2_000m,
                annualRate: 0m,
                termMonths: 2,
                payment: 1_000m);

        AircraftLoanAmortizationSchedule loan =
            AircraftLoanAmortization.Build(
                agreement);

        IReadOnlyList<RecurringOwnershipCostCycle> costs =
            AircraftLoanAmortization
                .BuildActivePlayCostSchedule(
                    loan,
                    new OwnershipFixedCostCycle(
                        Insurance: 50m,
                        Storage: 25m,
                        OtherFixedCosts: 5m),
                    cycleCount: 4);

        Assert.Equal(1_000m, costs[0].LoanPrincipal);
        Assert.Equal(1_000m, costs[1].LoanPrincipal);
        Assert.Equal(0m, costs[2].LoanPrincipal);
        Assert.Equal(0m, costs[3].LoanPrincipal);

        Assert.All(
            costs,
            cycle =>
            {
                Assert.Equal(50m, cycle.Insurance);
                Assert.Equal(25m, cycle.Storage);
                Assert.Equal(5m, cycle.OtherFixedCosts);
            });
    }

    [Fact]
    public void UnderpayingLoanIsRejectedAsNegativeAmortization()
    {
        AircraftLoanAgreement agreement =
            Agreement(
                principal: 100_000m,
                annualRate: 0.12m,
                termMonths: 120,
                payment: 100m);

        Assert.Throws<InvalidOperationException>(
            () =>
                AircraftLoanAmortization.Build(
                    agreement));
    }

    private static AircraftLoanAgreement Agreement(
        decimal principal,
        decimal annualRate,
        int termMonths,
        decimal payment) =>
        new(
            Guid.NewGuid(),
            "owned-1",
            "test-lender",
            principal,
            annualRate,
            termMonths,
            payment,
            new DateTimeOffset(
                2026,
                9,
                18,
                12,
                0,
                0,
                TimeSpan.Zero));
}
