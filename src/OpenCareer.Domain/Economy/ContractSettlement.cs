using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Economy;

public sealed record ContractSettlementCosts(
    decimal FuelCost,
    decimal MaintenanceReserveCost,
    decimal AirportFees,
    decimal OtherOperatingCosts = 0m)
{
    public decimal Total =>
        LedgerMoney.Normalize(
            FuelCost
            + MaintenanceReserveCost
            + AirportFees
            + OtherOperatingCosts);

    public void Validate()
    {
        LedgerMoney.Validate(
            FuelCost,
            nameof(FuelCost));

        LedgerMoney.Validate(
            MaintenanceReserveCost,
            nameof(MaintenanceReserveCost));

        LedgerMoney.Validate(
            AirportFees,
            nameof(AirportFees));

        LedgerMoney.Validate(
            OtherOperatingCosts,
            nameof(OtherOperatingCosts));
    }
}

public sealed record ContractSettlementSummary(
    Guid ContractId,
    decimal GrossCashReceipt,
    decimal PlayerOperatingCosts,
    decimal NetCashChange,
    EconomyLedgerTransaction Transaction)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(ContractId));
        }

        LedgerMoney.Validate(
            GrossCashReceipt,
            nameof(GrossCashReceipt));

        LedgerMoney.Validate(
            PlayerOperatingCosts,
            nameof(PlayerOperatingCosts));

        if (LedgerMoney.Normalize(
                GrossCashReceipt - PlayerOperatingCosts)
            != NetCashChange)
        {
            throw new ArgumentException(
                "Settlement cash summary does not reconcile.");
        }

        ArgumentNullException.ThrowIfNull(Transaction);
        Transaction.Validate();

        if (Transaction.TransactionId != ContractId
            || Transaction.CashChange != NetCashChange)
        {
            throw new ArgumentException(
                "Settlement transaction does not reconcile to the contract summary.");
        }
    }
}

public static class ContractSettlementEngine
{
    public static string GetIdempotencyKey(
        Guid contractId)
    {
        if (contractId == Guid.Empty)
        {
            throw new ArgumentException(
                "Contract ID is required.",
                nameof(contractId));
        }

        return $"contract:{contractId:D}:settlement-v1";
    }

    public static ContractSettlementSummary Create(
        JobContract contract,
        ContractSettlementCosts actualCosts,
        DateTimeOffset settledAt)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(actualCosts);

        contract.Validate();
        actualCosts.Validate();

        if (contract.Status != ContractStatus.Completed
            || contract.CompletedAt is not { } completedAt)
        {
            throw new InvalidOperationException(
                "Only a completed contract can be settled.");
        }

        if (settledAt < completedAt)
        {
            throw new ArgumentException(
                "Settlement cannot precede verified contract completion.",
                nameof(settledAt));
        }

        ContractCompensation compensation =
            contract.Compensation;

        decimal grossReceipt =
            LedgerMoney.Normalize(
                compensation.Model switch
                {
                    CompensationModel.CompanyRevenue =>
                        compensation.GrossCustomerRevenue,
                    CompensationModel.MissionFee =>
                        compensation.GrossCustomerRevenue > 0m
                            ? compensation.GrossCustomerRevenue
                            : compensation.PilotCompensation,
                    CompensationModel.PilotWage
                        or CompensationModel.SalaryDuty
                        or CompensationModel.Reimbursement =>
                        compensation.PilotCompensation,
                    _ =>
                        throw new ArgumentOutOfRangeException(
                            nameof(contract),
                            "Unsupported compensation model.")
                });

        decimal fuelCost =
            compensation.EmployerCoversFuel
                ? 0m
                : LedgerMoney.Normalize(
                    actualCosts.FuelCost);

        decimal maintenanceCost =
            compensation.EmployerCoversMaintenance
                ? 0m
                : LedgerMoney.Normalize(
                    actualCosts.MaintenanceReserveCost);

        decimal airportFees =
            compensation.EmployerCoversAirportFees
                ? 0m
                : LedgerMoney.Normalize(
                    actualCosts.AirportFees);

        decimal otherCosts =
            LedgerMoney.Normalize(
                actualCosts.OtherOperatingCosts);

        decimal playerCosts =
            LedgerMoney.Normalize(
                fuelCost
                + maintenanceCost
                + airportFees
                + otherCosts);

        var postings =
            new List<LedgerPosting>();

        if (grossReceipt > 0m)
        {
            postings.Add(
                LedgerPosting.DebitTo(
                    LedgerAccountCode.Cash,
                    grossReceipt,
                    "Contract cash receipt"));

            postings.Add(
                LedgerPosting.CreditTo(
                    IncomeAccountFor(
                        compensation.Model),
                    grossReceipt,
                    "Contract income"));
        }

        AddExpense(
            postings,
            LedgerAccountCode.FuelExpense,
            fuelCost,
            "Fuel cost");

        AddExpense(
            postings,
            LedgerAccountCode.MaintenanceExpense,
            maintenanceCost,
            "Maintenance reserve");

        AddExpense(
            postings,
            LedgerAccountCode.AirportFeesExpense,
            airportFees,
            "Airport and navigation fees");

        AddExpense(
            postings,
            LedgerAccountCode.OtherOperatingExpense,
            otherCosts,
            "Other operating costs");

        var transaction =
            new EconomyLedgerTransaction(
                TransactionId:
                    contract.ContractId,
                IdempotencyKey:
                    GetIdempotencyKey(
                        contract.ContractId),
                OccurredAt:
                    settledAt,
                Description:
                    $"{contract.Kind} contract settlement {contract.OriginIcao}-{contract.DestinationIcao}",
                ReferenceType:
                    nameof(JobContract),
                ReferenceId:
                    contract.ContractId.ToString("D"),
                Postings:
                    postings);

        transaction.Validate();

        var summary =
            new ContractSettlementSummary(
                contract.ContractId,
                grossReceipt,
                playerCosts,
                LedgerMoney.Normalize(
                    grossReceipt - playerCosts),
                transaction);

        summary.Validate();
        return summary;
    }

    private static LedgerAccountCode IncomeAccountFor(
        CompensationModel model) =>
        model switch
        {
            CompensationModel.PilotWage
                or CompensationModel.SalaryDuty =>
                LedgerAccountCode.WageIncome,
            CompensationModel.Reimbursement =>
                LedgerAccountCode.ReimbursementIncome,
            CompensationModel.MissionFee
                or CompensationModel.CompanyRevenue =>
                LedgerAccountCode.ContractRevenue,
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(model))
        };

    private static void AddExpense(
        ICollection<LedgerPosting> postings,
        LedgerAccountCode expenseAccount,
        decimal amount,
        string memo)
    {
        if (amount <= 0m)
            return;

        postings.Add(
            LedgerPosting.DebitTo(
                expenseAccount,
                amount,
                memo));

        postings.Add(
            LedgerPosting.CreditTo(
                LedgerAccountCode.Cash,
                amount,
                memo));
    }
}
