using System.Collections.Immutable;
using OpenCareer.Application.Careers;
using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Airports;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;
using OpenCareer.Domain.Planning;

namespace OpenCareer.Tests;

public sealed class CareerJobStartInputSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MissingContractTermsFailsClosed()
    {
        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    Array.Empty<ICareerJobContractTermsSource>(),
                dispatchAuthorities:
                    [new FixedDispatchAuthoritySource()]);

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft");

        Assert.Equal(
            CareerJobStartInputState.ContractTermsUnavailable,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task MissingDispatchAuthorityFailsClosed()
    {
        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new FixedContractTermsSource()],
                dispatchAuthorities:
                    Array.Empty<ICareerJobDispatchAuthoritySource>());

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft");

        Assert.Equal(
            CareerJobStartInputState.DispatchAuthorityUnavailable,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task VerifiedAuthoritiesProduceDispatchVerifiedStartRequest()
    {
        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new FixedContractTermsSource()],
                dispatchAuthorities:
                    [new FixedDispatchAuthoritySource()]);

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft");

        Assert.True(
            snapshot.IsReady);

        CareerJobPlayableStartRequest request =
            Assert.IsType<CareerJobPlayableStartRequest>(
                snapshot.Request);

        Assert.Equal(
            fixture.Offer.OfferId,
            request.Contract.Offer.OfferId);
        Assert.Equal(
            Now,
            request.Contract.AcceptanceTime);
        Assert.True(
            request.DispatchContext.DispatchFeasibilityVerified);
        Assert.True(
            request.DispatchContext.QualificationsVerified);
        Assert.Equal(
            "fixture-aircraft",
            request.DispatchContext.Aircraft.AircraftId);
        Assert.Equal(
            fixture.Offer.DistanceNm,
            request.DispatchRequirements.RequiredRangeNauticalMiles);
        Assert.Equal(
            0,
            request.DispatchRequirements.PayloadPounds);
    }

    [Fact]
    public async Task DevelopmentOfferWithSelectedInstalledAircraftProducesNormalReadyRequest()
    {
        JobMarketOfferDraft offer =
            DevelopmentFlight.CreateOffer(
                Guid.Parse(
                    "a7000000-0000-0000-0000-000000000090"),
                Now.AddMinutes(-30));

        JobBoardState board =
            JobBoardState
                .Empty(
                    "KJFK",
                    offer.OfferedAt)
                .Reconcile(
                    offer.OfferedAt,
                    1,
                    [offer]);

        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "a7000000-0000-0000-0000-000000000091"),
                "KJFK",
                Now.AddDays(-30));

        var registry =
            new FakeAircraftRegistrySource();

        var source =
            new CareerJobStartInputSource(
                new FakeBoardStore(board),
                new PlayerCareerRuntimeState(
                    new FakeProfileStore(
                        new PlayerCareerProfileStoreRecord(
                            Revision:
                                1,
                            profile,
                            SavedAt:
                                Now.AddDays(-1)))),
                new FakeContractStore(existing: null),
                registry,
                new OperationDispatchPlanningService(
                    registry,
                    new FakeAirportSource()),
                [new PersistedJobContractTermsSource()],
                [new StandardCivilianPointToPointDispatchAuthoritySource()],
                new FixedTimeProvider(Now));

        CareerJobStartInputSnapshot snapshot =
            await source.ReadAsync(
                offer.OfferId,
                "fixture-aircraft");

        Assert.True(
            snapshot.IsReady);

        CareerJobPlayableStartRequest request =
            Assert.IsType<CareerJobPlayableStartRequest>(
                snapshot.Request);

        Assert.Equal(
            "fixture-aircraft",
            request.DispatchContext.Aircraft.AircraftId);
        Assert.Equal(
            "KJFK",
            request.Contract.Offer.OriginIcao);
        Assert.Equal(
            "KJFK",
            request.Contract.Offer.DestinationIcao);
        Assert.Equal(
            DevelopmentFlight.MarketId,
            request.Contract.MarketId);
        Assert.Equal(
            0,
            request.Contract.ReputationReward);
        Assert.Equal(
            0,
            request.Contract.ReputationPenalty);

        JobContract contract =
            JobContractFactory.Create(
                request.Contract);

        Assert.Equal(
            0m,
            contract.Compensation.PilotCompensation);
    }

    [Fact]
    public async Task MissingIrrelevantCapabilityFieldsUseFailClosedFerryProjection()
    {
        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new FixedContractTermsSource()],
                dispatchAuthorities:
                    [new FixedDispatchAuthoritySource()],
                registry:
                    new PartialAircraftRegistrySource());

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft");

        Assert.True(
            snapshot.IsReady);

        CareerJobPlayableStartRequest request =
            Assert.IsType<CareerJobPlayableStartRequest>(
                snapshot.Request);

        Assert.Equal(
            AircraftAccess.Civilian,
            request.DispatchContext.Aircraft.Access);
        Assert.Equal(
            0,
            request.DispatchContext.Aircraft.Seats);
        Assert.False(
            request.DispatchContext.Aircraft.IfrCapable);
        Assert.Equal(
            500,
            request.DispatchContext.Aircraft.MaximumRangeNauticalMiles);
    }

    [Fact]
    public async Task PhysicalPreflightFailureCannotSetDispatchVerified()
    {
        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new FixedContractTermsSource()],
                dispatchAuthorities:
                    [new FixedDispatchAuthoritySource(
                        requiredRange:
                            900)]);

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft");

        Assert.Equal(
            CareerJobStartInputState.PreflightInfeasible,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task ExistingAcceptedContractReusesAuthoritativeAcceptanceTime()
    {
        DateTimeOffset acceptedAt =
            Now.AddMinutes(-5);

        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new FixedContractTermsSource()],
                dispatchAuthorities:
                    [new FixedDispatchAuthoritySource()],
                acceptedAt:
                    acceptedAt);

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft");

        CareerJobPlayableStartRequest request =
            Assert.IsType<CareerJobPlayableStartRequest>(
                snapshot.Request);

        Assert.Equal(
            acceptedAt,
            request.Contract.AcceptanceTime);
        Assert.Equal(
            acceptedAt,
            request.DispatchContext.Time);
    }

    [Fact]
    public async Task MultipleContractTermAuthoritiesAreRejected()
    {
        TestFixture fixture =
            CreateFixture(
                contractTerms:
                [
                    new FixedContractTermsSource(),
                    new FixedContractTermsSource()
                ],
                dispatchAuthorities:
                    [new FixedDispatchAuthoritySource()]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                "fixture-aircraft"));
    }

    private static TestFixture CreateFixture(
        IReadOnlyList<ICareerJobContractTermsSource> contractTerms,
        IReadOnlyList<ICareerJobDispatchAuthoritySource> dispatchAuthorities,
        DateTimeOffset? acceptedAt = null,
        IAircraftRegistrySource? registry = null)
    {
        JobMarketOfferDraft offer =
            Offer();

        JobBoardState board =
            new(
                "KRME",
                Now.AddMinutes(-30),
                ImmutableArray.Create(
                    offer),
                ImmutableHashSet<Guid>.Empty);

        board.Validate();

        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse(
                    "a7000000-0000-0000-0000-000000000001"),
                "KRME",
                Now.AddDays(-30));

        PersistedJobContract? existing =
            acceptedAt is { } accepted
                ? AcceptedContract(
                    offer,
                    accepted)
                : null;

        registry ??=
            new FakeAircraftRegistrySource();

        var source =
            new CareerJobStartInputSource(
                new FakeBoardStore(
                    board),
                new PlayerCareerRuntimeState(
                    new FakeProfileStore(
                        new PlayerCareerProfileStoreRecord(
                            Revision:
                                1,
                            profile,
                            SavedAt:
                                Now.AddDays(-1)))),
                new FakeContractStore(
                    existing),
                registry,
                new OperationDispatchPlanningService(
                    registry,
                    new FakeAirportSource()),
                contractTerms,
                dispatchAuthorities,
                new FixedTimeProvider(
                    Now));

        return new(
            source,
            offer);
    }

    private static JobMarketOfferDraft Offer() =>
        new(
            Guid.Parse(
                "a7000000-0000-0000-0000-000000000002"),
            ServiceTrack.CivilianEmployment,
            ContractKind.Ferry,
            JobScenarioKind.Standard,
            "KRME",
            "KSYR",
            DistanceNm:
                100,
            EstimatedFlightHours:
                1,
            OfferedAt:
                Now.AddHours(-1),
            ExpiresAt:
                Now.AddHours(2),
            IsLockedPreview:
                false,
            RouteStrength:
                0.5,
            RelationshipStrength:
                0.5,
            MarketSelectionWeight:
                1);

    private static PersistedJobContract AcceptedContract(
        JobMarketOfferDraft offer,
        DateTimeOffset acceptedAt)
    {
        CareerJobContractTermsEvidence terms =
            FixedContractTermsSource.Terms(
                offer);

        var request =
            new JobContractCreationRequest(
                offer,
                acceptedAt,
                terms.AircraftRequirements,
                terms.EstimatedFlightHours,
                terms.PayloadPounds,
                terms.DemandAttractiveness,
                terms.Urgency,
                terms.Difficulty,
                terms.EstimatedPlayerOperatingCosts,
                terms.EmployerId,
                terms.MustStartBy,
                terms.MustCompleteBy,
                terms.ReputationReward,
                terms.ReputationPenalty,
                terms.MarketId,
                terms.WorldEventId,
                terms.GovernmentAuthorizationRequired);

        JobContract offered =
            JobContractFactory.Create(
                request);

        AircraftCapabilityProfile aircraft =
            FakeAircraftRegistrySource.Record()
                .Capabilities;

        JobContract accepted =
            offered.Accept(
                new ContractDispatchContext(
                    acceptedAt,
                    aircraft,
                    AircraftAccess.Civilian,
                    new WorldEventEffects(),
                    QualificationsVerified:
                        true,
                    DispatchFeasibilityVerified:
                        true,
                    NavigationPlanVerified:
                        true));

        return new(
            accepted,
            Version:
                1);
    }

    private sealed record TestFixture(
        CareerJobStartInputSource Source,
        JobMarketOfferDraft Offer);

    private sealed class FixedContractTermsSource
        : ICareerJobContractTermsSource
    {
        public Task<CareerJobContractTermsEvidence?> ReadAsync(
            JobMarketOfferDraft offer,
            PlayerCareerProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<CareerJobContractTermsEvidence?>(
                Terms(offer));
        }

        public static CareerJobContractTermsEvidence Terms(
            JobMarketOfferDraft offer) =>
            new(
                offer.OfferId,
                new AircraftMissionRequirements(
                    AllowedAccess:
                        AircraftAccess.Civilian,
                    MinimumRangeNauticalMiles:
                        offer.DistanceNm,
                    MinimumSeats:
                        0),
                EstimatedFlightHours:
                    offer.EstimatedFlightHours!.Value,
                PayloadPounds:
                    0,
                DemandAttractiveness:
                    1,
                Urgency:
                    0,
                Difficulty:
                    0,
                EstimatedPlayerOperatingCosts:
                    0m,
                AuthorizedAircraftAccess:
                    AircraftAccess.Civilian);
    }

    private sealed class FixedDispatchAuthoritySource(
        double? requiredRange = null)
        : ICareerJobDispatchAuthoritySource
    {
        public Task<CareerJobDispatchAuthorityEvidence?> ReadAsync(
            JobMarketOfferDraft offer,
            PlayerCareerProfile profile,
            AircraftRegistryRecord aircraft,
            CareerJobContractTermsEvidence contractTerms,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<CareerJobDispatchAuthorityEvidence?>(
                new(
                    offer.OfferId,
                    aircraft.AircraftId,
                    AircraftAccess.Civilian,
                    new WorldEventEffects(),
                    new OperationDispatchRequirements(
                        PayloadPounds:
                            0,
                        RequiredRangeNauticalMiles:
                            requiredRange
                            ?? offer.DistanceNm),
                    QualificationsVerified:
                        true,
                    NavigationPlanVerified:
                        true));
        }
    }

    private sealed class PartialAircraftRegistrySource
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(
                    aircraftId,
                    "fixture-aircraft",
                    StringComparison.Ordinal))
            {
                return Task.FromResult<AircraftRegistryResolution?>(
                    null);
            }

            var runway =
                new AircraftRunwayPerformanceProfile(
                    MinimumTakeoffRunwayFeet:
                        1_000,
                    MinimumLandingRunwayFeet:
                        1_000,
                    MinimumRunwayWidthFeet:
                        40,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt
                        | RunwaySurfaceSupport.Concrete,
                    AircraftDataConfidence.Verified,
                    Source:
                        "fixture");

            AircraftRegistryResolution resolution =
                AircraftRegistryResolver.Resolve(
                [
                    new AircraftRegistryObservation(
                        "fixture-aircraft",
                        ProviderId:
                            "partial-fixture",
                        ProviderRecordId:
                            "fixture-aircraft",
                        AircraftDataConfidence.Verified,
                        IsInstalled:
                            true,
                        DisplayName:
                            "Fixture Aircraft",
                        Access:
                            AircraftAccess.Civilian,
                        MaximumPayloadPounds:
                            1_000,
                        MaximumRangeNauticalMiles:
                            500,
                        RunwayPerformance:
                            runway)
                ]);

            Assert.Contains(
                AircraftRegistryField.Seats,
                resolution.UnresolvedCapabilityFields);

            return Task.FromResult<AircraftRegistryResolution?>(
                resolution);
        }
    }

    private sealed class FakeAircraftRegistrySource
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(
                    aircraftId,
                    "fixture-aircraft",
                    StringComparison.Ordinal))
            {
                return Task.FromResult<AircraftRegistryResolution?>(
                    null);
            }

            AircraftRegistryRecord record =
                Record();

            AircraftRegistryResolution resolution =
                AircraftRegistryResolver.Resolve(
                [
                    new AircraftRegistryObservation(
                        record.AircraftId,
                        ProviderId:
                            "fixture",
                        ProviderRecordId:
                            "fixture-aircraft",
                        AircraftDataConfidence.Verified,
                        IsInstalled:
                            true,
                        DisplayName:
                            record.Capabilities.DisplayName,
                        Capabilities:
                            record.Capabilities.Capabilities,
                        Access:
                            record.Capabilities.Access,
                        MaximumPayloadPounds:
                            record.Capabilities.MaximumPayloadPounds,
                        MaximumRangeNauticalMiles:
                            record.Capabilities.MaximumRangeNauticalMiles,
                        TypicalCruiseKnots:
                            record.Capabilities.TypicalCruiseKnots,
                        Seats:
                            record.Capabilities.Seats,
                        EngineCount:
                            record.Capabilities.EngineCount,
                        IfrCapable:
                            record.Capabilities.IfrCapable,
                        Pressurized:
                            record.Capabilities.Pressurized,
                        RetractableGear:
                            record.Capabilities.RetractableGear,
                        RunwayPerformance:
                            record.RunwayPerformance)
                ]);

            return Task.FromResult<AircraftRegistryResolution?>(
                resolution);
        }

        public static AircraftRegistryRecord Record()
        {
            var profile =
                new AircraftCapabilityProfile(
                    "fixture-aircraft",
                    "Fixture Aircraft",
                    AircraftCapability.None,
                    AircraftAccess.Civilian,
                    MaximumPayloadPounds:
                        1_000,
                    MaximumRangeNauticalMiles:
                        500,
                    TypicalCruiseKnots:
                        120,
                    Seats:
                        4,
                    EngineCount:
                        1,
                    IfrCapable:
                        true,
                    Pressurized:
                        false,
                    RetractableGear:
                        false);

            return new(
                profile,
                IsInstalled:
                    true,
                new AircraftRunwayPerformanceProfile(
                    MinimumTakeoffRunwayFeet:
                        1_000,
                    MinimumLandingRunwayFeet:
                        1_000,
                    MinimumRunwayWidthFeet:
                        30,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt,
                    AircraftDataConfidence.Verified,
                    Source:
                        "fixture"));
        }
    }

    private sealed class FakeAirportSource
        : IAirportDataSource
    {
        public Task<AirportRecord?> FindAirportAsync(
            string icao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AirportRecord airport =
                new(
                    icao,
                    $"{icao} Fixture",
                    [
                        new RunwayRecord(
                            "01/19",
                            UsableLengthFeet:
                                5_000,
                            WidthFeet:
                                100,
                            RunwaySurface.Asphalt)
                    ]);

            return Task.FromResult<AirportRecord?>(
                airport);
        }
    }

    private sealed class FakeBoardStore(
        JobBoardState board)
        : IJobBoardStateStore
    {
        public Task SaveAsync(
            JobBoardState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobBoardState?> GetAsync(
            string airportIcao,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<JobBoardState?>(
                string.Equals(
                    board.AirportIcao,
                    airportIcao,
                    StringComparison.Ordinal)
                    ? board
                    : null);
        }

        public Task<IReadOnlyList<JobBoardState>> LoadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobBoardState>>(
                [board]);
    }

    private sealed class FakeProfileStore(
        PlayerCareerProfileStoreRecord record)
        : IPlayerCareerProfileStore
    {
        public Task<PlayerCareerProfileStoreRecord?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<PlayerCareerProfileStoreRecord?>(
                record);
        }

        public Task<PlayerCareerProfileStoreRecord> SaveAsync(
            PlayerCareerProfile profile,
            long? expectedRevision,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeContractStore(
        PersistedJobContract? existing)
        : IJobContractStore
    {
        public Task<PersistedJobContract?> ReadJobContractAsync(
            Guid contractId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                existing is not null
                && existing.Contract.ContractId
                    == contractId
                    ? existing
                    : null);
        }

        public Task<JobContractSaveResult> CreateJobContractAsync(
            JobContract contract,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobContractSaveResult> UpdateJobContractAsync(
            JobContract contract,
            long expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            now;
    }
}
