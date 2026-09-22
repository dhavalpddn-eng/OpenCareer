using System.Buffers.Binary;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Careers;

public static class JobMarketGenerator
{
    private static readonly ServiceTrack[] SupportedTracks =
    [
        ServiceTrack.CivilianEmployment,
        ServiceTrack.IndependentContract,
        ServiceTrack.CompanyContract,
        ServiceTrack.GovernmentContract
    ];

    public static IReadOnlyList<JobMarketOfferDraft> Generate(
        JobMarketGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        JobMarketPolicy policy = request.EffectivePolicy;
        string originIcao = JobMarketIcao.Normalize(request.Origin.Icao);
        long cycleIndex = policy.CycleIndex(request.Time);
        DateTimeOffset offeredAt = policy.CycleStart(request.Time);

        DeterministicRandom random =
            DeterministicSeed.CreateStream(
                request.GenerationSeed,
                $"job-market:{originIcao}:{cycleIndex}");

        List<Weighted<ServiceTrack>> availableTracks =
            BuildTrackChoices(request, policy, locked: false);

        if (availableTracks.Count == 0)
            return Array.Empty<JobMarketOfferDraft>();

        List<Weighted<ServiceTrack>> lockedTracks =
            BuildTrackChoices(request, policy, locked: true);

        int requestedCount =
            policy.VisibleOfferCount(
                request.EffectiveCapacity,
                request.CareerLevel,
                random);

        int dreamCount =
            policy.DreamPreviewCount(
                requestedCount,
                lockedTracks.Count > 0);

        int actionableCount =
            requestedCount - dreamCount;

        var offers =
            new List<JobMarketOfferDraft>(requestedCount);

        var equivalentCounts =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        GenerateOffers(
            actionableCount,
            isLockedPreview: false,
            availableTracks,
            request,
            policy,
            random,
            offeredAt,
            equivalentCounts,
            offers);

        GenerateOffers(
            dreamCount,
            isLockedPreview: true,
            lockedTracks,
            request,
            policy,
            random,
            offeredAt,
            equivalentCounts,
            offers);

        return offers;
    }

    private static void GenerateOffers(
        int targetCount,
        bool isLockedPreview,
        IReadOnlyList<Weighted<ServiceTrack>> trackChoices,
        JobMarketGenerationRequest request,
        JobMarketPolicy policy,
        DeterministicRandom random,
        DateTimeOffset offeredAt,
        IDictionary<string, int> equivalentCounts,
        ICollection<JobMarketOfferDraft> offers)
    {
        if (targetCount <= 0 || trackChoices.Count == 0)
            return;

        int added = 0;
        int maxAttempts = targetCount * 80;

        for (int attempt = 0;
            attempt < maxAttempts && added < targetCount;
            attempt++)
        {
            Weighted<ServiceTrack> track =
                ChooseWeighted(trackChoices, random);

            List<Weighted<ContractKind>> kindChoices =
                BuildKindChoices(
                    track.Value,
                    request.Origin,
                    policy);

            if (kindChoices.Count == 0)
                continue;

            Weighted<ContractKind> kind =
                ChooseWeighted(kindChoices, random);

            DestinationChoice? destination =
                ChooseDestination(
                    kind.Value,
                    request,
                    policy,
                    random);

            if (destination is null)
                continue;

            string equivalentKey =
                $"{track.Value}:{kind.Value}:{destination.Icao}:{isLockedPreview}";

            equivalentCounts.TryGetValue(
                equivalentKey,
                out int equivalentCount);

            if (equivalentCount
                >= policy.MaxEquivalentOffersPerCycle)
            {
                continue;
            }

            equivalentCounts[equivalentKey] =
                equivalentCount + 1;

            TimeSpan lifetime =
                policy.OfferLifetime(
                    kind.Value,
                    JobScenarioKind.Standard,
                    random);

            offers.Add(
                new JobMarketOfferDraft(
                    CreateGuid(random),
                    track.Value,
                    kind.Value,
                    JobScenarioKind.Standard,
                    JobMarketIcao.Normalize(request.Origin.Icao),
                    destination.Icao,
                    destination.DistanceNm,
                    destination.EstimatedFlightHours,
                    offeredAt,
                    offeredAt + lifetime,
                    isLockedPreview,
                    destination.RouteStrength,
                    destination.RelationshipStrength,
                    track.Weight
                        * kind.Weight
                        * destination.Weight));

            added++;
        }
    }

