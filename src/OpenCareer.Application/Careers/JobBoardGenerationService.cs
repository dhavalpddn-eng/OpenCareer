using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class JobBoardGenerationService
{
    private readonly IJobBoardStateStore _store;
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    public JobBoardGenerationService(
        IJobBoardStateStore store)
    {
        _store = store
            ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<JobBoardState> RefreshAsync(
        JobMarketGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        string airportIcao =
            request.Origin.Icao
                .Trim()
                .ToUpperInvariant();

        await _stateGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            JobBoardState current =
                await _store
                    .GetAsync(
                        airportIcao,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? JobBoardState.Empty(
                    airportIcao,
                    request.Time);

            EnsureAirportIdentity(
                current,
                airportIcao);

            IReadOnlyList<JobMarketOfferDraft> candidates =
                JobMarketGenerator.Generate(request);

            JobBoardState next =
                current.Reconcile(
                    request.Time,
                    Math.Max(1, candidates.Count),
                    candidates);

            await _store
                .SaveAsync(
                    next,
                    cancellationToken)
                .ConfigureAwait(false);

            JobBoardState authoritative =
                await _store
                    .GetAsync(
                        airportIcao,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Job board was not readable after persistence.");

            EnsureAirportIdentity(
                authoritative,
                airportIcao);

            if (authoritative.UpdatedAt < next.UpdatedAt)
            {
                throw new InvalidOperationException(
                    "Persisted job board is older than the requested refresh.");
            }

            return authoritative;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static void EnsureAirportIdentity(
        JobBoardState state,
        string expectedAirportIcao)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!string.Equals(
                state.AirportIcao,
                expectedAirportIcao,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Job-board store returned airport '{state.AirportIcao}' while '{expectedAirportIcao}' was requested.");
        }
    }
}
