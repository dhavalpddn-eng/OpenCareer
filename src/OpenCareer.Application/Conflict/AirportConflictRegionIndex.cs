namespace OpenCareer.Application.Conflict;

public sealed record AirportConflictRegionBinding(
    string AirportIcao,
    string RegionId,
    string SourceId);

public sealed record CountryConflictRegionBinding(
    string CountryCode,
    string RegionId,
    string SourceId);

public sealed record AirportConflictRegionResolution(
    string RegionId,
    string SourceId,
    bool IsAirportSpecific);

public sealed class AirportConflictRegionIndex
{
    private readonly IReadOnlyDictionary<string, AirportConflictRegionBinding> _airportBindings;
    private readonly IReadOnlyDictionary<string, CountryConflictRegionBinding> _countryBindings;

    public AirportConflictRegionIndex(
        IReadOnlyCollection<AirportConflictRegionBinding> airportBindings,
        IReadOnlyCollection<CountryConflictRegionBinding> countryBindings)
    {
        ArgumentNullException.ThrowIfNull(airportBindings);
        ArgumentNullException.ThrowIfNull(countryBindings);

        foreach (var binding in airportBindings)
            ValidateAirportBinding(binding);

        foreach (var binding in countryBindings)
            ValidateCountryBinding(binding);

        _airportBindings = airportBindings.ToDictionary(
            binding => NormalizeIcao(binding.AirportIcao),
            StringComparer.Ordinal);

        _countryBindings = countryBindings.ToDictionary(
            binding => NormalizeCountryCode(binding.CountryCode),
            StringComparer.Ordinal);
    }

    public AirportConflictRegionResolution? Resolve(
        string airportIcao,
        string? countryCode)
    {
        var icao = NormalizeIcao(airportIcao);
        if (_airportBindings.TryGetValue(icao, out var airport))
        {
            return new AirportConflictRegionResolution(
                airport.RegionId,
                airport.SourceId,
                IsAirportSpecific: true);
        }

        if (string.IsNullOrWhiteSpace(countryCode))
            return null;

        var country = NormalizeCountryCode(countryCode);
        if (!_countryBindings.TryGetValue(country, out var fallback))
            return null;

        return new AirportConflictRegionResolution(
            fallback.RegionId,
            fallback.SourceId,
            IsAirportSpecific: false);
    }

    private static void ValidateAirportBinding(AirportConflictRegionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _ = NormalizeIcao(binding.AirportIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.RegionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.SourceId);
    }

    private static void ValidateCountryBinding(CountryConflictRegionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _ = NormalizeCountryCode(binding.CountryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.RegionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.SourceId);
    }

    private static string NormalizeIcao(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 4
            || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("A normalized four-letter ICAO airport identifier is required.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeCountryCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length is < 2 or > 3
            || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Country mapping requires an ISO-style two- or three-letter code.", nameof(value));
        }

        return normalized;
    }
}