    private static List<Weighted<ServiceTrack>> BuildTrackChoices(
        JobMarketGenerationRequest request,
        JobMarketPolicy policy,
        bool locked)
    {
        var choices =
            new List<Weighted<ServiceTrack>>();

        foreach (ServiceTrack track in SupportedTracks)
        {
            bool isAvailable =
                request.Access.Allows(track);

            if (locked == isAvailable)
                continue;

            double weight =
                policy.TrackWeight(
                    request.Origin,
                    track);

            if (weight <= 0)
                continue;

            if (locked)
                weight *= policy.LockedPreviewWeight;

            if (weight > 0)
                choices.Add(new(track, weight));
        }

        return choices;
    }

    private static List<Weighted<ContractKind>> BuildKindChoices(
        ServiceTrack track,
        AirportCareerProfile airport,
        JobMarketPolicy policy)
    {
        List<Weighted<ContractKind>> choices =
            track switch
            {
                ServiceTrack.CivilianEmployment
                    or ServiceTrack.IndependentContract
                    or ServiceTrack.CompanyContract =>
                    [
                        new(ContractKind.Cargo, 1.20),
                        new(ContractKind.ExpressCargo, 0.80),
                        new(ContractKind.AogPartsDelivery, 0.40),
                        new(ContractKind.Passenger, 1.00),
                        new(ContractKind.Charter, 0.85),
                        new(ContractKind.Ferry, 0.55),
                        new(ContractKind.Reposition, 0.55),
                        new(ContractKind.Medical, 0.25),
                        new(ContractKind.Survey, 0.30),
                        new(ContractKind.Photography, 0.22),
                        new(ContractKind.GliderTow, 0.10),
                        new(ContractKind.Skydiving, 0.10),
                        new(ContractKind.BannerTow, 0.08),
                        new(ContractKind.Agricultural, 0.08),
                        new(ContractKind.Firefighting, 0.05)
                    ],
                ServiceTrack.GovernmentContract =>
                    [
                        new(ContractKind.GovernmentCourier, 1.00),
                        new(ContractKind.Survey, 0.70),
                        new(ContractKind.Medical, 0.45),
                        new(ContractKind.Medevac, 0.35),
                        new(ContractKind.SearchAndRescue, 0.35),
                        new(ContractKind.DisasterRelief, 0.22),
                        new(ContractKind.Evacuation, 0.18),
                        new(ContractKind.Firefighting, 0.18),
                        new(ContractKind.Ferry, 0.22),
                        new(ContractKind.Cargo, 0.30)
                    ],
                _ => []
            };

        if (airport.Opportunities
                .HasFlag(AirportOpportunity.UasResearch)
            && airport.UasResearchDemand > 0)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].Value is
                    ContractKind.Survey
                    or ContractKind.Photography)
                {
                    double boost =
                        1
                        + airport.UasResearchDemand
                        * (policy.UasSpecialtyBoost - 1);

                    choices[i] =
                        choices[i] with
                        {
                            Weight =
                                choices[i].Weight * boost
                        };
                }
            }
        }

        return choices;
    }

    private static DestinationChoice? ChooseDestination(
        ContractKind kind,
        JobMarketGenerationRequest request,
        JobMarketPolicy policy,
        DeterministicRandom random)
    {
        var choices =
            new List<Weighted<DestinationChoice>>();

        string originIcao =
            JobMarketIcao.Normalize(request.Origin.Icao);

        if (SupportsLocalOperation(kind))
        {
            choices.Add(
                new(
                    new DestinationChoice(
                        originIcao,
                        0,
                        null,
                        0,
                        0,
                        policy.LocalOperationWeight),
                    policy.LocalOperationWeight));
        }

        foreach (JobMarketDestination destination
            in request.Destinations)
        {
            string icao =
                destination.NormalizedIcao;

            if (icao == originIcao
                || destination.DistanceNm
                    > policy.MaximumGeneratedDistanceNm)
            {
                continue;
            }

            double distanceWeight =
                Math.Exp(
                    -destination.DistanceNm
                    / policy.DistanceDecayNm)
                + policy.LongRangeFloorWeight;

            double routeWeight =
                1
                + policy.EstablishedRouteBoost
                * destination.RouteStrength;

            double relationshipWeight =
                1
                + policy.RelationshipBoost
                * destination.RelationshipStrength;

            double kindDistanceSuitability =
                DistanceSuitability(
                    kind,
                    destination.DistanceNm);

            double durationSuitability =
                policy.DurationSuitability(
                    kind,
                    destination.EstimatedFlightHours,
                    request.CareerLevel);

            double demandAttractiveness =
                DemandAttractiveness(
                    kind,
                    destination.DemandProfile);

            double weight =
                distanceWeight
                * routeWeight
                * relationshipWeight
                * destination.MarketAttractiveness
                * demandAttractiveness
                * kindDistanceSuitability
                * durationSuitability;

            if (weight <= 0 || !double.IsFinite(weight))
                continue;

            var choice =
                new DestinationChoice(
                    icao,
                    destination.DistanceNm,
                    destination.EstimatedFlightHours,
                    destination.RouteStrength,
                    destination.RelationshipStrength,
                    weight);

            choices.Add(new(choice, weight));
        }

        return choices.Count == 0
            ? null
            : ChooseWeighted(choices, random).Value;
    }

    private static double DemandAttractiveness(
        ContractKind kind,
        RouteDemandProfile? demand)
    {
        if (demand is null)
            return 1;

        return kind switch
        {
            ContractKind.Passenger
                or ContractKind.Charter
                or ContractKind.Evacuation =>
                demand.PassengerAttractiveness,
            ContractKind.Cargo =>
                demand.CargoAttractiveness(),
            ContractKind.ExpressCargo =>
                demand.CargoAttractiveness(
                    CargoCommodityCategory.ExpressParcel),
            ContractKind.AogPartsDelivery =>
                demand.CargoAttractiveness(
                    CargoCommodityCategory.AircraftAogParts),
            ContractKind.Medical =>
                demand.CargoAttractiveness(
                    CargoCommodityCategory.MedicalSupplies),
            ContractKind.DisasterRelief =>
                demand.CargoAttractiveness(
                    CargoCommodityCategory.HumanitarianSupplies),
            _ => 1
        };
    }

    private static double DistanceSuitability(
        ContractKind kind,
        double distanceNm)
    {
        if (distanceNm <= 0)
            return SupportsLocalOperation(kind) ? 1 : 0;

        if (kind is
            ContractKind.GliderTow
            or ContractKind.Skydiving
            or ContractKind.BannerTow
            or ContractKind.Agricultural)
        {
            double ratio =
                distanceNm / 120.0;

            return 1.0
                / (1.0 + ratio * ratio);
        }

        if (kind is
            ContractKind.Ferry
            or ContractKind.Reposition)
        {
            return 0.80
                + Math.Min(
                    distanceNm / 600.0,
                    1.0)
                * 0.40;
        }

        return 1;
    }

    private static bool SupportsLocalOperation(
        ContractKind kind) =>
        kind is
            ContractKind.Survey
            or ContractKind.Photography
            or ContractKind.GliderTow
            or ContractKind.Skydiving
            or ContractKind.BannerTow
            or ContractKind.Agricultural
            or ContractKind.Firefighting
            or ContractKind.SearchAndRescue;

    private static Weighted<T> ChooseWeighted<T>(
        IReadOnlyList<Weighted<T>> choices,
        DeterministicRandom random)
    {
        if (choices.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one weighted choice is required.");
        }

        double total = 0;

        foreach (Weighted<T> choice in choices)
        {
            if (!double.IsFinite(choice.Weight)
                || choice.Weight < 0)
            {
                throw new InvalidOperationException(
                    "Job-market weights must be finite and non-negative.");
            }

            total += choice.Weight;
        }

        if (!(total > 0)
            || !double.IsFinite(total))
        {
            throw new InvalidOperationException(
                "At least one job-market weight must be positive.");
        }

        double target =
            random.NextDouble(0, total);

        double cumulative = 0;

        foreach (Weighted<T> choice in choices)
        {
            cumulative += choice.Weight;

            if (target < cumulative)
                return choice;
        }

        return choices[^1];
    }

    private static Guid CreateGuid(
        DeterministicRandom random)
    {
        Span<byte> bytes =
            stackalloc byte[16];

        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes,
            random.NextUInt64());

        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes[8..],
            random.NextUInt64());

        return new Guid(bytes);
    }

    private readonly record struct Weighted<T>(
        T Value,
        double Weight);

    private sealed record DestinationChoice(
        string Icao,
        double DistanceNm,
        double? EstimatedFlightHours,
        double RouteStrength,
        double RelationshipStrength,
        double Weight);
}
