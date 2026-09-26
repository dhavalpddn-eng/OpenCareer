using OpenCareer.Application.Careers;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class AcceptedJobStartBridgeTests
{
    private static readonly DateTimeOffset OfferedAt =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FeasibleDispatchAuthorizesStartWithoutTrustingCallerFlag()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var store =
            new FakeStore(accepted);
        var bridge =
            new AcceptedJobStartBridge(
                new JobContractLifecycleService(store));

        PersistedJobContract started =
            await bridge.StartAsync(
                DispatchResult(
                    accepted,
                    DispatchFeasibilityStatus.Feasible),
                DispatchContext(
                    OfferedAt.AddMinutes(20),
                    dispatchFeasibilityVerified:
                        false));

        Assert.Equal(
            ContractStatus.InProgress,
            started.Contract.Status);
        Assert.Equal(
            OfferedAt.AddMinutes(20),
            started.Contract.StartedAt);
        Assert.Equal(1, store.UpdateCount);
    }

    [Theory]
    [InlineData(DispatchFeasibilityStatus.Infeasible)]
    [InlineData(DispatchFeasibilityStatus.InsufficientData)]
    public async Task NonFeasibleDispatchCannotStartContract(
        DispatchFeasibilityStatus status)
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var store =
            new FakeStore(accepted);
        var bridge =
            new AcceptedJobStartBridge(
                new JobContractLifecycleService(store));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.StartAsync(
                DispatchResult(
                    accepted,
                    status),
                DispatchContext(
                    OfferedAt.AddMinutes(20),
                    dispatchFeasibilityVerified:
                        true)));

        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task ReservationFromDifferentContractCannotAuthorizeStart()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var store =
            new FakeStore(accepted);
        var bridge =
            new AcceptedJobStartBridge(
                new JobContractLifecycleService(store));

        AcceptedJobDispatchResult result =
            DispatchResult(
                accepted,
                DispatchFeasibilityStatus.Feasible)
            with
            {
                FleetResult =
                    DispatchResult(
                        accepted,
                        DispatchFeasibilityStatus.Feasible)
                    .FleetResult
                    with
                    {
                        ReservationId =
                            JobAcceptanceFleetBridge.GetReservationId(
                                Guid.Parse(
                                    "95000000-0000-0000-0000-000000000099"))
                    }
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.StartAsync(
                result,
                DispatchContext(
                    OfferedAt.AddMinutes(20),
                    dispatchFeasibilityVerified:
                        true)));

        Assert.Equal(0, store.UpdateCount);
    }

    [Fact]
    public async Task MissingDispatchResultCannotAuthorizeStart()
    {
        PersistedJobContract accepted =
            AcceptedContract();
        var store =
            new FakeStore(accepted);
        var bridge =
            new AcceptedJobStartBridge(
                new JobContractLifecycleService(store));

        AcceptedJobDispatchResult result =
            DispatchResult(
                accepted,
                DispatchFeasibilityStatus.Feasible)
            with
            {
                DispatchResult = null
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.StartAsync(
                result,
                DispatchContext(
                    OfferedAt.AddMinutes(20),
                    dispatchFeasibilityVerified:
                        true)));

        Assert.Equal(0, store.UpdateCount);
    }

    private static AcceptedJobDispatchResult DispatchResult(
        PersistedJobContract accepted,
        DispatchFeasibilityStatus status)
    {
        DispatchFeasibilityResult dispatch =
            status == DispatchFeasibilityStatus.Feasible
                ? DispatchFeasibilityResult.Create(
                    DispatchFeasibilityStatus.Feasible,
                    "18",
                    "36",
                    Array.Empty<DispatchFeasibilityIssue>())
                : DispatchFeasibilityResult.Create(
                    status,
                    null,
                    null,
                    [
                        new DispatchFeasibilityIssue(
                            DispatchFeasibilityReason.AircraftUnavailable)
                    ]);

        var fleet =
            new JobAcceptanceFleetResult(
                JobAcceptanceFleetStatus.AcceptedAndReserved,
                accepted,
                JobAcceptanceFleetBridge.GetReservationId(
                    accepted.Contract.ContractId),
                "canonical-aircraft");

        return new(
            fleet,
            dispatch);
    }

    private static PersistedJobContract AcceptedContract()
    {
        var contract =
            new JobContract(
                ContractId:
                    Guid.Parse(
                        "95000000-0000-0000-0000-000000000001"),
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CivilianEmployment,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.PilotWage,
                        1_200m,
                        450m,
                        true,
                        true,
                        true),
                OfferedAt:
                    OfferedAt,
                MustStartBy:
                    OfferedAt.AddHours(2),
                MustCompleteBy:
                    OfferedAt.AddHours(4),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumPayloadPounds:
                            500,
                        MinimumRangeNauticalMiles:
                            150,
                        MinimumSeats:
                            0),
                Status:
                    ContractStatus.Accepted,
                AcceptedAt:
                    OfferedAt.AddMinutes(10));

        contract.Validate();

        return new(
            contract,
            Version: 1);
    }

    private static ContractDispatchContext DispatchContext(
        DateTimeOffset time,
        bool dispatchFeasibilityVerified) =>
        new(
            time,
            new AircraftCapabilityProfile(
                "provider-aircraft",
                "Cargo fixture",
                AircraftCapability.Cargo,
                AircraftAccess.Civilian,
                2_000,
                1_000,
                150,
                4,
                1,
                true,
                false,
                false),
            AircraftAccess.Civilian,
            new WorldEventEffects(),
            QualificationsVerified:
                true,
            DispatchFeasibilityVerified:
                dispatchFeasibilityVerified);

    private sealed class FakeStore(
        PersistedJobContract initial)
        : IJobContractStore
    {
        private PersistedJobContract _current =
            initial;

        public int UpdateCount { get; private set; }

        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PersistedJobContract?>(
                _current.Contract.ContractId
                    == contractId
                        ? _current
                        : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contract.Validate();
            UpdateCount++;

            if (_current.Version
                != expectedVersion)
            {
                throw new InvalidOperationException(
                    "Version conflict.");
            }

            _current =
                new PersistedJobContract(
                    contract,
                    checked(expectedVersion + 1));

            return Task.FromResult(
                JobContractSaveResult.Updated);
        }
    }
}
