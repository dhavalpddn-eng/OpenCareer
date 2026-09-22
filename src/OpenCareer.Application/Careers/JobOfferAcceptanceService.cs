using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class JobOfferAcceptanceService
{
    private readonly IJobContractStore _store;
    private readonly JobContractLifecycleService _lifecycle;
    private readonly IJobBoardStateStore _boardStore;
    private readonly JobContractRuntimeState _runtimeState;
    private readonly SemaphoreSlim _acceptanceGate = new(1, 1);

    public JobOfferAcceptanceService(
        IJobContractStore store,
        JobContractLifecycleService lifecycle,
        IJobBoardStateStore boardStore,
        JobContractRuntimeState runtimeState)
    {
        _store =
            store
            ?? throw new ArgumentNullException(nameof(store));

        _lifecycle =
            lifecycle
            ?? throw new ArgumentNullException(nameof(lifecycle));

        _boardStore =
            boardStore
            ?? throw new ArgumentNullException(nameof(boardStore));

        _runtimeState =
            runtimeState
            ?? throw new ArgumentNullException(nameof(runtimeState));
    }

    public async Task<PersistedJobContract> AcceptOfferAsync(
        JobContractCreationRequest request,
        ContractDispatchContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        request.Validate();

        if (context.Time != request.AcceptanceTime)
        {
            throw new ArgumentException(
                "Dispatch context time must match the offer acceptance time.",
                nameof(context));
        }

        JobContract expected =
            JobContractFactory.Create(request);

        await _acceptanceGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            JobBoardState board =
                await LoadBoardAsync(
                    request.Offer,
                    cancellationToken)
                    .ConfigureAwait(false);

            bool offerIsActive =
                board.Offers.Any(
                    offer =>
                        offer.OfferId
                        == expected.ContractId);

            bool offerIsRetired =
                board.RetiredOfferIds.Contains(
                    expected.ContractId);

            PersistedJobContract? existing =
                await _store
                    .ReadJobContractAsync(
                        expected.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existing is null)
            {
                if (!offerIsActive)
                {
                    throw new InvalidOperationException(
                        "A retired or missing job-board offer cannot create a new contract.");
                }

                JobContractSaveResult createResult =
                    await _store
                        .CreateJobContractAsync(
                            expected,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (createResult is not
                    (JobContractSaveResult.Created
                    or JobContractSaveResult.AlreadySaved))
                {
                    throw new InvalidOperationException(
                        $"Unexpected contract creation result {createResult}.");
                }

                if (createResult
                    == JobContractSaveResult.Created)
                {
                    PersistedJobContract accepted =
                        await _lifecycle
                            .AcceptAsync(
                                expected.ContractId,
                                context,
                                cancellationToken)
                            .ConfigureAwait(false);

                    await _runtimeState
                        .PublishAuthoritativeAsync(
                            accepted,
                            cancellationToken)
                        .ConfigureAwait(false);

                    await EnsureOfferRetiredAsync(
                            board,
                            request.Offer,
                            context.Time,
                            cancellationToken)
                        .ConfigureAwait(false);

                    return accepted;
                }

                existing =
                    await _store
                        .ReadJobContractAsync(
                            expected.ContractId,
                            cancellationToken)
                        .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "Job contract disappeared after idempotent creation.");
            }

            PersistedJobContract current =
                existing;

            current.Validate();

            if (!EquivalentCreationTerms(
                    current.Contract,
                    expected))
            {
                throw new InvalidOperationException(
                    "Existing job contract does not match the accepted market offer.");
            }

            PersistedJobContract accepted;

            if (current.Contract.Status
                == ContractStatus.Offered)
            {
                if (!offerIsActive)
                {
                    throw new InvalidOperationException(
                        "A retired or missing job-board offer cannot transition an offered contract to Accepted.");
                }

                accepted =
                    await _lifecycle
                        .AcceptAsync(
                            expected.ContractId,
                            context,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            else if (current.Contract.Status
                    == ContractStatus.Accepted
                && current.Contract.AcceptedAt
                    == context.Time)
            {
                accepted =
                    current;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Market offer contract is already in state {current.Contract.Status}.");
            }

            await _runtimeState
                .PublishAuthoritativeAsync(
                    accepted,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!offerIsRetired)
            {
                await EnsureOfferRetiredAsync(
                        board,
                        request.Offer,
                        context.Time,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return accepted;
        }
        finally
        {
            _acceptanceGate.Release();
        }
    }

    private async Task<JobBoardState> LoadBoardAsync(
        JobMarketOfferDraft expectedOffer,
        CancellationToken cancellationToken)
    {
        JobBoardState board =
            await _boardStore
                .GetAsync(
                    expectedOffer.OriginIcao,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The job board containing the accepted offer was not found.");

        board.Validate();

        JobMarketOfferDraft? activeOffer =
            board.Offers
                .SingleOrDefault(
                    offer =>
                        offer.OfferId
                        == expectedOffer.OfferId);

        bool retired =
            board.RetiredOfferIds.Contains(
                expectedOffer.OfferId);

        if (activeOffer is null && !retired)
        {
            throw new InvalidOperationException(
                "The requested job offer is not active or retired on the authoritative job board.");
        }

        if (activeOffer is not null
            && activeOffer != expectedOffer)
        {
            throw new InvalidOperationException(
                "The authoritative job-board offer does not match the requested acceptance.");
        }

        return board;
    }

    private async Task EnsureOfferRetiredAsync(
        JobBoardState board,
        JobMarketOfferDraft acceptedOffer,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken)
    {
        if (board.RetiredOfferIds.Contains(
                acceptedOffer.OfferId))
        {
            if (board.Offers.Any(
                    offer =>
                        offer.OfferId
                        == acceptedOffer.OfferId))
            {
                throw new InvalidOperationException(
                    "A job-board offer cannot be both active and retired.");
            }

            return;
        }

        DateTimeOffset retirementTime =
            board.UpdatedAt > acceptedAt
                ? board.UpdatedAt
                : acceptedAt;

        JobBoardState retired =
            board.Retire(
                acceptedOffer.OfferId,
                retirementTime);

        if (!retired.RetiredOfferIds.Contains(
                acceptedOffer.OfferId))
        {
            throw new InvalidOperationException(
                "The accepted offer was not active on the authoritative job board.");
        }

        await _boardStore
            .SaveAsync(
                retired,
                cancellationToken)
            .ConfigureAwait(false);

        JobBoardState authoritative =
            await _boardStore
                .GetAsync(
                    acceptedOffer.OriginIcao,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The job board disappeared after accepted-offer retirement.");

        authoritative.Validate();

        if (!authoritative.RetiredOfferIds.Contains(
                acceptedOffer.OfferId)
            || authoritative.Offers.Any(
                offer =>
                    offer.OfferId
                    == acceptedOffer.OfferId))
        {
            throw new InvalidOperationException(
                "Accepted job offer retirement was not durably preserved.");
        }
    }

    private static bool EquivalentCreationTerms(
        JobContract actual,
        JobContract expected)
    {
        JobContract normalized =
            actual with
            {
                Status =
                    ContractStatus.Offered,
                AcceptedAt = null,
                StartedAt = null,
                CompletedAt = null
            };

        return normalized == expected;
    }
}
