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
                "fixture-aircraft",
                selectedOwnershipId: "ownership-one");

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
                "fixture-aircraft",
                selectedOwnershipId: "ownership-one");

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
                "fixture-aircraft",
                selectedOwnershipId: "ownership-one");

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
                "fixture-aircraft",
                selectedOwnershipId: "ownership-one");

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
                "fixture-aircraft",
                selectedOwnershipId: "ownership-one");

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
                "fixture-aircraft",
                selectedOwnershipId: "ownership-one");

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

    [Fact]
    public async Task ContractProviderAircraftCanReachReadyFromKnownRegistryData()
    {
        ProviderAircraftAssignment provider =
            ProviderAssignment();

        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new PersistedJobContractTermsSource()],
                dispatchAuthorities:
                    [new StandardCivilianPointToPointDispatchAuthoritySource()],
                registry:
                    new FakeAircraftRegistrySource(
                        isInstalled:
                            false),
                offer:
                    OfferWithProvider(
                        provider,
                        PrivateQualifications()),
                profileQualifications:
                    PrivateQualifications());

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                provider.AircraftId,
                provider.ProviderAircraftInstanceId);

        Assert.True(
            snapshot.IsReady);

        CareerJobPlayableStartRequest request =
            Assert.IsType<CareerJobPlayableStartRequest>(
                snapshot.Request);

        Assert.Equal(
            provider.ProviderAircraftInstanceId,
            request.SelectedProviderAircraftInstanceId);
        Assert.Equal(
            provider,
            request.Contract.ProviderAircraft);
        Assert.Equal(
            provider.AircraftId,
            request.DispatchContext.Aircraft.AircraftId);
        Assert.Equal(
            fixture.Offer.OriginIcao,
            request.Contract.ProviderAircraft?.OriginIcao);
        Assert.NotEqual(
            fixture.Offer.OfferId,
            provider.ProviderAircraftInstanceId);
    }

    [Fact]
    public async Task RequirementOnlyOfferAssignsSupportedProviderWithoutSimulatorOrInstalledCatalog()
    {
        JobMarketOfferDraft offer = OfferWithProvider(ProviderAssignment());
        offer = offer with
        {
            ContractTerms = offer.ContractTerms! with { ProviderAircraft = null }
        };

        var registry = new AircraftRegistryCatalogService(
            [new PlayableLoopAircraftRegistrySource()]);
        TestFixture fixture = CreateFixture(
            [new PersistedJobContractTermsSource()],
            [new StandardCivilianPointToPointDispatchAuthoritySource()],
            registry: registry,
            offer: offer);

        ProviderAircraftAssignment expected = Assert.IsType<ProviderAircraftAssignment>(
            await new ProviderAircraftAssignmentResolver().ResolveAsync(
                offer, offer.ContractTerms!.AircraftRequirements));

        CareerJobStartInputSnapshot first = await fixture.Source.ReadAsync(
            offer.OfferId, expected.AircraftId, expected.ProviderAircraftInstanceId);
        CareerJobStartInputSnapshot retry = await fixture.Source.ReadAsync(
            offer.OfferId, expected.AircraftId, expected.ProviderAircraftInstanceId);

        Assert.True(first.IsReady, first.Detail);
        Assert.Equal(expected, first.Request!.Contract.ProviderAircraft);
        Assert.Equal(expected, retry.Request!.Contract.ProviderAircraft);
        Assert.Null(offer.ContractTerms.ProviderAircraft);
        Assert.Equal(AircraftInstallationStatus.KnownOnly,
            (await registry.FindAircraftAsync(expected.AircraftId))!.InstallationStatus);
    }

    [Fact]
    public async Task RequirementOnlyOfferAcceptsEligibleLocalOwnedAircraftWithoutInstalledCatalog()
    {
        JobMarketOfferDraft offer = OfferWithProvider(ProviderAssignment());
        offer = offer with
        {
            ContractTerms = offer.ContractTerms! with { ProviderAircraft = null }
        };
        TestFixture fixture = CreateFixture(
            [new PersistedJobContractTermsSource()],
            [new StandardCivilianPointToPointDispatchAuthoritySource()],
            registry: new AircraftRegistryCatalogService([new PlayableLoopAircraftRegistrySource()]),
            offer: offer,
            ownedAircraftId: PlayableLoopAircraftRegistrySource.AircraftId);

        CareerJobStartInputSnapshot result = await fixture.Source.ReadAsync(
            offer.OfferId,
            PlayableLoopAircraftRegistrySource.AircraftId,
            selectedOwnershipId: "ownership-one");

        Assert.True(result.IsReady, result.Detail);
        Assert.Null(result.Request!.Contract.ProviderAircraft);
        Assert.Equal("ownership-one", result.Request.SelectedOwnershipId);
    }

    [Fact]
    public async Task ProviderAircraftDoesNotBypassQualificationFailure()
    {
        ProviderAircraftAssignment provider =
            ProviderAssignment();

        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new PersistedJobContractTermsSource()],
                dispatchAuthorities:
                    [new StandardCivilianPointToPointDispatchAuthoritySource()],
                registry:
                    new FakeAircraftRegistrySource(
                        isInstalled:
                            false),
                offer:
                    OfferWithProvider(
                        provider,
                        PrivateQualifications()));

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                provider.AircraftId,
                provider.ProviderAircraftInstanceId);

        Assert.Equal(
            CareerJobStartInputState.QualificationsNotMet,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task ProviderSelectionStillRequiresRegistryResolution()
    {
        ProviderAircraftAssignment provider =
            ProviderAssignment();

        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new PersistedJobContractTermsSource()],
                dispatchAuthorities:
                    [new StandardCivilianPointToPointDispatchAuthoritySource()],
                registry:
                    new MissingAircraftRegistrySource(),
                offer:
                    OfferWithProvider(provider));

        CareerJobStartInputSnapshot snapshot =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                provider.AircraftId,
                provider.ProviderAircraftInstanceId);

        Assert.Equal(
            CareerJobStartInputState.AircraftUnavailable,
            snapshot.State);
        Assert.Null(
            snapshot.Request);
    }

    [Fact]
    public async Task CanonicalMatchAloneDoesNotGrantProviderAuthority()
    {
        ProviderAircraftAssignment provider =
            ProviderAssignment();

        TestFixture fixture =
            CreateFixture(
                contractTerms:
                    [new PersistedJobContractTermsSource()],
                dispatchAuthorities:
                    [new StandardCivilianPointToPointDispatchAuthoritySource()],
                registry:
                    new FakeAircraftRegistrySource(
                        isInstalled:
                            false),
                offer:
                    OfferWithProvider(provider));

        CareerJobStartInputSnapshot ownedSelection =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                provider.AircraftId);

        CareerJobStartInputSnapshot wrongProviderInstance =
            await fixture.Source.ReadAsync(
                fixture.Offer.OfferId,
                provider.AircraftId,
                Guid.Parse(
                    "a7000000-0000-0000-0000-000000000099"));

        Assert.Equal(
            CareerJobStartInputState.AircraftUnavailable,
            ownedSelection.State);
        Assert.Equal(
            CareerJobStartInputState.AircraftUnavailable,
            wrongProviderInstance.State);
    }

    private static TestFixture CreateFixture(
        IReadOnlyList<ICareerJobContractTermsSource> contractTerms,
        IReadOnlyList<ICareerJobDispatchAuthoritySource> dispatchAuthorities,
        DateTimeOffset? acceptedAt = null,
        IAircraftRegistrySource? registry = null,
        JobMarketOfferDraft? offer = null,
        PilotQualificationState? profileQualifications = null,
        string ownedAircraftId = "fixture-aircraft")
    {
        offer ??=
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
                Now.AddDays(-30)) with
            {
                Qualifications =
                    profileQualifications
                    ?? PilotQualificationState.Entry
            };

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
                    Now),
                new TestOwnershipStore(
                    CareerAircraftTestData.Snapshot(
                        profile.CareerId,
                        CareerAircraftTestData.Owned(
                            profile.CareerId, "ownership-one", ownedAircraftId, "Fixture"))));

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

    private static ProviderAircraftAssignment ProviderAssignment() =>
        ProviderAircraftAssignment.CreateForOffer(
            Offer().OfferId,
            new ProviderAircraftType(
                "fixture-aircraft",
                "Fixture Aircraft"),
            "KRME");

    private static PilotQualificationState PrivateQualifications() =>
        new(
            PilotLicenseLevel.Private,
            ImmutableHashSet<PilotRating>.Empty);

    private static JobMarketOfferDraft OfferWithProvider(
        ProviderAircraftAssignment provider,
        PilotQualificationState? requiredQualifications = null)
    {
        JobMarketOfferDraft offer =
            Offer();

        return offer with
        {
            ContractTerms =
                new JobMarketContractTermsEnvelope(
                    PersistedJobContractTermsSource.AuthorityId,
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
                    RequiredPilotQualifications:
                        requiredQualifications
                        ?? PilotQualificationState.Entry,
                    AuthorizedAircraftAccess:
                        AircraftAccess.Civilian,
                    ProviderAircraft:
                        provider)
        };
    }

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
                    AircraftAccess.Civilian,
                ProviderAircraft:
                    offer.ContractTerms?.ProviderAircraft);
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

    private sealed class FakeAircraftRegistrySource(
        bool isInstalled = true)
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
                            isInstalled,
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

    private sealed class MissingAircraftRegistrySource
        : IAircraftRegistrySource
    {
        public Task<AircraftRegistryResolution?> FindAircraftAsync(
            string aircraftId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AircraftRegistryResolution?>(
                null);
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
