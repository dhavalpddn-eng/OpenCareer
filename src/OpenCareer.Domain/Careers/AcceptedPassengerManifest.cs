using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Careers;

public sealed record AcceptedPassengerManifest(
    Guid ContractId,
    string OriginIcao,
    string DestinationIcao,
    DateTimeOffset AcceptedAt,
    int PassengerCount,
    double PassengerAndBaggageWeightPounds)
{
    public static AcceptedPassengerManifest Create(
        JobContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();

        if (contract.Status is not (
                ContractStatus.Accepted
                or ContractStatus.InProgress)
            || contract.AcceptedAt is not { } acceptedAt)
        {
            throw new InvalidOperationException(
                "Passenger manifest requires an accepted or in-progress job contract.");
        }

        if (contract.Kind != ContractKind.Passenger)
        {
            throw new InvalidOperationException(
                "Passenger manifest requires a passenger contract.");
        }

        int passengerCount =
            contract.AircraftRequirements.MinimumPassengerCapacity;

        if (passengerCount <= 0
            || contract.PlanningTerms is not { } planningTerms
            || planningTerms.PayloadPounds <= 0)
        {
            throw new InvalidOperationException(
                "Passenger manifest requires immutable passenger count and payload terms.");
        }

        var manifest = new AcceptedPassengerManifest(
            contract.ContractId,
            contract.OriginIcao,
            contract.DestinationIcao,
            acceptedAt,
            passengerCount,
            planningTerms.PayloadPounds);

        manifest.ValidateForContract(contract);
        return manifest;
    }

    public void Validate()
    {
        if (ContractId == Guid.Empty)
            throw new ArgumentException("Contract identity is required.", nameof(ContractId));

        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(DestinationIcao, nameof(DestinationIcao));

        if (PassengerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(PassengerCount));

        if (!double.IsFinite(PassengerAndBaggageWeightPounds)
            || PassengerAndBaggageWeightPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PassengerAndBaggageWeightPounds));
        }
    }

    public void ValidateForContract(
        JobContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        Validate();
        contract.Validate();

        if (contract.Status is not (
                ContractStatus.Accepted
                or ContractStatus.InProgress)
            || contract.AcceptedAt is not { } acceptedAt
            || contract.Kind != ContractKind.Passenger
            || contract.PlanningTerms is not { } planningTerms
            || contract.ContractId != ContractId
            || acceptedAt != AcceptedAt
            || !string.Equals(contract.OriginIcao, OriginIcao, StringComparison.Ordinal)
            || !string.Equals(contract.DestinationIcao, DestinationIcao, StringComparison.Ordinal)
            || contract.AircraftRequirements.MinimumPassengerCapacity
                != PassengerCount
            || Math.Abs(
                planningTerms.PayloadPounds
                - PassengerAndBaggageWeightPounds) > 1e-9)
        {
            throw new InvalidOperationException(
                "Passenger manifest does not match the accepted contract authority.");
        }
    }

    public static bool TryCreate(
        JobContract contract,
        out AcceptedPassengerManifest? manifest)
    {
        ArgumentNullException.ThrowIfNull(contract);

        try
        {
            manifest = Create(contract);
            return true;
        }
        catch (InvalidOperationException)
        {
            manifest = null;
            return false;
        }
    }

    private static void ValidateIcao(
        string value,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != 4
            || value.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "A normalized four-letter ICAO identifier is required.",
                name);
        }
    }
}
