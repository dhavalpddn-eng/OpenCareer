using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Careers;

public sealed record CareerJobOperatingCostBasis(
    Guid ContractId,
    Guid FlightSessionId,
    DateTimeOffset EvidenceAt,
    double StartFuelPounds,
    double LastFuelPounds,
    double FuelBurnedPounds,
    double FuelAddedPounds,
    double DistanceNauticalMiles,
    TimeSpan BlockTime,
    TimeSpan AirborneTime,
    TimeSpan CareerCreditTime)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        if (FlightSessionId == Guid.Empty)
            throw new ArgumentException("FlightSession ID is required.", nameof(FlightSessionId));

        if (EvidenceAt == default)
            throw new ArgumentOutOfRangeException(nameof(EvidenceAt));

        ValidateNonNegativeFinite(StartFuelPounds, nameof(StartFuelPounds));
        ValidateNonNegativeFinite(LastFuelPounds, nameof(LastFuelPounds));
        ValidateNonNegativeFinite(FuelBurnedPounds, nameof(FuelBurnedPounds));
        ValidateNonNegativeFinite(FuelAddedPounds, nameof(FuelAddedPounds));
        ValidateNonNegativeFinite(DistanceNauticalMiles, nameof(DistanceNauticalMiles));

        ValidateDuration(BlockTime, nameof(BlockTime));
        ValidateDuration(AirborneTime, nameof(AirborneTime));
        ValidateDuration(CareerCreditTime, nameof(CareerCreditTime));
    }

    private static void ValidateNonNegativeFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateDuration(
        TimeSpan value,
        string parameterName)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record CareerJobOperatingCostQuote(
    Guid ContractId,
    Guid FlightSessionId,
    DateTimeOffset PricedAt,
    string PricingAuthorityId,
    ContractSettlementCosts Costs)
{
    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract ID is required.", nameof(ContractId));

        if (FlightSessionId == Guid.Empty)
            throw new ArgumentException("FlightSession ID is required.", nameof(FlightSessionId));

        if (PricedAt == default)
            throw new ArgumentOutOfRangeException(nameof(PricedAt));

        ArgumentException.ThrowIfNullOrWhiteSpace(PricingAuthorityId);
        ArgumentNullException.ThrowIfNull(Costs);
        Costs.Validate();
    }
}

public interface ICareerJobOperatingCostQuoteSource
{
    string SourceId { get; }

    Task<CareerJobOperatingCostQuote?> QuoteAsync(
        JobContract contract,
        CareerJobOperatingCostBasis basis,
        CancellationToken cancellationToken = default);
}

public sealed class PersistedFlightSettlementCostsSource
    : ICareerJobSettlementCostsSource
{
    private readonly ICareerJobOperatingCostQuoteSource[] _pricingSources;

    public PersistedFlightSettlementCostsSource(
        IEnumerable<ICareerJobOperatingCostQuoteSource> pricingSources)
    {
        ArgumentNullException.ThrowIfNull(pricingSources);

        _pricingSources =
            pricingSources.ToArray();

        if (_pricingSources.Any(static source => source is null))
        {
            throw new ArgumentException(
                "Operating-cost pricing sources cannot contain null entries.",
                nameof(pricingSources));
        }

        foreach (ICareerJobOperatingCostQuoteSource source in _pricingSources)
            ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);

        if (_pricingSources
            .GroupBy(
                static source => source.SourceId,
                StringComparer.Ordinal)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Operating-cost pricing source IDs must be unique.",
                nameof(pricingSources));
        }
    }

    public async Task<CareerJobSettlementCostsEvidence?> ReadAsync(
        JobContract contract,
        FlightSession flightSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(flightSession);

        contract.Validate();

        bool initialCompletionReady =
            contract.Status == ContractStatus.InProgress
            && flightSession.Status == FlightSessionStatus.Active
            && flightSession.OperationState == FlightOperationState.Shutdown
            && flightSession.Tracking.State == FlightTrackingState.Parked;

        DateTimeOffset sessionCompletedAt =
            flightSession.Milestones.CompletedAt
            ?? flightSession.UpdatedAt;

        bool terminalReplayReady =
            contract.Status == ContractStatus.Completed
            && contract.CompletedAt == sessionCompletedAt
            && flightSession.Status == FlightSessionStatus.Completed
            && flightSession.OperationState == FlightOperationState.Complete
            && flightSession.Tracking.State == FlightTrackingState.Complete;

        if (flightSession.ContractId
                != contract.ContractId
            || (!initialCompletionReady
                && !terminalReplayReady)
            || flightSession.Tracking.CrashReported)
        {
            return null;
        }

        FlightSessionStatistics statistics =
            flightSession.EffectiveStatistics;

        if (statistics.StartFuelPounds
                is not { } startFuel
            || statistics.LastFuelPounds
                is not { } lastFuel)
        {
            return null;
        }

        var basis =
            new CareerJobOperatingCostBasis(
                contract.ContractId,
                flightSession.SessionId,
                flightSession.UpdatedAt,
                startFuel,
                lastFuel,
                statistics.FuelBurnedPounds,
                statistics.FuelAddedPounds,
                statistics.DistanceNauticalMiles,
                flightSession.TimeLedger.BlockTime,
                flightSession.TimeLedger.AirborneTime,
                flightSession.TimeLedger.CareerCreditTime);

        basis.Validate();

        CareerJobOperatingCostQuote? quote =
            await ReadSingleQuoteAsync(
                    contract,
                    basis,
                    cancellationToken)
                .ConfigureAwait(false);

        if (quote is null)
            return null;

        quote.Validate();

        if (quote.ContractId
                != contract.ContractId
            || quote.FlightSessionId
                != flightSession.SessionId)
        {
            throw new InvalidOperationException(
                "Operating-cost quote does not belong to the current career flight.");
        }

        DateTimeOffset observedAt =
            quote.PricedAt > flightSession.UpdatedAt
                ? quote.PricedAt
                : flightSession.UpdatedAt;

        return new(
            contract.ContractId,
            observedAt,
            quote.Costs);
    }

    private async Task<CareerJobOperatingCostQuote?> ReadSingleQuoteAsync(
        JobContract contract,
        CareerJobOperatingCostBasis basis,
        CancellationToken cancellationToken)
    {
        var matches =
            new List<CareerJobOperatingCostQuote>();

        foreach (ICareerJobOperatingCostQuoteSource source in _pricingSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CareerJobOperatingCostQuote? quote =
                await source
                    .QuoteAsync(
                        contract,
                        basis,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (quote is null)
                continue;

            quote.Validate();

            if (!string.Equals(
                    quote.PricingAuthorityId,
                    source.SourceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Operating-cost quote authority does not match its source.");
            }

            matches.Add(quote);
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple operating-cost pricing sources claimed authority for one career flight.")
        };
    }
}
