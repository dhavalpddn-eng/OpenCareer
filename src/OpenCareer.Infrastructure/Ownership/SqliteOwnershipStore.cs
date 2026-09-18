using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenCareer.Application.Ownership;
using OpenCareer.Domain.Dealers;
using OpenCareer.Domain.Maintenance;
using OpenCareer.Domain.Ownership;

namespace OpenCareer.Infrastructure.Ownership;

public sealed class SqliteOwnershipStore : IOwnershipStore
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteOwnershipStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS career_accounts (
                career_id TEXT PRIMARY KEY,
                cash_cents INTEGER NOT NULL CHECK(cash_cents >= 0),
                reserve_cents INTEGER NOT NULL CHECK(reserve_cents >= 0)
            );

            CREATE TABLE IF NOT EXISTS dealer_stock (
                listing_id TEXT PRIMARY KEY,
                dealer_id TEXT NOT NULL,
                aircraft_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                condition INTEGER NOT NULL,
                asking_cents INTEGER NOT NULL,
                appraised_cents INTEGER NOT NULL,
                condition_percent REAL NOT NULL,
                civilian_authorized INTEGER NOT NULL,
                sold_operation_id TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS dealer_offers (
                offer_key TEXT PRIMARY KEY,
                dealer_id TEXT NOT NULL,
                listing_id TEXT NOT NULL,
                aircraft_id TEXT NOT NULL,
                condition INTEGER NOT NULL,
                list_price_cents INTEGER NOT NULL,
                discount_rate REAL NOT NULL,
                sale_price_cents INTEGER NOT NULL,
                issued_at TEXT NOT NULL,
                expires_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS storage_slots (
                slot_id TEXT PRIMARY KEY,
                airport_icao TEXT NOT NULL,
                storage_class INTEGER NOT NULL,
                monthly_cost_cents INTEGER NOT NULL,
                expires_at TEXT NOT NULL,
                available INTEGER NOT NULL,
                occupied_ownership_id TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS owned_aircraft (
                ownership_id TEXT PRIMARY KEY,
                career_id TEXT NOT NULL,
                aircraft_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                source_listing_id TEXT NOT NULL UNIQUE,
                purchase_price_cents INTEGER NOT NULL,
                acquired_condition_percent REAL NOT NULL,
                current_airport_icao TEXT NOT NULL,
                acquired_at TEXT NOT NULL,
                status INTEGER NOT NULL,
                FOREIGN KEY(career_id) REFERENCES career_accounts(career_id)
            );

            CREATE TABLE IF NOT EXISTS aircraft_loans (
                loan_id TEXT PRIMARY KEY,
                career_id TEXT NOT NULL,
                ownership_id TEXT NOT NULL UNIQUE,
                lender_id TEXT NOT NULL,
                original_principal_cents INTEGER NOT NULL,
                remaining_principal_cents INTEGER NOT NULL,
                annual_rate REAL NOT NULL,
                term_months INTEGER NOT NULL,
                monthly_payment_cents INTEGER NOT NULL,
                originated_at TEXT NOT NULL,
                next_payment_due_at TEXT NOT NULL,
                payments_made INTEGER NOT NULL,
                status INTEGER NOT NULL,
                FOREIGN KEY(career_id) REFERENCES career_accounts(career_id),
                FOREIGN KEY(ownership_id) REFERENCES owned_aircraft(ownership_id)
            );

            CREATE TABLE IF NOT EXISTS loan_schedule (
                loan_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                due_at TEXT NOT NULL,
                payment_cents INTEGER NOT NULL,
                principal_cents INTEGER NOT NULL,
                interest_cents INTEGER NOT NULL,
                remaining_principal_cents INTEGER NOT NULL,
                PRIMARY KEY(loan_id, sequence),
                FOREIGN KEY(loan_id) REFERENCES aircraft_loans(loan_id)
            );

            CREATE TABLE IF NOT EXISTS insurance_policies (
                policy_id TEXT PRIMARY KEY,
                ownership_id TEXT NOT NULL UNIQUE,
                plan_id TEXT NOT NULL,
                plan_name TEXT NOT NULL,
                monthly_premium_cents INTEGER NOT NULL,
                deductible_cents INTEGER NOT NULL,
                hull_coverage REAL NOT NULL,
                daily_redo_allowance INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                last_redo_day TEXT NULL,
                active INTEGER NOT NULL,
                FOREIGN KEY(ownership_id) REFERENCES owned_aircraft(ownership_id)
            );

            CREATE TABLE IF NOT EXISTS storage_leases (
                lease_id TEXT PRIMARY KEY,
                ownership_id TEXT NOT NULL UNIQUE,
                slot_id TEXT NOT NULL UNIQUE,
                airport_icao TEXT NOT NULL,
                storage_class INTEGER NOT NULL,
                monthly_cost_cents INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                active INTEGER NOT NULL,
                FOREIGN KEY(ownership_id) REFERENCES owned_aircraft(ownership_id)
            );

            CREATE TABLE IF NOT EXISTS maintenance_state (
                ownership_id TEXT PRIMARY KEY,
                tracked_airframe_hours REAL NOT NULL,
                tracked_engine_hours REAL NOT NULL,
                landing_cycles INTEGER NOT NULL,
                airframe_wear REAL NOT NULL,
                engine_wear REAL NOT NULL,
                gear_wear REAL NOT NULL,
                damage REAL NOT NULL,
                next_inspection_hours REAL NOT NULL,
                confidence INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(ownership_id) REFERENCES owned_aircraft(ownership_id)
            );

            CREATE TABLE IF NOT EXISTS maintenance_events (
                operation_id TEXT PRIMARY KEY,
                ownership_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                cost_cents INTEGER NOT NULL,
                recorded_at TEXT NOT NULL,
                FOREIGN KEY(ownership_id) REFERENCES owned_aircraft(ownership_id)
            );

            CREATE TABLE IF NOT EXISTS purchase_operations (
                operation_id TEXT PRIMARY KEY,
                career_id TEXT NOT NULL,
                listing_id TEXT NOT NULL,
                ownership_id TEXT NOT NULL UNIQUE,
                loan_id TEXT NULL,
                purchase_price_cents INTEGER NOT NULL,
                cash_debit_cents INTEGER NOT NULL,
                purchased_at TEXT NOT NULL,
                FOREIGN KEY(career_id) REFERENCES career_accounts(career_id),
                FOREIGN KEY(ownership_id) REFERENCES owned_aircraft(ownership_id)
            );

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetCareerAccountAsync(
        CareerAccountSnapshot account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.CareerId);
        if (account.CashBalance < 0 || account.OperatingReserve < 0 || account.OperatingReserve > account.CashBalance)
            throw new ArgumentOutOfRangeException(nameof(account));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_accounts(career_id, cash_cents, reserve_cents)
            VALUES($career, $cash, $reserve)
            ON CONFLICT(career_id) DO UPDATE SET
                cash_cents = excluded.cash_cents,
                reserve_cents = excluded.reserve_cents;
            """;
        command.Parameters.AddWithValue("$career", account.CareerId);
        command.Parameters.AddWithValue("$cash", ToCents(account.CashBalance));
        command.Parameters.AddWithValue("$reserve", ToCents(account.OperatingReserve));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertDealerStockAsync(
        string dealerId,
        DealerStock stock,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dealerId);
        ArgumentNullException.ThrowIfNull(stock);
        stock.Validate();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dealer_stock(
                listing_id, dealer_id, aircraft_id, display_name, condition,
                asking_cents, appraised_cents, condition_percent, civilian_authorized, sold_operation_id)
            VALUES($listing, $dealer, $aircraft, $display, $condition, $asking, $appraised, $conditionPercent, $authorized, NULL)
            ON CONFLICT(listing_id) DO UPDATE SET
                dealer_id = excluded.dealer_id,
                aircraft_id = excluded.aircraft_id,
                display_name = excluded.display_name,
                condition = excluded.condition,
                asking_cents = excluded.asking_cents,
                appraised_cents = excluded.appraised_cents,
                condition_percent = excluded.condition_percent,
                civilian_authorized = excluded.civilian_authorized
            WHERE dealer_stock.sold_operation_id IS NULL;
            """;
        command.Parameters.AddWithValue("$listing", stock.ListingId);
        command.Parameters.AddWithValue("$dealer", dealerId);
        command.Parameters.AddWithValue("$aircraft", stock.Aircraft.AircraftId);
        command.Parameters.AddWithValue("$display", stock.Aircraft.DisplayName);
        command.Parameters.AddWithValue("$condition", (int)stock.Condition);
        command.Parameters.AddWithValue("$asking", ToCents(stock.AskingPrice));
        command.Parameters.AddWithValue("$appraised", ToCents(stock.AppraisedValue));
        command.Parameters.AddWithValue("$conditionPercent", (double)stock.ConditionPercent);
        command.Parameters.AddWithValue("$authorized", stock.CivilianSaleAuthorized ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveDealerOfferAsync(
        DealerOffer offer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.DealerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.ListingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.AircraftId);
        if (offer.ListPrice <= 0 || offer.SalePrice <= 0 || offer.SalePrice > offer.ListPrice
            || offer.DiscountRate is < 0 or > 0.25m || offer.ExpiresAt <= offer.IssuedAt)
            throw new ArgumentException("Invalid dealer offer.", nameof(offer));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dealer_offers(
                offer_key, dealer_id, listing_id, aircraft_id, condition, list_price_cents,
                discount_rate, sale_price_cents, issued_at, expires_at)
            VALUES($key, $dealer, $listing, $aircraft, $condition, $listPrice, $discount, $salePrice, $issuedAt, $expiresAt)
            ON CONFLICT(offer_key) DO UPDATE SET
                list_price_cents = excluded.list_price_cents,
                discount_rate = excluded.discount_rate,
                sale_price_cents = excluded.sale_price_cents,
                expires_at = excluded.expires_at;
            """;
        command.Parameters.AddWithValue("$key", OfferKey(offer));
        command.Parameters.AddWithValue("$dealer", offer.DealerId);
        command.Parameters.AddWithValue("$listing", offer.ListingId);
        command.Parameters.AddWithValue("$aircraft", offer.AircraftId);
        command.Parameters.AddWithValue("$condition", (int)offer.Condition);
        command.Parameters.AddWithValue("$listPrice", ToCents(offer.ListPrice));
        command.Parameters.AddWithValue("$discount", (double)offer.DiscountRate);
        command.Parameters.AddWithValue("$salePrice", ToCents(offer.SalePrice));
        command.Parameters.AddWithValue("$issuedAt", ToTimestamp(offer.IssuedAt));
        command.Parameters.AddWithValue("$expiresAt", ToTimestamp(offer.ExpiresAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertStorageOfferAsync(
        AircraftStorageOffer offer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.SlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.AirportIcao);
        if (!Enum.IsDefined(offer.StorageClass) || offer.MonthlyCost < 0)
            throw new ArgumentException("Invalid storage offer.", nameof(offer));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO storage_slots(
                slot_id, airport_icao, storage_class, monthly_cost_cents, expires_at, available, occupied_ownership_id)
            VALUES($slot, $airport, $class, $cost, $expires, $available, NULL)
            ON CONFLICT(slot_id) DO UPDATE SET
                airport_icao = excluded.airport_icao,
                storage_class = excluded.storage_class,
                monthly_cost_cents = excluded.monthly_cost_cents,
                expires_at = excluded.expires_at,
                available = excluded.available
            WHERE storage_slots.occupied_ownership_id IS NULL;
            """;
        command.Parameters.AddWithValue("$slot", offer.SlotId);
        command.Parameters.AddWithValue("$airport", offer.AirportIcao);
        command.Parameters.AddWithValue("$class", (int)offer.StorageClass);
        command.Parameters.AddWithValue("$cost", ToCents(offer.MonthlyCost));
        command.Parameters.AddWithValue("$expires", ToTimestamp(offer.ExpiresAt));
        command.Parameters.AddWithValue("$available", offer.Available ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AircraftPurchaseReceipt> ExecutePurchaseAsync(
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existing = await TryReadReceiptAsync(connection, transaction, plan, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        var account = await ReadAccountAsync(connection, transaction, plan.CareerId, cancellationToken);
        await VerifyStockAsync(connection, transaction, plan, cancellationToken);
        await VerifyOfferAsync(connection, transaction, plan, cancellationToken);
        await VerifyStorageAsync(connection, transaction, plan, cancellationToken);

        if (account.CashBalance - plan.CashDebit < account.OperatingReserve)
            throw new InvalidOperationException("Purchase would violate the persisted operating reserve.");

        var owned = plan.CreateOwnedAircraft();
        owned.Validate();
        var insurance = plan.CreateInsurancePolicy();
        var storageLease = plan.CreateStorageLease();
        var loan = plan.CreateLoan();
        var maintenance = AircraftMaintenanceEngine.CreateInitial(
            plan.OwnershipId,
            plan.Stock.ConditionPercent,
            plan.MaintenanceProgram,
            plan.PurchasedAt);

        await UpdateAccountCashAsync(
            connection,
            transaction,
            plan.CareerId,
            account.CashBalance - plan.CashDebit,
            cancellationToken);

        await MarkStockSoldAsync(connection, transaction, plan, cancellationToken);
        await OccupyStorageAsync(connection, transaction, plan, cancellationToken);
        await InsertOwnedAircraftAsync(connection, transaction, owned, cancellationToken);
        await InsertInsuranceAsync(connection, transaction, insurance, cancellationToken);
        await InsertStorageLeaseAsync(connection, transaction, storageLease, cancellationToken);
        await InsertMaintenanceStateAsync(connection, transaction, maintenance, cancellationToken);

        if (loan is not null)
            await InsertLoanAsync(connection, transaction, loan, cancellationToken);

        var receipt = new AircraftPurchaseReceipt(
            plan.OperationId,
            plan.OwnershipId,
            plan.LoanId,
            plan.Offer.SalePrice,
            plan.CashDebit,
            plan.PurchasedAt);

        await InsertPurchaseOperationAsync(connection, transaction, plan, receipt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return receipt;
    }

    public async Task<OwnershipSnapshot> LoadSnapshotAsync(
        string careerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(careerId);

        await using var connection = await OpenAsync(cancellationToken);
        var account = await ReadAccountAsync(connection, null, careerId, cancellationToken);
        var aircraft = await ReadOwnedAircraftAsync(connection, careerId, cancellationToken);
        var loans = await ReadLoansAsync(connection, careerId, cancellationToken);
        var insurance = await ReadInsuranceAsync(connection, careerId, cancellationToken);
        var storage = await ReadStorageLeasesAsync(connection, careerId, cancellationToken);
        var maintenance = await ReadMaintenanceStatesAsync(connection, careerId, cancellationToken);

        return new OwnershipSnapshot(account, aircraft, loans, insurance, storage, maintenance);
    }

    public async Task<ImmutableArray<LoanScheduleItem>> LoadLoanScheduleAsync(
        string loanId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loanId);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, due_at, payment_cents, principal_cents, interest_cents, remaining_principal_cents
            FROM loan_schedule
            WHERE loan_id = $loan
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$loan", loanId);

        var result = ImmutableArray.CreateBuilder<LoanScheduleItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetInt32(0),
                ParseTimestamp(reader.GetString(1)),
                FromCents(reader.GetInt64(2)),
                FromCents(reader.GetInt64(3)),
                FromCents(reader.GetInt64(4)),
                FromCents(reader.GetInt64(5))));
        }
        return result.ToImmutable();
    }

    public async Task<bool> TryUseInsuranceRedoAsync(
        string policyId,
        DateOnly realLocalDay,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT daily_redo_allowance, last_redo_day, active
            FROM insurance_policies
            WHERE policy_id = $policy;
            """;
        read.Parameters.AddWithValue("$policy", policyId);

        int allowance;
        string? lastDay;
        bool active;
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Insurance policy does not exist.");
            allowance = reader.GetInt32(0);
            lastDay = reader.IsDBNull(1) ? null : reader.GetString(1);
            active = reader.GetInt32(2) != 0;
        }

        var dayText = realLocalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!active || allowance <= 0 || string.Equals(lastDay, dayText, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE insurance_policies SET last_redo_day = $day WHERE policy_id = $policy;";
        update.Parameters.AddWithValue("$day", dayText);
        update.Parameters.AddWithValue("$policy", policyId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<AircraftMaintenanceState> RecordMaintenanceUsageAsync(
        string ownershipId,
        string operationId,
        MaintenanceProgram program,
        MaintenanceUsage usage,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(usage);

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        if (await MaintenanceOperationExistsAsync(connection, transaction, operationId, cancellationToken))
        {
            var unchanged = await ReadMaintenanceStateAsync(connection, transaction, ownershipId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return unchanged;
        }

        var current = await ReadMaintenanceStateAsync(connection, transaction, ownershipId, cancellationToken);
        var next = AircraftMaintenanceEngine.ApplyUsage(current, program, usage, at);
        await UpdateMaintenanceStateAsync(connection, transaction, next, cancellationToken);
        await InsertMaintenanceEventAsync(connection, transaction, operationId, ownershipId, "usage", 0m, at, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    public async Task<AircraftMaintenanceState> CompleteMaintenanceServiceAsync(
        string ownershipId,
        string operationId,
        MaintenanceProgram program,
        MaintenanceServiceQuote quote,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(quote);

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        if (await MaintenanceOperationExistsAsync(connection, transaction, operationId, cancellationToken))
        {
            var unchanged = await ReadMaintenanceStateAsync(connection, transaction, ownershipId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return unchanged;
        }

        var current = await ReadMaintenanceStateAsync(connection, transaction, ownershipId, cancellationToken);
        var careerId = await ReadCareerIdForAircraftAsync(connection, transaction, ownershipId, cancellationToken);
        var account = await ReadAccountAsync(connection, transaction, careerId, cancellationToken);

        if (account.CashBalance < quote.Cost)
            throw new InvalidOperationException("Insufficient cash for maintenance service.");

        var next = AircraftMaintenanceEngine.CompleteService(current, program, quote, completedAt);
        await UpdateAccountCashAsync(connection, transaction, careerId, account.CashBalance - quote.Cost, cancellationToken);
        await UpdateMaintenanceStateAsync(connection, transaction, next, cancellationToken);
        await InsertMaintenanceEventAsync(connection, transaction, operationId, ownershipId, "service", quote.Cost, completedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<CareerAccountSnapshot> ReadAccountAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string careerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT cash_cents, reserve_cents FROM career_accounts WHERE career_id = $career;";
        command.Parameters.AddWithValue("$career", careerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Career account does not exist.");
        return new CareerAccountSnapshot(careerId, FromCents(reader.GetInt64(0)), FromCents(reader.GetInt64(1)));
    }

    private static async Task<AircraftPurchaseReceipt?> TryReadReceiptAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT career_id, listing_id, ownership_id, loan_id, purchase_price_cents, cash_debit_cents, purchased_at
            FROM purchase_operations
            WHERE operation_id = $operation;
            """;
        command.Parameters.AddWithValue("$operation", plan.OperationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var career = reader.GetString(0);
        var listing = reader.GetString(1);
        var ownership = reader.GetString(2);
        var loan = reader.IsDBNull(3) ? null : reader.GetString(3);

        if (career != plan.CareerId || listing != plan.Stock.ListingId || ownership != plan.OwnershipId || loan != plan.LoanId)
            throw new InvalidOperationException("Purchase operation ID was already used for a different transaction.");

        return new AircraftPurchaseReceipt(
            plan.OperationId,
            ownership,
            loan,
            FromCents(reader.GetInt64(4)),
            FromCents(reader.GetInt64(5)),
            ParseTimestamp(reader.GetString(6)));
    }

    private static async Task VerifyStockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT dealer_id, aircraft_id, condition, asking_cents, appraised_cents, condition_percent,
                   civilian_authorized, sold_operation_id
            FROM dealer_stock
            WHERE listing_id = $listing;
            """;
        command.Parameters.AddWithValue("$listing", plan.Stock.ListingId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Dealer listing does not exist.");

        var sold = reader.IsDBNull(7) ? null : reader.GetString(7);
        if (sold is not null)
            throw new InvalidOperationException("Dealer listing has already been sold.");

        if (reader.GetString(0) != plan.Offer.DealerId
            || reader.GetString(1) != plan.Stock.Aircraft.AircraftId
            || reader.GetInt32(2) != (int)plan.Stock.Condition
            || FromCents(reader.GetInt64(3)) != plan.Stock.AskingPrice
            || FromCents(reader.GetInt64(4)) != plan.Stock.AppraisedValue
            || Math.Abs(reader.GetDouble(5) - (double)plan.Stock.ConditionPercent) > 0.0001
            || reader.GetInt32(6) == 0)
        {
            throw new InvalidOperationException("Persisted dealer listing does not match the purchase plan.");
        }
    }

    private static async Task VerifyOfferAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT dealer_id, listing_id, aircraft_id, condition, list_price_cents, sale_price_cents, issued_at, expires_at
            FROM dealer_offers
            WHERE offer_key = $key;
            """;
        command.Parameters.AddWithValue("$key", plan.OfferKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Persisted dealer offer does not exist.");

        var issued = ParseTimestamp(reader.GetString(6));
        var expires = ParseTimestamp(reader.GetString(7));
        if (reader.GetString(0) != plan.Offer.DealerId
            || reader.GetString(1) != plan.Offer.ListingId
            || reader.GetString(2) != plan.Offer.AircraftId
            || reader.GetInt32(3) != (int)plan.Offer.Condition
            || FromCents(reader.GetInt64(4)) != plan.Offer.ListPrice
            || FromCents(reader.GetInt64(5)) != plan.Offer.SalePrice
            || issued != plan.Offer.IssuedAt
            || expires != plan.Offer.ExpiresAt
            || plan.PurchasedAt < issued
            || plan.PurchasedAt >= expires)
        {
            throw new InvalidOperationException("Persisted dealer offer does not match or has expired.");
        }
    }

    private static async Task VerifyStorageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT airport_icao, storage_class, monthly_cost_cents, expires_at, available, occupied_ownership_id
            FROM storage_slots
            WHERE slot_id = $slot;
            """;
        command.Parameters.AddWithValue("$slot", plan.Storage.SlotId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Storage slot does not exist.");

        var occupied = reader.IsDBNull(5) ? null : reader.GetString(5);
        if (reader.GetString(0) != plan.Storage.AirportIcao
            || reader.GetInt32(1) != (int)plan.RequiredStorageClass
            || FromCents(reader.GetInt64(2)) != plan.Storage.MonthlyCost
            || ParseTimestamp(reader.GetString(3)) <= plan.PurchasedAt
            || reader.GetInt32(4) == 0
            || occupied is not null)
        {
            throw new InvalidOperationException("Compatible storage is no longer available.");
        }
    }

    private static async Task UpdateAccountCashAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string careerId,
        decimal newBalance,
        CancellationToken cancellationToken)
    {
        if (newBalance < 0)
            throw new InvalidOperationException("Career account cannot become negative.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE career_accounts SET cash_cents = $cash WHERE career_id = $career;";
        command.Parameters.AddWithValue("$cash", ToCents(newBalance));
        command.Parameters.AddWithValue("$career", careerId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Career account update failed.");
    }

    private static async Task MarkStockSoldAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dealer_stock
            SET sold_operation_id = $operation
            WHERE listing_id = $listing AND sold_operation_id IS NULL;
            """;
        command.Parameters.AddWithValue("$operation", plan.OperationId);
        command.Parameters.AddWithValue("$listing", plan.Stock.ListingId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Dealer stock was consumed concurrently.");
    }

    private static async Task OccupyStorageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE storage_slots
            SET available = 0, occupied_ownership_id = $ownership
            WHERE slot_id = $slot AND available = 1 AND occupied_ownership_id IS NULL;
            """;
        command.Parameters.AddWithValue("$ownership", plan.OwnershipId);
        command.Parameters.AddWithValue("$slot", plan.Storage.SlotId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Storage slot was consumed concurrently.");
    }

    private static async Task InsertOwnedAircraftAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OwnedAircraft aircraft,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO owned_aircraft(
                ownership_id, career_id, aircraft_id, display_name, source_listing_id, purchase_price_cents,
                acquired_condition_percent, current_airport_icao, acquired_at, status)
            VALUES($ownership, $career, $aircraft, $display, $listing, $price, $condition, $airport, $acquiredAt, $status);
            """;
        command.Parameters.AddWithValue("$ownership", aircraft.OwnershipId);
        command.Parameters.AddWithValue("$career", aircraft.CareerId);
        command.Parameters.AddWithValue("$aircraft", aircraft.AircraftId);
        command.Parameters.AddWithValue("$display", aircraft.DisplayName);
        command.Parameters.AddWithValue("$listing", aircraft.SourceListingId);
        command.Parameters.AddWithValue("$price", ToCents(aircraft.PurchasePrice));
        command.Parameters.AddWithValue("$condition", (double)aircraft.AcquiredConditionPercent);
        command.Parameters.AddWithValue("$airport", aircraft.CurrentAirportIcao);
        command.Parameters.AddWithValue("$acquiredAt", ToTimestamp(aircraft.AcquiredAt));
        command.Parameters.AddWithValue("$status", (int)aircraft.Status);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertInsuranceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftInsurancePolicy policy,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO insurance_policies(
                policy_id, ownership_id, plan_id, plan_name, monthly_premium_cents, deductible_cents,
                hull_coverage, daily_redo_allowance, started_at, last_redo_day, active)
            VALUES($policy, $ownership, $plan, $name, $premium, $deductible, $coverage, $redo, $started, NULL, 1);
            """;
        command.Parameters.AddWithValue("$policy", policy.PolicyId);
        command.Parameters.AddWithValue("$ownership", policy.OwnershipId);
        command.Parameters.AddWithValue("$plan", policy.Plan.Id);
        command.Parameters.AddWithValue("$name", policy.Plan.Name);
        command.Parameters.AddWithValue("$premium", ToCents(policy.Plan.MonthlyPremium));
        command.Parameters.AddWithValue("$deductible", ToCents(policy.Plan.Deductible));
        command.Parameters.AddWithValue("$coverage", (double)policy.Plan.HullCoveragePercent);
        command.Parameters.AddWithValue("$redo", policy.Plan.DailyRedoAllowance);
        command.Parameters.AddWithValue("$started", ToTimestamp(policy.StartedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStorageLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftStorageLease lease,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO storage_leases(
                lease_id, ownership_id, slot_id, airport_icao, storage_class, monthly_cost_cents, started_at, active)
            VALUES($lease, $ownership, $slot, $airport, $class, $cost, $started, 1);
            """;
        command.Parameters.AddWithValue("$lease", lease.LeaseId);
        command.Parameters.AddWithValue("$ownership", lease.OwnershipId);
        command.Parameters.AddWithValue("$slot", lease.SlotId);
        command.Parameters.AddWithValue("$airport", lease.AirportIcao);
        command.Parameters.AddWithValue("$class", (int)lease.StorageClass);
        command.Parameters.AddWithValue("$cost", ToCents(lease.MonthlyCost));
        command.Parameters.AddWithValue("$started", ToTimestamp(lease.StartedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLoanAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftLoanAccount loan,
        CancellationToken cancellationToken)
    {
        loan.Validate();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO aircraft_loans(
                loan_id, career_id, ownership_id, lender_id, original_principal_cents, remaining_principal_cents,
                annual_rate, term_months, monthly_payment_cents, originated_at, next_payment_due_at, payments_made, status)
            VALUES($loan, $career, $ownership, $lender, $original, $remaining, $rate, $term, $payment, $originated, $nextDue, $made, $status);
            """;
        command.Parameters.AddWithValue("$loan", loan.LoanId);
        command.Parameters.AddWithValue("$career", loan.CareerId);
        command.Parameters.AddWithValue("$ownership", loan.OwnershipId);
        command.Parameters.AddWithValue("$lender", loan.LenderId);
        command.Parameters.AddWithValue("$original", ToCents(loan.OriginalPrincipal));
        command.Parameters.AddWithValue("$remaining", ToCents(loan.RemainingPrincipal));
        command.Parameters.AddWithValue("$rate", (double)loan.AnnualRate);
        command.Parameters.AddWithValue("$term", loan.TermMonths);
        command.Parameters.AddWithValue("$payment", ToCents(loan.MonthlyPayment));
        command.Parameters.AddWithValue("$originated", ToTimestamp(loan.OriginatedAt));
        command.Parameters.AddWithValue("$nextDue", ToTimestamp(loan.NextPaymentDueAt));
        command.Parameters.AddWithValue("$made", loan.PaymentsMade);
        command.Parameters.AddWithValue("$status", (int)loan.Status);
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var item in loan.BuildSchedule())
        {
            await using var schedule = connection.CreateCommand();
            schedule.Transaction = transaction;
            schedule.CommandText = """
                INSERT INTO loan_schedule(
                    loan_id, sequence, due_at, payment_cents, principal_cents, interest_cents, remaining_principal_cents)
                VALUES($loan, $sequence, $due, $payment, $principal, $interest, $remaining);
                """;
            schedule.Parameters.AddWithValue("$loan", loan.LoanId);
            schedule.Parameters.AddWithValue("$sequence", item.Sequence);
            schedule.Parameters.AddWithValue("$due", ToTimestamp(item.DueAt));
            schedule.Parameters.AddWithValue("$payment", ToCents(item.Payment));
            schedule.Parameters.AddWithValue("$principal", ToCents(item.Principal));
            schedule.Parameters.AddWithValue("$interest", ToCents(item.Interest));
            schedule.Parameters.AddWithValue("$remaining", ToCents(item.RemainingPrincipal));
            await schedule.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertMaintenanceStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftMaintenanceState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO maintenance_state(
                ownership_id, tracked_airframe_hours, tracked_engine_hours, landing_cycles, airframe_wear,
                engine_wear, gear_wear, damage, next_inspection_hours, confidence, updated_at)
            VALUES($ownership, $airframeHours, $engineHours, $cycles, $airframeWear, $engineWear, $gearWear, $damage, $nextInspection, $confidence, $updated);
            """;
        BindMaintenance(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPurchaseOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftPurchasePlan plan,
        AircraftPurchaseReceipt receipt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO purchase_operations(
                operation_id, career_id, listing_id, ownership_id, loan_id, purchase_price_cents, cash_debit_cents, purchased_at)
            VALUES($operation, $career, $listing, $ownership, $loan, $price, $cash, $purchased);
            """;
        command.Parameters.AddWithValue("$operation", receipt.OperationId);
        command.Parameters.AddWithValue("$career", plan.CareerId);
        command.Parameters.AddWithValue("$listing", plan.Stock.ListingId);
        command.Parameters.AddWithValue("$ownership", receipt.OwnershipId);
        command.Parameters.AddWithValue("$loan", (object?)receipt.LoanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$price", ToCents(receipt.PurchasePrice));
        command.Parameters.AddWithValue("$cash", ToCents(receipt.CashDebit));
        command.Parameters.AddWithValue("$purchased", ToTimestamp(receipt.PurchasedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ImmutableArray<OwnedAircraft>> ReadOwnedAircraftAsync(
        SqliteConnection connection,
        string careerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ownership_id, aircraft_id, display_name, source_listing_id, purchase_price_cents,
                   acquired_condition_percent, current_airport_icao, acquired_at, status
            FROM owned_aircraft
            WHERE career_id = $career
            ORDER BY acquired_at, ownership_id;
            """;
        command.Parameters.AddWithValue("$career", careerId);

        var result = ImmutableArray.CreateBuilder<OwnedAircraft>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetString(0),
                careerId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                FromCents(reader.GetInt64(4)),
                (decimal)reader.GetDouble(5),
                reader.GetString(6),
                ParseTimestamp(reader.GetString(7)),
                (OwnedAircraftStatus)reader.GetInt32(8)));
        }
        return result.ToImmutable();
    }

    private static async Task<ImmutableArray<AircraftLoanAccount>> ReadLoansAsync(
        SqliteConnection connection,
        string careerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT loan_id, ownership_id, lender_id, original_principal_cents, remaining_principal_cents,
                   annual_rate, term_months, monthly_payment_cents, originated_at, next_payment_due_at, payments_made, status
            FROM aircraft_loans
            WHERE career_id = $career
            ORDER BY originated_at, loan_id;
            """;
        command.Parameters.AddWithValue("$career", careerId);

        var result = ImmutableArray.CreateBuilder<AircraftLoanAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetString(0),
                careerId,
                reader.GetString(1),
                reader.GetString(2),
                FromCents(reader.GetInt64(3)),
                FromCents(reader.GetInt64(4)),
                (decimal)reader.GetDouble(5),
                reader.GetInt32(6),
                FromCents(reader.GetInt64(7)),
                ParseTimestamp(reader.GetString(8)),
                ParseTimestamp(reader.GetString(9)),
                reader.GetInt32(10),
                (AircraftLoanStatus)reader.GetInt32(11)));
        }
        return result.ToImmutable();
    }

    private static async Task<ImmutableArray<AircraftInsurancePolicy>> ReadInsuranceAsync(
        SqliteConnection connection,
        string careerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.policy_id, p.ownership_id, p.plan_id, p.plan_name, p.monthly_premium_cents,
                   p.deductible_cents, p.hull_coverage, p.daily_redo_allowance, p.started_at, p.last_redo_day, p.active
            FROM insurance_policies p
            JOIN owned_aircraft a ON a.ownership_id = p.ownership_id
            WHERE a.career_id = $career
            ORDER BY p.started_at, p.policy_id;
            """;
        command.Parameters.AddWithValue("$career", careerId);

        var result = ImmutableArray.CreateBuilder<AircraftInsurancePolicy>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var plan = new AircraftInsurancePlan(
                reader.GetString(2),
                reader.GetString(3),
                FromCents(reader.GetInt64(4)),
                FromCents(reader.GetInt64(5)),
                (decimal)reader.GetDouble(6),
                reader.GetInt32(7));

            result.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                plan,
                ParseTimestamp(reader.GetString(8)),
                reader.IsDBNull(9)
                    ? null
                    : DateOnly.ParseExact(reader.GetString(9), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(10) != 0));
        }
        return result.ToImmutable();
    }

    private static async Task<ImmutableArray<AircraftStorageLease>> ReadStorageLeasesAsync(
        SqliteConnection connection,
        string careerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.lease_id, l.ownership_id, l.slot_id, l.airport_icao, l.storage_class,
                   l.monthly_cost_cents, l.started_at, l.active
            FROM storage_leases l
            JOIN owned_aircraft a ON a.ownership_id = l.ownership_id
            WHERE a.career_id = $career
            ORDER BY l.started_at, l.lease_id;
            """;
        command.Parameters.AddWithValue("$career", careerId);

        var result = ImmutableArray.CreateBuilder<AircraftStorageLease>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                (AircraftStorageClass)reader.GetInt32(4),
                FromCents(reader.GetInt64(5)),
                ParseTimestamp(reader.GetString(6)),
                reader.GetInt32(7) != 0));
        }
        return result.ToImmutable();
    }

    private static async Task<ImmutableArray<AircraftMaintenanceState>> ReadMaintenanceStatesAsync(
        SqliteConnection connection,
        string careerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.ownership_id, m.tracked_airframe_hours, m.tracked_engine_hours, m.landing_cycles,
                   m.airframe_wear, m.engine_wear, m.gear_wear, m.damage, m.next_inspection_hours,
                   m.confidence, m.updated_at
            FROM maintenance_state m
            JOIN owned_aircraft a ON a.ownership_id = m.ownership_id
            WHERE a.career_id = $career
            ORDER BY m.ownership_id;
            """;
        command.Parameters.AddWithValue("$career", careerId);

        var result = ImmutableArray.CreateBuilder<AircraftMaintenanceState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadMaintenance(reader));
        return result.ToImmutable();
    }

    private static async Task<AircraftMaintenanceState> ReadMaintenanceStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string ownershipId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ownership_id, tracked_airframe_hours, tracked_engine_hours, landing_cycles,
                   airframe_wear, engine_wear, gear_wear, damage, next_inspection_hours, confidence, updated_at
            FROM maintenance_state
            WHERE ownership_id = $ownership;
            """;
        command.Parameters.AddWithValue("$ownership", ownershipId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Maintenance state does not exist.");
        return ReadMaintenance(reader);
    }

    private static AircraftMaintenanceState ReadMaintenance(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetInt32(3),
            reader.GetDouble(4),
            reader.GetDouble(5),
            reader.GetDouble(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            (MaintenanceDataConfidence)reader.GetInt32(9),
            ParseTimestamp(reader.GetString(10)));

    private static async Task UpdateMaintenanceStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AircraftMaintenanceState state,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE maintenance_state SET
                tracked_airframe_hours = $airframeHours,
                tracked_engine_hours = $engineHours,
                landing_cycles = $cycles,
                airframe_wear = $airframeWear,
                engine_wear = $engineWear,
                gear_wear = $gearWear,
                damage = $damage,
                next_inspection_hours = $nextInspection,
                confidence = $confidence,
                updated_at = $updated
            WHERE ownership_id = $ownership;
            """;
        BindMaintenance(command, state);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Maintenance state update failed.");
    }

    private static void BindMaintenance(SqliteCommand command, AircraftMaintenanceState state)
    {
        command.Parameters.AddWithValue("$ownership", state.OwnershipId);
        command.Parameters.AddWithValue("$airframeHours", state.TrackedAirframeHours);
        command.Parameters.AddWithValue("$engineHours", state.TrackedEngineHours);
        command.Parameters.AddWithValue("$cycles", state.LandingCycles);
        command.Parameters.AddWithValue("$airframeWear", state.AirframeWearPercent);
        command.Parameters.AddWithValue("$engineWear", state.EngineWearPercent);
        command.Parameters.AddWithValue("$gearWear", state.GearWearPercent);
        command.Parameters.AddWithValue("$damage", state.DamagePercent);
        command.Parameters.AddWithValue("$nextInspection", state.NextInspectionDueAtTrackedHours);
        command.Parameters.AddWithValue("$confidence", (int)state.Confidence);
        command.Parameters.AddWithValue("$updated", ToTimestamp(state.UpdatedAt));
    }

    private static async Task<bool> MaintenanceOperationExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM maintenance_events WHERE operation_id = $operation LIMIT 1;";
        command.Parameters.AddWithValue("$operation", operationId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task InsertMaintenanceEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationId,
        string ownershipId,
        string eventType,
        decimal cost,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO maintenance_events(operation_id, ownership_id, event_type, cost_cents, recorded_at)
            VALUES($operation, $ownership, $type, $cost, $recorded);
            """;
        command.Parameters.AddWithValue("$operation", operationId);
        command.Parameters.AddWithValue("$ownership", ownershipId);
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue("$cost", ToCents(cost));
        command.Parameters.AddWithValue("$recorded", ToTimestamp(recordedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ReadCareerIdForAircraftAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string ownershipId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT career_id FROM owned_aircraft WHERE ownership_id = $ownership;";
        command.Parameters.AddWithValue("$ownership", ownershipId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string ?? throw new InvalidOperationException("Owned aircraft does not exist.");
    }

    private static string OfferKey(DealerOffer offer) =>
        $"{offer.DealerId}:{offer.ListingId}:{offer.IssuedAt.UtcDateTime.Ticks}";

    private static long ToCents(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        return checked((long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal FromCents(long cents) => cents / 100m;

    private static string ToTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
