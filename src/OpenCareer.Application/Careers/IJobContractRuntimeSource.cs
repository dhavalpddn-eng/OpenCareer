namespace OpenCareer.Application.Careers;

public interface IJobContractRuntimeSource
{
    bool IsInitialized { get; }

    IReadOnlyList<PersistedJobContract> Current { get; }

    PersistedJobContract? Find(
        Guid contractId);
}
