using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Planning;

public interface IAircraftRegistryObservationSource
{
    Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default);
}

public sealed class AircraftRegistryCatalogService(
    IEnumerable<IAircraftRegistryObservationSource> sources)
    : IAircraftRegistrySource
{
    private readonly IAircraftRegistryObservationSource[] _sources =
        sources?.ToArray()
        ?? throw new ArgumentNullException(nameof(sources));

    public async Task<AircraftRegistryResolution?> FindAircraftAsync(
        string aircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftId);

        var observations = new List<AircraftRegistryObservation>();

        foreach (IAircraftRegistryObservationSource source in _sources)
        {
            if (source is null)
                throw new InvalidOperationException("Aircraft registry sources cannot contain null entries.");

            IReadOnlyList<AircraftRegistryObservation> supplied = await source
                .FindAircraftObservationsAsync(aircraftId, cancellationToken)
                .ConfigureAwait(false);

            ArgumentNullException.ThrowIfNull(supplied);

            foreach (AircraftRegistryObservation observation in supplied)
            {
                ArgumentNullException.ThrowIfNull(observation);
                observation.Validate();

                if (!string.Equals(
                        observation.CanonicalAircraftId.Trim(),
                        aircraftId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Aircraft registry source returned an observation for a different canonical aircraft identity.");
                }

                observations.Add(observation);
            }
        }

        return observations.Count == 0
            ? null
            : AircraftRegistryResolver.Resolve(observations);
    }
}
