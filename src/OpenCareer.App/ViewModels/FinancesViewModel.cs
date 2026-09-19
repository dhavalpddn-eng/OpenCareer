using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenCareer.Application.Economy;
using OpenCareer.Domain.Economy;

namespace OpenCareer.App.ViewModels;

public sealed class FinancesViewModel(
    IEconomyLedgerStore ledgerStore,
    ILogger<FinancesViewModel> logger)
    : INotifyPropertyChanged
{
    private static readonly CultureInfo CurrencyCulture =
        CultureInfo.GetCultureInfo("en-US");

    private readonly IEconomyLedgerStore _ledgerStore =
        ledgerStore ?? throw new ArgumentNullException(nameof(ledgerStore));

    private readonly ILogger<FinancesViewModel> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private string _cashBalanceText = "$0.00";
    private string _incomeText = "$0.00";
    private string _operatingCostsText = "$0.00";
    private string _aircraftAssetsText = "$0.00";
    private string _loanBalanceText = "$0.00";
    private string _statusText = "Ledger ready";
    private string _statusDetail =
        "No settled transactions have been posted yet.";
    private bool _isRefreshing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FinanceTransactionRow> RecentTransactions { get; } = [];

    public string CashBalanceText => _cashBalanceText;
    public string IncomeText => _incomeText;
    public string OperatingCostsText => _operatingCostsText;
    public string AircraftAssetsText => _aircraftAssetsText;
    public string LoanBalanceText => _loanBalanceText;
    public string StatusText => _statusText;
    public string StatusDetail => _statusDetail;
    public bool IsRefreshing => _isRefreshing;

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isRefreshing)
            return;

        SetRefreshing(true);

        try
        {
            decimal cash =
                await _ledgerStore
                    .ReadCashBalanceAsync(cancellationToken);

            IReadOnlyList<LedgerAccountBalance> accountBalances =
                await _ledgerStore
                    .ReadAccountBalancesAsync(cancellationToken);

            IReadOnlyList<EconomyLedgerTransaction> recent =
                await _ledgerStore
                    .ReadRecentAsync(
                        20,
                        cancellationToken);

            SetField(
                ref _cashBalanceText,
                cash.ToString("C2", CurrencyCulture),
                nameof(CashBalanceText));

            decimal income = NetCredit(
                accountBalances,
                LedgerAccountCode.ContractRevenue,
                LedgerAccountCode.WageIncome,
                LedgerAccountCode.ReimbursementIncome);

            decimal operatingCosts = NetDebit(
                accountBalances,
                LedgerAccountCode.FuelExpense,
                LedgerAccountCode.MaintenanceExpense,
                LedgerAccountCode.AirportFeesExpense,
                LedgerAccountCode.OtherOperatingExpense,
                LedgerAccountCode.InsuranceExpense,
                LedgerAccountCode.InterestExpense,
                LedgerAccountCode.StorageExpense);

            decimal aircraftAssets = NetDebit(
                accountBalances,
                LedgerAccountCode.AircraftAsset);

            decimal loanBalance = NetCredit(
                accountBalances,
                LedgerAccountCode.LoanPayable);

            SetField(
                ref _incomeText,
                income.ToString("C2", CurrencyCulture),
                nameof(IncomeText));
            SetField(
                ref _operatingCostsText,
                operatingCosts.ToString("C2", CurrencyCulture),
                nameof(OperatingCostsText));
            SetField(
                ref _aircraftAssetsText,
                aircraftAssets.ToString("C2", CurrencyCulture),
                nameof(AircraftAssetsText));
            SetField(
                ref _loanBalanceText,
                loanBalance.ToString("C2", CurrencyCulture),
                nameof(LoanBalanceText));

            RecentTransactions.Clear();
            foreach (EconomyLedgerTransaction transaction in recent)
            {
                RecentTransactions.Add(
                    FinanceTransactionRow.From(transaction));
            }

            if (recent.Count == 0)
            {
                SetField(
                    ref _statusText,
                    "Ledger ready",
                    nameof(StatusText));
                SetField(
                    ref _statusDetail,
                    "No settled transactions have been posted yet.",
                    nameof(StatusDetail));
            }
            else
            {
                SetField(
                    ref _statusText,
                    "Ledger synchronized",
                    nameof(StatusText));
                SetField(
                    ref _statusDetail,
                    $"{recent.Count} recent transaction(s) loaded from the local career ledger.",
                    nameof(StatusDetail));
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to refresh the finance ledger.");

            SetField(
                ref _statusText,
                "Finance data unavailable",
                nameof(StatusText));
            SetField(
                ref _statusDetail,
                "OpenCareer could not read the local economy ledger. The error was logged.",
                nameof(StatusDetail));
        }
        finally
        {
            SetRefreshing(false);
        }
    }

    private static decimal NetDebit(
        IReadOnlyList<LedgerAccountBalance> balances,
        params LedgerAccountCode[] accounts) =>
        balances
            .Where(balance => accounts.Contains(balance.Account))
            .Sum(balance => balance.NetDebit);

    private static decimal NetCredit(
        IReadOnlyList<LedgerAccountBalance> balances,
        params LedgerAccountCode[] accounts) =>
        balances
            .Where(balance => accounts.Contains(balance.Account))
            .Sum(balance => balance.NetCredit);

    private void SetRefreshing(bool value)
    {
        if (_isRefreshing == value)
            return;

        _isRefreshing = value;
        OnPropertyChanged(nameof(IsRefreshing));
    }

    private void SetField(
        ref string field,
        string value,
        string propertyName)
    {
        if (string.Equals(
            field,
            value,
            StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}

public sealed record FinanceTransactionRow(
    string Description,
    string AmountText,
    string DateText,
    string ReferenceText)
{
    private static readonly CultureInfo CurrencyCulture =
        CultureInfo.GetCultureInfo("en-US");

    public static FinanceTransactionRow From(
        EconomyLedgerTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Validate();

        decimal cashChange = transaction.CashChange;
        string amount =
            cashChange.ToString(
                "+$#,##0.00;-$#,##0.00;$0.00",
                CurrencyCulture);

        return new FinanceTransactionRow(
            transaction.Description,
            amount,
            transaction.OccurredAt
                .ToLocalTime()
                .ToString(
                    "MMM d, yyyy h:mm tt",
                    CultureInfo.CurrentCulture),
            $"{transaction.ReferenceType} • {transaction.ReferenceId}");
    }
}
