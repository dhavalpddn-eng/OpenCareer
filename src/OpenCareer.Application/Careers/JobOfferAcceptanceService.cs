using OpenCareer.Domain.Careers;

namespace OpenCareer.Application.Careers;

public sealed class JobOfferAcceptanceService
{
    private readonly IJobContractStore _store;
    private readonly JobContractLifecycleService _lifecycle;
    private readonly SemaphoreSlim _acceptanceGate = new(1, 1);

    public JobOfferAcceptanceService(
        IJobContractStore store,
        JobContractLifecycleService lifecycle)
    {
        _store =
            store
            ?? throw new ArgumentNullException(nameof(store));

        _lifecycle =
            lifecycle
            ?? throw new ArgumentNullException(nameof(lifecycle));
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
            PersistedJobContract? existing =
                await _store
                    .ReadJobContractAsync(
                        expected.ContractId,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (existing is null)
            {
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
                    return await _lifecycle
                        .AcceptAsync(
                            expected.ContractId,
                            context,
                            cancellationToken)
                        .ConfigureAwait(false);
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

            if (current.Contract.Status
                == ContractStatus.Offered)
            {
                return await _lifecycle
                    .AcceptAsync(
                        expected.ContractId,
                        context,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (current.Contract.Status
                    == ContractStatus.Accepted
                && current.Contract.AcceptedAt
                    == context.Time)
            {
                return current;
            }

            throw new InvalidOperationException(
                $"Market offer contract is already in state {current.Contract.Status}.");
        }
        finally
        {
            _acceptanceGate.Release();
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
