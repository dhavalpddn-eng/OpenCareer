using OpenCareer.Application.Careers;
using OpenCareer.Application.Fleet;
using OpenCareer.Application.Flights;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class CareerJobCompletionInputSourceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingMissionAuthorityBlocksWithoutInventingCompletion()
    {
        TestFixture fixture =
            CreateFixture(
                missionSources:
                    Array.Empty<ICareerJobMissionCompletionSource>(),
                costSources:
                    [new FixedCostsSource()]);

        CareerJobCompletionInputSnapshot snapshot =
            await fixture.Source.ReadCurrentAsync();

        Assert.Equal(
            CareerJobCompletionInputState.MissionEvidenceUnavailable,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task IncompleteShutdownNamesTheMissingLiveEvidence()
    {
        Guid contractId =
            Guid.Parse(
                "a2000000-0000-0000-0000-000000000001");
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId: contractId);
        session = Advance(session, 1, stable: true, validAircraft: true);
        session = Advance(session, 2, movement: true);
        session = Advance(session, 3, takeoffCandidate: true);
        session = Advance(session, 4, airborne: true);
        session = Advance(session, 5, touchdown: true);
        session = Advance(session, 6, rollout: true);

        TestFixture fixture =
            CreateFixture(
                missionSources: [new FixedMissionSource()],
                costSources: [new FixedCostsSource()],
                sessionOverride: session);

        CareerJobCompletionInputSnapshot snapshot =
            await fixture.Source.ReadCurrentAsync();

        Assert.Equal(
            CareerJobCompletionInputState.FlightEvidenceIncomplete,
            snapshot.State);
        Assert.Contains(
            "parking brake while stopped on the ground",
            snapshot.Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "all engines off after parking",
            snapshot.Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Current operation state: {session.OperationState}",
            snapshot.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingActualCostsBlocksAfterVerifiedMissionEvidence()
    {
        TestFixture fixture =
            CreateFixture(
                missionSources:
                    [new FixedMissionSource()],
                costSources:
                    Array.Empty<ICareerJobSettlementCostsSource>());

        CareerJobCompletionInputSnapshot snapshot =
            await fixture.Source.ReadCurrentAsync();

        Assert.Equal(
            CareerJobCompletionInputState.SettlementCostsUnavailable,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task VerifiedAuthoritiesProduceStablePlayableCompletionRequest()
    {
        TestFixture fixture =
            CreateFixture(
                missionSources:
                    [new FixedMissionSource()],
                costSources:
                    [new FixedCostsSource()]);

        CareerJobCompletionInputSnapshot snapshot =
            await fixture.Source.ReadCurrentAsync();

        Assert.True(
            snapshot.IsReady);
        Assert.Equal(
            fixture.Session.SessionId,
            snapshot.FlightSessionId);

        CareerJobPlayableCompletionRequest request =
            Assert.IsType<CareerJobPlayableCompletionRequest>(
                snapshot.Request);

        Assert.Equal(
            fixture.Contract.Contract.ContractId,
            request.ContractId);
        Assert.True(
            request.MissionConditionsVerified);
        Assert.True(
            request.PostFlightTasksVerified);
        Assert.Equal(
            new ContractSettlementCosts(
                100m,
                50m,
                25m),
            request.ActualCosts);
        Assert.Equal(
            Epoch.AddSeconds(9),
            request.SettledAt);
        Assert.Equal(
            request.SettledAt,
            request.LogbookCommittedAt);
        Assert.Equal(
            request.SettledAt,
            request.ExperienceSavedAt);
        Assert.Equal(
            "Fixture Aircraft",
            request.LogbookContext.Aircraft.DisplayName);
        Assert.Equal(
            "KRME",
            request.LogbookContext.ActualDeparture);
        Assert.Equal(
            "KSYR",
            request.LogbookContext.ActualArrival);
        Assert.Equal(
            MissionOutcome.Succeeded,
            request.LogbookContext.MissionOutcome);
        Assert.Equal(
            fixture.Contract.Contract.ReputationReward,
            request.LogbookContext.ReputationDelta);
    }

    [Fact]
    public async Task UnverifiedMissionConditionsCannotProduceRequest()
    {
        TestFixture fixture =
            CreateFixture(
                missionSources:
                    [new FixedMissionSource(
                        verified:
                            false,
                        outcome:
                            MissionOutcome.Failed)],
                costSources:
                    [new FixedCostsSource()]);

        CareerJobCompletionInputSnapshot snapshot =
            await fixture.Source.ReadCurrentAsync();

        Assert.Equal(
            CareerJobCompletionInputState.MissionConditionsNotVerified,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task MultipleMissionAuthoritiesAreRejected()
    {
        TestFixture fixture =
            CreateFixture(
                missionSources:
                [
                    new FixedMissionSource(),
                    new FixedMissionSource()
                ],
                costSources:
                    [new FixedCostsSource()]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Source.ReadCurrentAsync());
    }

    [Fact]
    public async Task SessionReplacementDuringRegistryReadProjectsReplacementInsteadOfStaleReadyState()
    {
        var readStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var release =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var registry =
            new FakeAircraftRegistrySource(
                async cancellationToken =>
                {
                    readStarted.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                });
        TestFixture fixture =
            CreateFixture(
                missionSources: [new FixedMissionSource()],
                costSources: [new FixedCostsSource()],
                aircraftRegistry: registry);

        Task<CareerJobCompletionInputSnapshot> read =
            fixture.Source.ReadCurrentAsync();
        await readStarted.Task;

        FlightSession replacement =
            ReplaceWithContractlessActiveSession(
                fixture.Sessions);
        release.TrySetResult();

        CareerJobCompletionInputSnapshot snapshot =
            await read;

        Assert.Equal(
            CareerJobCompletionInputState.NoCareerFlight,
            snapshot.State);
        Assert.Equal(
            replacement.SessionId,
            snapshot.FlightSessionId);
        Assert.False(snapshot.IsReady);
        Assert.Null(snapshot.Request);
        Assert.Equal(1, registry.ReadCount);
    }

    [Fact]
    public async Task RepeatedSessionReplacementDuringRegistryReadFailsClosedAfterOneRetry()
    {
        TestFixture? fixture = null;
        var registry =
            new FakeAircraftRegistrySource(
                _ =>
                {
                    ReplaceWithShutdownCareerSession(
                        fixture!.Sessions,
                        fixture.Contract.Contract.ContractId);
                    return Task.CompletedTask;
                });
        fixture =
            CreateFixture(
                missionSources: [new FixedMissionSource()],
                costSources: [new FixedCostsSource()],
                aircraftRegistry: registry);

        CareerJobCompletionInputSnapshot snapshot =
            await fixture.Source.ReadCurrentAsync();

        Assert.Equal(
            CareerJobCompletionInputState.FlightSessionChanged,
            snapshot.State);
        Assert.False(snapshot.IsReady);
        Assert.Null(snapshot.ContractId);
        Assert.Null(snapshot.FlightSessionId);
        Assert.Null(snapshot.Request);
        Assert.Contains(
            "Reopen Current Flight",
            snapshot.Detail,
            StringComparison.Ordinal);
        Assert.Equal(2, registry.ReadCount);
    }

    private static TestFixture CreateFixture(
        IReadOnlyList<ICareerJobMissionCompletionSource> missionSources,
        IReadOnlyList<ICareerJobSettlementCostsSource> costSources,
        FlightSession? sessionOverride = null,
        IAircraftRegistrySource? aircraftRegistry = null)
    {
        Guid contractId =
            Guid.Parse(
                "a2000000-0000-0000-0000-000000000001");

        PersistedJobContract contract =
            InProgressContract(
                contractId);

        FlightSession session =
            sessionOverride
            ?? ShutdownSession(
                contractId);

        var sessions =
            new FlightSessionCoordinator();

        sessions.Restore(
            session);

        var tracker =
            new JobFlightCompletionEvidenceTracker(
                sessions,
                new FakeEvidenceSource());

        string reservationId =
            JobAcceptanceFleetBridge.GetReservationId(
                contractId);

        var source =
            new CareerJobCompletionInputSource(
                sessions,
                new FakeContracts(
                    contract),
                tracker,
                new FakeReservationLookup(
                    new AircraftReservationOwnership(
                        "canonical-aircraft",
                        reservationId)),
                aircraftRegistry
                    ?? new FakeAircraftRegistrySource(),
                missionSources,
                costSources);

        return new(
            source,
            contract,
            session,
            sessions);
    }

    private static FlightSession ReplaceWithContractlessActiveSession(
        FlightSessionCoordinator sessions)
    {
        RetireCurrentSession(sessions);
        return sessions.Start(Epoch.AddHours(1));
    }

    private static FlightSession ReplaceWithShutdownCareerSession(
        FlightSessionCoordinator sessions,
        Guid contractId)
    {
        RetireCurrentSession(sessions);
        FlightSession replacement =
            ShutdownSession(
                contractId,
                Guid.NewGuid());
        sessions.Restore(replacement);
        return replacement;
    }

    private static void RetireCurrentSession(
        FlightSessionCoordinator sessions)
    {
        FlightSession current =
            Assert.IsType<FlightSession>(
                sessions.Current);
        sessions.CommitPersisted(
            current with
            {
                UpdatedAt = current.UpdatedAt.AddSeconds(1),
                Status = FlightSessionStatus.Cancelled
            });
        sessions.ClearTerminalSession();
    }

    private static PersistedJobContract InProgressContract(
        Guid contractId)
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
                    ContractStatus.InProgress,
                ReputationReward:
                    3.5,
                AcceptedAt:
                    Epoch.AddMinutes(-30),
                StartedAt:
                    Epoch);

        contract.Validate();

        return new(
            contract,
            Version: 2);
    }

    private static FlightSession ShutdownSession(
        Guid contractId,
        Guid? sessionId = null)
    {
        FlightSession session =
            FlightSession.Start(
                Epoch,
                contractId,
                sessionId:
                    sessionId ?? contractId);

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

    private sealed record TestFixture(
        CareerJobCompletionInputSource Source,
        PersistedJobContract Contract,
        FlightSession Session,
        FlightSessionCoordinator Sessions);

    private sealed class FixedMissionSource(
        bool verified = true,
        MissionOutcome outcome = MissionOutcome.Succeeded)
        : ICareerJobMissionCompletionSource
    {
        public Task<CareerJobMissionCompletionEvidence?> ReadAsync(
            JobContract contract,
            FlightSession flightSession,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<CareerJobMissionCompletionEvidence?>(
                new(
                    contract.ContractId,
                    flightSession.SessionId,
                    Epoch.AddSeconds(8),
                    verified,
                    ActualDeparture:
                        "KRME",
                    ActualArrival:
                        "KSYR",
                    DiversionLocation:
                        null,
                    Payload:
                        new PayloadDebrief(
                            PassengerCount:
                                null,
                            CargoMassPounds:
                                500,
                            CargoDescription:
                                "Career cargo",
                            Outcome:
                                verified
                                    ? "Delivered"
                                    : "Not delivered",
                            EvidenceQuality:
                                EvidenceQuality.MissionDeclared),
                    SafetyOutcome:
                        FlightSafetyOutcome.CompletedNormally,
                    MissionOutcome:
                        outcome));
        }
    }

    private sealed class FixedCostsSource
        : ICareerJobSettlementCostsSource
    {
        public Task<CareerJobSettlementCostsEvidence?> ReadAsync(
            JobContract contract,
            FlightSession flightSession,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<CareerJobSettlementCostsEvidence?>(
                new(
                    contract.ContractId,
                    Epoch.AddSeconds(9),
                    new ContractSettlementCosts(
                        100m,
                        50m,
                        25m)));
        }
    }

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

    private sealed class FakeReservationLookup(
        AircraftReservationOwnership? ownership)
        : IAircraftReservationLookup
    {
        public Task<AircraftReservationOwnership?> FindByReservationIdAsync(
            string reservationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                ownership is not null
                && string.Equals(
                    ownership.ReservationId,
                    reservationId,
                    StringComparison.Ordinal)
                    ? ownership
                    : null);
        }
    }

    private sealed class FakeAircraftRegistrySource(
        Func<CancellationToken, Task>? beforeReturnAsync = null)
        : IAircraftRegistrySource
    {
        public int ReadCount { get; private set; }

        public async Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (beforeReturnAsync is not null)
                await beforeReturnAsync(cancellationToken);

            return ResolveAircraft(aircraftId);
        }
    }

    private static AircraftRegistryResolution ResolveAircraft(
        string aircraftId) =>
        AircraftRegistryResolver.Resolve(
        [
            new AircraftRegistryObservation(
                CanonicalAircraftId: aircraftId,
                ProviderId: "fixture",
                ProviderRecordId: "fixture-aircraft",
                Confidence: AircraftDataConfidence.Verified,
                IsInstalled: true,
                DisplayName: "Fixture Aircraft",
                Capabilities: AircraftCapability.Cargo,
                Access: AircraftAccess.Civilian,
                MaximumPayloadPounds: 2_000,
                MaximumRangeNauticalMiles: 800,
                TypicalCruiseKnots: 140,
                Seats: 4,
                EngineCount: 1,
                IfrCapable: true,
                Pressurized: false,
                RetractableGear: false)
        ]);
}
