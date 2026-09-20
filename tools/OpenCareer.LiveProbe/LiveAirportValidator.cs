using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Planning;
using OpenCareer.SimConnect;

namespace OpenCareer.LiveProbe;

internal sealed record LiveAirportValidationResult(
    string Icao,
    AirportDataObservation? Airport,
    AirportDispatchWeatherObservation? Weather)
{
    internal bool Succeeded => Airport is not null && Weather is not null;
}

internal sealed class LiveAirportValidator
{
    private readonly string _icao;
    private readonly SimConnectAirportDataObservationSource _airportSource;
    private readonly SimConnectLocalAirportWeatherSource _weatherSource;

    internal LiveAirportValidator(
        SimConnectConnection connection,
        string icao)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        _icao = icao.Trim().ToUpperInvariant();
        _airportSource = new(connection);
        _weatherSource = new(connection);
    }

    internal async Task<LiveAirportValidationResult> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        AirportDataObservation? airport = await _airportSource
            .FindAirportObservationAsync(_icao, cancellationToken)
            .ConfigureAwait(false);

        AirportDispatchWeatherObservation? weather = airport is null
            ? null
            : await _weatherSource
                .FindWeatherAsync(_icao, cancellationToken)
                .ConfigureAwait(false);

        return new(_icao, airport, weather);
    }
}
