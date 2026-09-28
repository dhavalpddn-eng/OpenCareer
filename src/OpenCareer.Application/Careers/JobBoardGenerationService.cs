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

    internal async Task<JobMarketOfferDraft> AddDevelopmentOfferAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _stateGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            JobBoardState board =
                await _store
                    .GetAsync(
                        "KJFK",
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? JobBoardState.Empty(
                    "KJFK",
                    now);

            board.Validate();
            EnsureAirportIdentity(
                board,
                "KJFK");

            if (now < board.UpdatedAt)
                now = board.UpdatedAt;

            JobMarketOfferDraft? existing =
                board.Offers
                    .FirstOrDefault(
                        offer =>
                            DevelopmentFlight.IsDevelopment(offer)
                            && offer.ExpiresAt > now);

            if (existing is not null)
                return existing;

            foreach (JobMarketOfferDraft stale
                in board.Offers.Where(DevelopmentFlight.IsDevelopment))
            {
                board = board.Retire(
                    stale.OfferId,
                    now);
            }

            JobMarketOfferDraft created =
                DevelopmentFlight.CreateOffer(
                    Guid.NewGuid(),
                    now);

            JobBoardState next =
                board with
                {
                    UpdatedAt = now,
                    Offers = board.Offers.Add(created)
                };

            next.Validate();

            await _store
                .SaveAsync(
                    next,
                    cancellationToken)
                .ConfigureAwait(false);

            JobBoardState saved =
                await _store
                    .GetAsync(
                        "KJFK",
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Development offer was not durably stored.");

            saved.Validate();

            JobMarketOfferDraft? persisted =
                saved.Offers
                    .SingleOrDefault(
                        offer =>
                            offer.OfferId == created.OfferId);

            if (persisted is null
                || !DevelopmentFlight.IsDevelopment(persisted))
            {
                throw new InvalidOperationException(
                    "Development offer was not durably stored.");
            }

            return persisted;
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
