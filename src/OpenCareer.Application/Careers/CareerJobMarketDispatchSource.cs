using OpenCareer.Application.Planning;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Careers;

public interface ICareerJobMarketDispatchSource
{
    Task<IReadOnlyDictionary<ContractKind, IReadOnlySet<string>>> FilterAsync(
        string originIcao,
        CareerJobMarketFeasibilitySnapshot feasibility,
        IReadOnlyDictionary<ContractKind, IReadOnlyDictionary<string, AcceptedCargoLot>> cargoLots,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Screens each proven kind/route/aircraft combination with the same automatic
/// planning inputs used after acceptance. Only the read-only composite evaluator
/// is used: market refresh cannot acquire a reservation or persist a flight plan.
/// </summary>
public sealed class CareerJobMarketDispatchSource(
    IDispatchCruiseProfileSource cruiseProfiles,
    ICompositeDispatchPlanningService planning)
    : ICareerJobMarketDispatchSource
{
    public async Task<IReadOnlyDictionary<ContractKind, IReadOnlySet<string>>> FilterAsync(
        string originIcao,
        CareerJobMarketFeasibilitySnapshot feasibility,
        IReadOnlyDictionary<ContractKind, IReadOnlyDictionary<string, AcceptedCargoLot>> cargoLots,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originIcao);
        ArgumentNullException.ThrowIfNull(feasibility);
        ArgumentNullException.ThrowIfNull(cargoLots);
        cancellationToken.ThrowIfCancellationRequested();

        var eligible = new Dictionary<ContractKind, IReadOnlySet<string>>();
        if (!feasibility.IsReady || feasibility.AircraftCandidates is null)
            return eligible;

        // Cache only within one refresh. Later refreshes must observe changed
        // performance, fuel capacity, airport and Fleet availability evidence.
        var catalogs = new Dictionary<string, DispatchCruiseProfileCatalog>(StringComparer.Ordinal);
        var evaluated = new Dictionary<(string Aircraft, string Destination, double Payload), bool>();

        foreach ((ContractKind kind, IReadOnlySet<string> destinations)
            in feasibility.EligibleDestinationIcaosByKind)
        {
            var readyDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JobMarketDestination destination in feasibility.Destinations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string icao = destination.NormalizedIcao;
                if (!destinations.Contains(icao))
                    continue;

                double payload;
                if (kind == ContractKind.Passenger)
                    payload = JobMarketPolicy.Default.StandardPassengerPayloadPounds;
                else if (kind is ContractKind.Ferry or ContractKind.Reposition)
                    payload = 0;
                else if (kind is ContractKind.Cargo or ContractKind.ExpressCargo or ContractKind.AogPartsDelivery
                    && cargoLots.TryGetValue(kind, out var lots)
                    && lots.TryGetValue(icao, out AcceptedCargoLot? lot))
                {
                    lot.Validate();
                    if (!string.Equals(lot.OriginIcao, originIcao, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(lot.DestinationIcao, icao, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Market cargo lot does not match the screened route.");
                    }

                    payload = lot.MassPounds;
                }
                else
                    continue;

                foreach (CareerJobMarketAircraftCandidate candidate in feasibility.AircraftCandidates)
                {
                    if (candidate.Kind != kind
                        || !string.Equals(candidate.DestinationIcao, icao, StringComparison.OrdinalIgnoreCase)
                        || !double.IsFinite(candidate.MaximumPayloadPounds)
                        || candidate.MaximumPayloadPounds < payload)
                        continue;

                    var key = (candidate.CanonicalAircraftId, icao, payload);
                    if (!evaluated.TryGetValue(key, out bool isFeasible))
                    {
                        if (!catalogs.TryGetValue(candidate.CanonicalAircraftId, out var catalog))
                        {
                            catalog = await cruiseProfiles.ReadAsync(candidate.CanonicalAircraftId, cancellationToken)
                                .ConfigureAwait(false);
                            catalogs.Add(candidate.CanonicalAircraftId, catalog);
                        }

                        if (catalog.Status == DispatchCruiseProfileCatalogStatus.Ready)
                        {
                            foreach (DispatchCruiseProfileOption profile in catalog.Profiles)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                AutomaticDispatchInputs inputs = AutomaticDispatchPolicy.Create(
                                    profile, payload, destination.DistanceNm);
                                CompositeDispatchPlanningResult result = await planning.EvaluateAsync(
                                    candidate.CanonicalAircraftId, originIcao, icao,
                                    inputs.Cruise, inputs.RouteFuel, inputs.Requirements, cancellationToken)
                                    .ConfigureAwait(false);
                                cancellationToken.ThrowIfCancellationRequested();

                                if (result.Status == CompositeDispatchPlanningStatus.Evaluated
                                    && result.FuelWeightPlan is not null
                                    && result.DispatchResult?.Status == DispatchFeasibilityStatus.Feasible)
                                {
                                    isFeasible = true;
                                    break;
                                }
                            }
                        }

                        evaluated.Add(key, isFeasible);
                    }

                    if (isFeasible)
                    {
                        readyDestinations.Add(icao);
                        break;
                    }
                }
            }

            if (readyDestinations.Count > 0)
                eligible.Add(kind, readyDestinations);
        }

        return eligible;
    }
}
