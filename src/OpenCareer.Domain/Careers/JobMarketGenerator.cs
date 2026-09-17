using System.Buffers.Binary;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Careers;

public static class JobMarketGenerator
{
    public static IReadOnlyList<JobMarketOfferDraft> Generate(JobMarketGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var policy = request.EffectivePolicy;
        var originIcao = JobMarketIcao.Normalize(request.Origin.Icao);
        var cycleIndex = policy.CycleIndex(request.Time);
        var offeredAt = policy.CycleStart(request.Time);
        var random = DeterministicSeed.CreateStream(
            request.CareerSeed,
            $"job-market:{originIcao}:{cycleIndex}");

        var availableTracks = BuildTrackChoices(request, policy, locked: false);
        var lockedTracks = BuildTrackChoices(request, policy, locked: true);
        if (availableTracks.Count == 0 && lockedTracks.Count == 0)
            return Array.Empty<JobMarketOfferDraft>();

        var requestedCount = policy.VisibleOfferCount(
            request.EffectiveCapacity,
            request.EffectiveCareerStanding.Level,
            random);
        var dreamCount = policy.DreamPreviewCount(requestedCount, lockedTracks.Count > 0);
        var actionableCount = availableTracks.Count == 0 ? 0 : requestedCount - dreamCount;
        if (availableTracks.Count == 0)
            dreamCount = Math.Min(requestedCount, Math.Max(1, dreamCount));

        var offers = new List<JobMarketOfferDraft>(requestedCount);
        var equivalentCounts = new Dictionary<string, int>(StringComparer.Ordinal);

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

        var added = 0;
        var maxAttempts = targetCount * 80;
        for (var attempt = 0; attempt < maxAttempts && added < targetCount; attempt++)
        {
            var track = ChooseWeighted(trackChoices, random);
            var kindChoices = BuildKindChoices(track.Value, request.Origin, policy);
            if (kindChoices.Count == 0)
                continue;
            var kind = ChooseWeighted(kindChoices, random);
            var destination = ChooseDestination(kind.Value, request, policy, random);
            if (destination is null)
                continue;

            var equivalentKey = $"{track.Value}:{kind.Value}:{destination.Icao}:{isLockedPreview}";
            equivalentCounts.TryGetValue(equivalentKey, out var equivalentCount);
            if (equivalentCount >= policy.MaxEquivalentOffersPerCycle)
                continue;
            equivalentCounts[equivalentKey] = equivalentCount + 1;

            var lifetime = policy.OfferLifetime(kind.Value, random);
            offers.Add(new JobMarketOfferDraft(
                CreateGuid(random),
                track.Value,
                kind.Value,
                JobMarketIcao.Normalize(request.Origin.Icao),
                destination.Icao,
                destination.DistanceNm,
                destination.EstimatedFlightHours,
                offeredAt,
                offeredAt + lifetime,
                isLockedPreview,
                destination.RouteStrength,
                destination.RelationshipStrength,
                track.Weight * kind.Weight * destination.Weight));
            added++;
        }
    }

