using System.Collections.Concurrent;
using OpenCareer.Domain.Airports;

namespace OpenCareer.Application.Planning;

public interface IAirportDataObservationSource
{
    string SourceId { get; }
    AirportDataAuthority Authority { get; }

    Task<AirportDataObservation?> FindAirportObservationAsync(
        string icao,
        CancellationToken cancellationToken = default);
}

public sealed record AirportDataCachePolicy
{
    public TimeSpan ReferenceFreshness { get; init; } = TimeSpan.FromHours(12);

    public void Validate()
    {
        if (ReferenceFreshness <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReferenceFreshness));
    }
}

/// <summary>
/// Resolves one airport observation without merging physical runway facts across
/// providers. Runtime simulator scenery always wins when available. Lower-authority
/// reference data is only a fallback and cannot fill unknown fields in a local
/// simulator observation.
/// </summary>
public sealed class CachedAirportDataSource : IAirportDataSource
{
    private readonly IAirportDataObservationSource[] _sources;
    private readonly AirportDataCachePolicy _policy;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache = new();

    public CachedAirportDataSource(
        IEnumerable<IAirportDataObservationSource> sources,
        AirportDataCachePolicy? policy = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _policy = policy ?? new();
        _policy.Validate();
        _clock = clock ?? TimeProvider.System;

        _sources = sources.ToArray();

        if (_sources.Any(static source => source is null))
            throw new ArgumentException("Airport data sources cannot contain null entries.", nameof(sources));

        foreach (IAirportDataObservationSource source in _sources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);

            if (!Enum.IsDefined(source.Authority))
                throw new ArgumentOutOfRangeException(nameof(sources), "Airport source authority is invalid.");
        }

        if (_sources
            .GroupBy(static source => source.SourceId, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException("Airport data source IDs must be unique.", nameof(sources));
        }

        _sources = _sources
            .OrderByDescending(static source => source.Authority)
            .ThenBy(static source => source.SourceId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<AirportRecord?> FindAirportAsync(
        string icao,
        CancellationToken cancellationToken = default) =>
        (await FindAirportObservationAsync(icao, cancellationToken).ConfigureAwait(false))?.Airport;

    public async Task<AirportDataObservation?> FindAirportObservationAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        string normalizedIcao = NormalizeIcao(icao);

        foreach (IAirportDataObservationSource source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (source.Authority != AirportDataAuthority.LocalSimulator
                && TryGetFreshCached(normalizedIcao, source, out AirportDataObservation? cached))
            {
                return cached;
            }

            AirportDataObservation? observation = await source
                .FindAirportObservationAsync(normalizedIcao, cancellationToken)
                .ConfigureAwait(false);

            if (observation is null)
                continue;

            ValidateObservation(source, normalizedIcao, observation);

            if (source.Authority != AirportDataAuthority.LocalSimulator)
            {
                _cache[new(normalizedIcao, source.SourceId)] =
                    new(observation, _clock.GetUtcNow());
            }

            return observation;
        }

        return null;
    }

    public void Invalidate(string icao)
    {
        string normalizedIcao = NormalizeIcao(icao);

        foreach (CacheKey key in _cache.Keys)
        {
            if (string.Equals(key.Icao, normalizedIcao, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
    }

    public void Clear() => _cache.Clear();

    private bool TryGetFreshCached(
        string icao,
        IAirportDataObservationSource source,
        out AirportDataObservation? observation)
    {
        var key = new CacheKey(icao, source.SourceId);

        if (_cache.TryGetValue(key, out CacheEntry? entry))
        {
            TimeSpan age = _clock.GetUtcNow() - entry.CachedAt;
            if (age >= TimeSpan.Zero && age < _policy.ReferenceFreshness)
            {
                observation = entry.Observation;
                return true;
            }
        }

        _cache.TryRemove(key, out _);
        observation = null;
        return false;
    }

    private static void ValidateObservation(
        IAirportDataObservationSource source,
        string requestedIcao,
        AirportDataObservation observation)
    {
        observation.Validate();

        if (!string.Equals(
                observation.Airport.Icao.Trim(),
                requestedIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Airport data source returned a different ICAO than requested.");
        }

        if (!string.Equals(
                observation.Provenance.SourceId,
                source.SourceId,
                StringComparison.Ordinal)
            || observation.Provenance.Authority != source.Authority)
        {
            throw new InvalidOperationException(
                "Airport data observation provenance does not match its source.");
        }
    }

    private static string NormalizeIcao(string icao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);
        return icao.Trim().ToUpperInvariant();
    }

    private sealed record CacheKey(string Icao, string SourceId);

    private sealed record CacheEntry(
        AirportDataObservation Observation,
        DateTimeOffset CachedAt);
}
