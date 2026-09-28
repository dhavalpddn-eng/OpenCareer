using OpenCareer.Domain.Planning;

namespace OpenCareer.Application.Planning;

/// <summary>
/// Uses simulator-local weather when it is genuinely local to the requested
/// airport, then falls back to a separately supplied reference source.
/// </summary>
public sealed class LocalThenReferenceAirportWeatherSource(
    IAirportDispatchWeatherSource localSource,
    IAirportDispatchWeatherSource referenceSource)
    : IAirportDispatchWeatherSource
{
    public async Task<AirportDispatchWeatherObservation?> FindWeatherAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        string normalizedIcao = icao.Trim().ToUpperInvariant();

        AirportDispatchWeatherObservation? local = await localSource
            .FindWeatherAsync(normalizedIcao, cancellationToken)
            .ConfigureAwait(false);

        if (local is not null)
        {
            ValidateObservation(
                normalizedIcao,
                local,
                DispatchWeatherAuthority.LocalSimulator);

            return local;
        }

        AirportDispatchWeatherObservation? reference = await referenceSource
            .FindWeatherAsync(normalizedIcao, cancellationToken)
            .ConfigureAwait(false);

        if (reference is not null)
        {
            ValidateObservation(
                normalizedIcao,
                reference,
                DispatchWeatherAuthority.Reference);
        }

        return reference;
    }

    private static void ValidateObservation(
        string requestedIcao,
        AirportDispatchWeatherObservation observation,
        DispatchWeatherAuthority expectedAuthority)
    {
        observation.Validate();

        if (!string.Equals(
                observation.Icao.Trim(),
                requestedIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Weather source returned a different ICAO than requested.");
        }

        if (observation.Authority != expectedAuthority)
        {
            throw new InvalidOperationException(
                "Weather source returned an unexpected authority.");
        }
    }
}