    private static List<Weighted<ServiceTrack>> BuildTrackChoices(
        JobMarketGenerationRequest request,
        JobMarketPolicy policy,
        bool locked)
    {
        var choices = new List<Weighted<ServiceTrack>>();
        foreach (var track in Enum.GetValues<ServiceTrack>())
        {
            var isAvailable = request.Access.Allows(track);
            if (locked == isAvailable)
                continue;

            var weight = policy.TrackWeight(request.Origin, track);
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
        var choices = track switch
        {
            ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract =>
                new List<Weighted<ContractKind>>
                {
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
                },
            ServiceTrack.GovernmentContract =>
                new List<Weighted<ContractKind>>
                {
                    new(ContractKind.GovernmentCourier, 1.00),
                    new(ContractKind.Survey, 0.70),
                    new(ContractKind.Medical, 0.45),
                    new(ContractKind.Medevac, 0.35),
                    new(ContractKind.SearchAndRescue, 0.35),
                    new(ContractKind.DisasterRelief, 0.22),
                    new(ContractKind.Evacuation, 0.18),
                    new(ContractKind.Firefighting, 0.18),
                    new(ContractKind.Ferry, 0.22)
                },
            ServiceTrack.MilitaryService =>
                new List<Weighted<ContractKind>>
                {
                    new(ContractKind.MilitaryTraining, 1.20),
                    new(ContractKind.MilitaryReadiness, 1.00),
                    new(ContractKind.MilitaryPatrol, 0.80),
                    new(ContractKind.MilitarySurveillance, 0.65),
                    new(ContractKind.MilitaryTransport, 0.70),
                    new(ContractKind.MilitaryFerry, 0.50),
                    new(ContractKind.MilitaryEscort, 0.30),
                    new(ContractKind.MilitaryIntercept, 0.25),
                    new(ContractKind.MilitaryTankerSupport, 0.20)
                },
            _ => new List<Weighted<ContractKind>>()
        };

        if (airport.Opportunities.HasFlag(AirportOpportunity.UasResearch) && airport.UasResearchDemand > 0)
        {
            for (var i = 0; i < choices.Count; i++)
            {
                if (choices[i].Value is ContractKind.Survey or ContractKind.Photography or ContractKind.MilitarySurveillance)
                {
                    var boost = 1 + airport.UasResearchDemand * (policy.UasSpecialtyBoost - 1);
                    choices[i] = choices[i] with { Weight = choices[i].Weight * boost };
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
        var choices = new List<Weighted<DestinationChoice>>();
        var originIcao = JobMarketIcao.Normalize(request.Origin.Icao);

        if (SupportsLocalOperation(kind))
        {
            choices.Add(new(
                new DestinationChoice(originIcao, 0, null, 1, 0, policy.LocalOperationWeight),
                policy.LocalOperationWeight));
        }

        foreach (var destination in request.Destinations)
        {
            var icao = destination.NormalizedIcao;
            if (icao == originIcao || destination.DistanceNm > policy.MaximumGeneratedDistanceNm)
                continue;

            var distanceWeight = Math.Exp(-destination.DistanceNm / policy.DistanceDecayNm) + policy.LongRangeFloorWeight;
            var routeWeight = 1 + policy.EstablishedRouteBoost * destination.RouteStrength;
            var relationshipWeight = 1 + policy.RelationshipBoost * destination.RelationshipStrength;
            var kindDistanceSuitability = DistanceSuitability(kind, destination.DistanceNm);
            var durationSuitability = policy.DurationSuitability(
                kind,
                destination.EstimatedFlightHours,
                request.EffectiveCareerStanding.Level);
            var weight = distanceWeight
                * routeWeight
                * relationshipWeight
                * destination.MarketAttractiveness
                * kindDistanceSuitability
                * durationSuitability;
            if (weight <= 0 || !double.IsFinite(weight))
                continue;

            var choice = new DestinationChoice(
                icao,
                destination.DistanceNm,
                destination.EstimatedFlightHours,
                destination.RouteStrength,
                destination.RelationshipStrength,
                weight);
            choices.Add(new(choice, weight));
        }

        return choices.Count == 0 ? null : ChooseWeighted(choices, random).Value;
    }

    private static double DistanceSuitability(ContractKind kind, double distanceNm)
    {
        if (distanceNm <= 0)
            return SupportsLocalOperation(kind) ? 1 : 0;

        if (kind is ContractKind.GliderTow or ContractKind.Skydiving or ContractKind.BannerTow or ContractKind.Agricultural)
        {
            var ratio = distanceNm / 120.0;
            return 1.0 / (1.0 + ratio * ratio);
        }

        if (kind is ContractKind.Ferry or ContractKind.Reposition or ContractKind.MilitaryFerry or ContractKind.MilitaryTransport)
            return 0.80 + Math.Min(distanceNm / 600.0, 1.0) * 0.40;

        return 1;
    }

    private static bool SupportsLocalOperation(ContractKind kind) => kind is
        ContractKind.Survey or ContractKind.Photography or ContractKind.GliderTow or ContractKind.Skydiving or
        ContractKind.BannerTow or ContractKind.Agricultural or ContractKind.Firefighting or
        ContractKind.SearchAndRescue or ContractKind.MilitaryTraining or ContractKind.MilitaryReadiness or
        ContractKind.MilitaryIntercept or ContractKind.MilitaryEscort or ContractKind.MilitaryPatrol or
        ContractKind.MilitarySurveillance or ContractKind.MilitaryTankerSupport;

    private static Weighted<T> ChooseWeighted<T>(IReadOnlyList<Weighted<T>> choices, DeterministicRandom random)
    {
        if (choices.Count == 0)
            throw new InvalidOperationException("At least one weighted choice is required.");

        var total = 0.0;
        foreach (var choice in choices)
        {
            if (!double.IsFinite(choice.Weight) || choice.Weight < 0)
                throw new InvalidOperationException("Job-market weights must be finite and non-negative.");
            total += choice.Weight;
        }
        if (!(total > 0) || !double.IsFinite(total))
            throw new InvalidOperationException("At least one job-market weight must be positive.");

        var target = random.NextDouble(0, total);
        var cumulative = 0.0;
        foreach (var choice in choices)
        {
            cumulative += choice.Weight;
            if (target < cumulative)
                return choice;
        }

        return choices[^1];
    }

    private static Guid CreateGuid(DeterministicRandom random)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, random.NextUInt64());
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], random.NextUInt64());
        return new Guid(bytes);
    }

    private readonly record struct Weighted<T>(T Value, double Weight);
    private sealed record DestinationChoice(
        string Icao,
        double DistanceNm,
        double? EstimatedFlightHours,
        double RouteStrength,
        double RelationshipStrength,
        double Weight);
}
