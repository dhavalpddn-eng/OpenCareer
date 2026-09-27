using OpenCareer.Application.Careers;
using OpenCareer.Application.Flights;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class CareerJobPlayableLoopReadinessSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShutdownSequenceReportsVerifiedInputBoundary()
    {
        Guid contractId =
            Guid.Parse(
                "a1000000-0000-0000-0000-000000000001");

        FlightSession session =
            ShutdownSession(
                contractId);

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            session);

        var source =
            new CareerJobPlayableLoopReadinessSource(
                sessions,
                new FakeContracts(
                    InProgressContract(
                        contractId)),
                new JobFlightCompletionEvidenceTracker(
                    sessions,
                    new FakeEvidenceSource()));

        CareerJobPlayableReadinessSnapshot snapshot =
            source.Current;

        Assert.Equal(
            CareerJobPlayableReadinessState.AwaitingVerifiedCompletionInputs,
            snapshot.State);
        Assert.Equal(
            contractId,
            snapshot.ContractId);
        Assert.Contains(
            "mission",
            snapshot.Detail,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "cost",
            snapshot.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuspendedCareerFlightDoesNotClaimCompletionReadiness()
    {
        Guid contractId =
            Guid.Parse(
                "a1000000-0000-0000-0000-000000000002");

        FlightSession active =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId);

        FlightSession suspended =
            FlightSessionEngine.Advance(
                active,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected:
                            false,
                        ContinuityPlausible:
                            false)));

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            suspended);

        var source =
            new CareerJobPlayableLoopReadinessSource(
                sessions,
                new FakeContracts(
                    InProgressContract(
                        contractId)),
                new JobFlightCompletionEvidenceTracker(
                    sessions,
                    new FakeEvidenceSource()));

        Assert.Equal(
            CareerJobPlayableReadinessState.FlightSuspended,
            source.Current.State);
    }

    [Fact]
    public void CompletedContractAndSessionReportTerminalWorkflowReadiness()
    {
        Guid contractId =
            Guid.Parse(
                "a1000000-0000-0000-0000-000000000003");

        FlightSession completed =
            CompletedSession(
                contractId);

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            completed);

        var source =
            new CareerJobPlayableLoopReadinessSource(
                sessions,
                new FakeContracts(
                    CompletedContract(
                        contractId,
                        completed.Milestones.CompletedAt!.Value)),
                new JobFlightCompletionEvidenceTracker(
                    sessions,
                    new FakeEvidenceSource()));

        Assert.Equal(
            CareerJobPlayableReadinessState.CompletedAwaitingTerminalWorkflow,
            source.Current.State);
    }

    [Fact]
    public void MissingSessionReportsNoCareerFlight()
    {
        var sessions =
            new FlightSessionCoordinator();

        var source =
            new CareerJobPlayableLoopReadinessSource(
                sessions,
                new FakeContracts(),
                new JobFlightCompletionEvidenceTracker(
                    sessions,
                    new FakeEvidenceSource()));

        Assert.Equal(
            CareerJobPlayableReadinessState.NoCareerFlight,
            source.Current.State);
        Assert.Null(
            source.Current.ContractId);
    }

    private static PersistedJobContract InProgressContract(
        Guid contractId) =>
        Persisted(
            contractId,
            ContractStatus.InProgress,
            CompletedAt:
                null);

    private static PersistedJobContract CompletedContract(
        Guid contractId,
        DateTimeOffset completedAt) =>
        Persisted(
            contractId,
            ContractStatus.Completed,
            completedAt);

    private static PersistedJobContract Persisted(
        Guid contractId,
        ContractStatus status,
        DateTimeOffset? CompletedAt)
    {
        var contract =
            new JobContract(
                ContractId:
                    contractId,
                EmployerId:
                    null,
                Kind:
                    ContractKind.Cargo,
                ServiceTrack:
                    ServiceTrack.CompanyContract,
                OriginIcao:
                    "KRME",
                DestinationIcao:
                    "KSYR",
                Compensation:
                    new ContractCompensation(
                        CompensationModel.CompanyRevenue,
                        1_000m,
                        0m,
                        false,
                        false,
                        false),
                OfferedAt:
                    Epoch.AddHours(-1),
                MustStartBy:
                    null,
                MustCompleteBy:
                    Epoch.AddHours(2),
                AircraftRequirements:
                    new AircraftMissionRequirements(
                        RequiredCapabilities:
                            AircraftCapability.Cargo,
                        AllowedAccess:
                            AircraftAccess.Civilian,
                        MinimumSeats:
                            0),
                Status:
                    status,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch,
                CompletedAt:
                    CompletedAt);

        contract.Validate();

        return new(
            contract,
            status == ContractStatus.Completed
                ? 3
                : 2);
    }

    private static FlightSession ShutdownSession(
        Guid contractId)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    contractId);

        session =
            Advance(
                session,
                1,
                stable:
                    true,
                validAircraft:
                    true);

        session =
            Advance(
                session,
                2,
                movement:
                    true);

        session =
            Advance(
                session,
                3,
                takeoffCandidate:
                    true);

        session =
            Advance(
                session,
                4,
                airborne:
                    true);

        session =
            Advance(
                session,
                5,
                touchdown:
                    true);

        session =
            Advance(
                session,
                6,
                rollout:
                    true);

        return Advance(
            session,
            7,
            parking:
                true,
            shutdown:
                true);
    }

    private static FlightSession CompletedSession(
        Guid contractId)
    {
        FlightSession shutdown =
            ShutdownSession(
                contractId);

        return FlightSessionEngine.Advance(
            shutdown,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(8),
                    Connected:
                        true,
                    StableTelemetry:
                        true,
                    ContinuityPlausible:
                        true,
                    OperationCompleteConfirmed:
                        true),
                ShutdownConfirmed:
                    true));
    }

    private static FlightSession Advance(
        FlightSession session,
        int seconds,
        bool stable = false,
        bool validAircraft = false,
        bool movement = false,
        bool takeoffCandidate = false,
        bool airborne = false,
        bool touchdown = false,
        bool rollout = false,
        bool parking = false,
        bool shutdown = false) =>
        FlightSessionEngine.Advance(
            session,
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(seconds),
                    Connected:
                        true,
                    StableTelemetry:
                        stable,
                    ValidLoadedAircraft:
                        validAircraft,
                    ContinuityPlausible:
                        true,
                    SelfPoweredMovementForFlight:
                        movement,
                    TakeoffCandidate:
                        takeoffCandidate,
                    AirborneConfirmed:
                        airborne,
                    TouchdownConfirmed:
                        touchdown,
                    LandingRolloutConfirmed:
                        rollout,
                    ParkingConfirmed:
                        parking),
                ShutdownConfirmed:
                    shutdown));

    private sealed class FakeContracts(
        params PersistedJobContract[] contracts)
        : IJobContractRuntimeSource
    {
        public bool IsInitialized =>
            true;

        public IReadOnlyList<PersistedJobContract> Current =>
            contracts;

        public PersistedJobContract? Find(
            Guid contractId) =>
            contracts.SingleOrDefault(
                item =>
                    item.Contract.ContractId
                    == contractId);
    }

    private sealed class FakeEvidenceSource
        : IFlightStateEvidenceSource
    {
        public FlightStateEvidence? Current =>
            null;

        public event Action<FlightStateEvidence?>? EvidenceChanged
        {
            add { }
            remove { }
        }
    }
}
