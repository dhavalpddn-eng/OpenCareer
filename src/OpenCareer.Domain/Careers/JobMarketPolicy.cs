using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Careers;

[Flags]
public enum JobMarketAccess
{
    None = 0,
    CivilianEmployment = 1 << 0,
    IndependentContract = 1 << 1,
    CompanyContract = 1 << 2,
    GovernmentContract = 1 << 3,
    MilitaryService = 1 << 4,
    All = CivilianEmployment | IndependentContract | CompanyContract | GovernmentContract | MilitaryService
}

public static class JobMarketAccessExtensions
{
    public static bool Allows(this JobMarketAccess access, ServiceTrack track) => track switch
    {
        ServiceTrack.CivilianEmployment => access.HasFlag(JobMarketAccess.CivilianEmployment),
        ServiceTrack.IndependentContract => access.HasFlag(JobMarketAccess.IndependentContract),
        ServiceTrack.CompanyContract => access.HasFlag(JobMarketAccess.CompanyContract),
        ServiceTrack.GovernmentContract => access.HasFlag(JobMarketAccess.GovernmentContract),
        ServiceTrack.MilitaryService => access.HasFlag(JobMarketAccess.MilitaryService),
        _ => false
    };
}

/// <summary>
/// Deterministic airport job-board tuning. Level changes market visibility only; downstream contract
/// dispatch rules remain authoritative for licenses, aircraft, authorization and feasibility.
/// </summary>
public sealed record JobMarketPolicy(
    int MinimumOffers,
    int MaximumOffers,
    TimeSpan RefreshInterval,
    double DistanceDecayNm,
    double MaximumGeneratedDistanceNm,
    double LongRangeFloorWeight,
    double EstablishedRouteBoost,
    double RelationshipBoost,
    double GovernmentTrackBoost,
    double MilitaryTrackBoost,
    double UasSpecialtyBoost,
    double CivilianEmploymentShare,
    double IndependentContractShare,
    double CompanyContractShare,
    bool ShowLockedPreviews,
    double LockedPreviewWeight,
    double LocalOperationWeight,
    int MaxEquivalentOffersPerCycle,
    double BoardGrowthExponent = 0.82,
    int CareerLevelCap = 50,
    int MaximumDreamPreviews = 2,
    int FirstDreamPreviewBoardSize = 4,
    int SecondDreamPreviewBoardSize = 12,
    TimeSpan? MinimumOfferLifetime = null,
    TimeSpan? MaximumOfferLifetime = null,
    int EarlyCareerLevelCeiling = 10,
    double EarlyCareerPreferredMinHours = 2,
    double EarlyCareerPreferredMaxHours = 4,
    double EarlyCareerOutOfBandWeight = 0.30)
{
    public static JobMarketPolicy Default { get; } = new(
        MinimumOffers: 1,
        MaximumOffers: 2,
        RefreshInterval: TimeSpan.FromHours(1),
        DistanceDecayNm: 350,
        MaximumGeneratedDistanceNm: 1_500,
        LongRangeFloorWeight: 0.04,
        EstablishedRouteBoost: 0.85,
        RelationshipBoost: 1.10,
        GovernmentTrackBoost: 1.35,
        MilitaryTrackBoost: 1.80,
        UasSpecialtyBoost: 1.60,
        CivilianEmploymentShare: 0.60,
        IndependentContractShare: 0.25,
        CompanyContractShare: 0.15,
        ShowLockedPreviews: true,
        LockedPreviewWeight: 0.15,
        LocalOperationWeight: 0.35,
        MaxEquivalentOffersPerCycle: 2,
        MinimumOfferLifetime: TimeSpan.FromHours(2),
        MaximumOfferLifetime: TimeSpan.FromHours(18));

    public TimeSpan EffectiveMinimumOfferLifetime => MinimumOfferLifetime ?? TimeSpan.FromHours(2);
    public TimeSpan EffectiveMaximumOfferLifetime => MaximumOfferLifetime ?? TimeSpan.FromHours(18);

    public void Validate()
    {
        if (MinimumOffers < 1 || MaximumOffers < MinimumOffers || MaximumOffers > 10)
            throw new ArgumentOutOfRangeException(nameof(MinimumOffers));
        if (RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshInterval));

        RequirePositiveFinite(DistanceDecayNm, nameof(DistanceDecayNm));
        RequirePositiveFinite(MaximumGeneratedDistanceNm, nameof(MaximumGeneratedDistanceNm));
        RequireUnit(LongRangeFloorWeight, nameof(LongRangeFloorWeight));
        RequireNonNegativeFinite(EstablishedRouteBoost, nameof(EstablishedRouteBoost));
        RequireNonNegativeFinite(RelationshipBoost, nameof(RelationshipBoost));
        RequirePositiveFinite(GovernmentTrackBoost, nameof(GovernmentTrackBoost));
        RequirePositiveFinite(MilitaryTrackBoost, nameof(MilitaryTrackBoost));
        RequirePositiveFinite(UasSpecialtyBoost, nameof(UasSpecialtyBoost));
        RequireUnit(CivilianEmploymentShare, nameof(CivilianEmploymentShare));
        RequireUnit(IndependentContractShare, nameof(IndependentContractShare));
        RequireUnit(CompanyContractShare, nameof(CompanyContractShare));
        RequireUnit(LockedPreviewWeight, nameof(LockedPreviewWeight));
        RequirePositiveFinite(LocalOperationWeight, nameof(LocalOperationWeight));
        if (MaxEquivalentOffersPerCycle < 1 || MaxEquivalentOffersPerCycle > 10)
            throw new ArgumentOutOfRangeException(nameof(MaxEquivalentOffersPerCycle));
        if (!double.IsFinite(BoardGrowthExponent) || BoardGrowthExponent <= 0)
            throw new ArgumentOutOfRangeException(nameof(BoardGrowthExponent));
        if (CareerLevelCap is < 2 or > 500)
            throw new ArgumentOutOfRangeException(nameof(CareerLevelCap));
        if (MaximumDreamPreviews is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(MaximumDreamPreviews));
        if (FirstDreamPreviewBoardSize < 1 || SecondDreamPreviewBoardSize < FirstDreamPreviewBoardSize)
            throw new ArgumentOutOfRangeException(nameof(FirstDreamPreviewBoardSize));
        if (EffectiveMinimumOfferLifetime <= TimeSpan.Zero || EffectiveMaximumOfferLifetime < EffectiveMinimumOfferLifetime)
            throw new ArgumentOutOfRangeException(nameof(MinimumOfferLifetime));
        if (EarlyCareerLevelCeiling < 1 || EarlyCareerLevelCeiling > CareerLevelCap)
            throw new ArgumentOutOfRangeException(nameof(EarlyCareerLevelCeiling));
        RequirePositiveFinite(EarlyCareerPreferredMinHours, nameof(EarlyCareerPreferredMinHours));
        if (!double.IsFinite(EarlyCareerPreferredMaxHours) || EarlyCareerPreferredMaxHours < EarlyCareerPreferredMinHours)
            throw new ArgumentOutOfRangeException(nameof(EarlyCareerPreferredMaxHours));
        RequireUnit(EarlyCareerOutOfBandWeight, nameof(EarlyCareerOutOfBandWeight));

        var civilianShare = CivilianEmploymentShare + IndependentContractShare + CompanyContractShare;
        if (Math.Abs(civilianShare - 1.0) > 1e-9)
            throw new ArgumentException("Civilian service-track shares must sum to one.");
    }

    public int VisibleOfferCount(AirportMarketCapacity capacity, int careerLevel, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(random);
        Validate();
        capacity.Validate();
        if (careerLevel < 1 || careerLevel > CareerLevelCap)
            throw new ArgumentOutOfRangeException(nameof(careerLevel));

        var startingCount = random.NextInt(MinimumOffers, MaximumOffers + 1);
        if (careerLevel == 1)
            return Math.Min(startingCount, capacity.MatureVisibleOfferCapacity);

        var progress = (careerLevel - 1.0) / (CareerLevelCap - 1.0);
        var grown = startingCount
            + (capacity.MatureVisibleOfferCapacity - startingCount) * Math.Pow(progress, BoardGrowthExponent);
        return Math.Clamp((int)Math.Round(grown, MidpointRounding.AwayFromZero), 1, capacity.MatureVisibleOfferCapacity);
    }

    public int DreamPreviewCount(int boardCount, bool lockedTracksExist)
    {
        Validate();
        if (!ShowLockedPreviews || !lockedTracksExist || MaximumDreamPreviews == 0)
            return 0;
        if (boardCount >= SecondDreamPreviewBoardSize)
            return Math.Min(2, MaximumDreamPreviews);
        if (boardCount >= FirstDreamPreviewBoardSize)
            return 1;
        return 0;
    }

    public TimeSpan OfferLifetime(ContractKind kind, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Validate();

        var min = EffectiveMinimumOfferLifetime;
        var max = EffectiveMaximumOfferLifetime;
        if (kind is ContractKind.ExpressCargo or ContractKind.AogPartsDelivery or ContractKind.Medical or ContractKind.Medevac)
        {
            min = TimeSpan.FromMinutes(45);
            max = TimeSpan.FromHours(5);
        }

        var seconds = random.NextDouble(min.TotalSeconds, max.TotalSeconds + 0.000001);
        return TimeSpan.FromSeconds(seconds);
    }

    public double DurationSuitability(ContractKind kind, double? estimatedFlightHours, int careerLevel)
    {
        Validate();
        if (estimatedFlightHours is null)
            return 1;
        var hours = estimatedFlightHours.Value;
        if (!double.IsFinite(hours) || hours < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedFlightHours));
        if (careerLevel > EarlyCareerLevelCeiling || hours == 0)
            return 1;
        if (hours >= EarlyCareerPreferredMinHours && hours <= EarlyCareerPreferredMaxHours)
            return 1.35;

        var distanceFromBand = hours < EarlyCareerPreferredMinHours
            ? EarlyCareerPreferredMinHours - hours
            : hours - EarlyCareerPreferredMaxHours;
        var weight = Math.Max(EarlyCareerOutOfBandWeight, Math.Exp(-distanceFromBand / 1.5));
        if (kind is ContractKind.Ferry or ContractKind.Reposition or ContractKind.MilitaryFerry or ContractKind.MilitaryTransport)
            weight = Math.Max(weight, 0.60);
        return weight;
    }

    public double TrackWeight(AirportCareerProfile airport, ServiceTrack track)
    {
        ArgumentNullException.ThrowIfNull(airport);
        Validate();
        airport.Validate();

        return track switch
        {
            ServiceTrack.CivilianEmployment => airport.DemandFor(track) * CivilianEmploymentShare,
            ServiceTrack.IndependentContract => airport.DemandFor(track) * IndependentContractShare,
            ServiceTrack.CompanyContract => airport.DemandFor(track) * CompanyContractShare,
            ServiceTrack.GovernmentContract => airport.DemandFor(track) * GovernmentTrackBoost,
            ServiceTrack.MilitaryService => airport.DemandFor(track) * MilitaryTrackBoost,
            _ => 0
        };
    }

    public long CycleIndex(DateTimeOffset time)
    {
        Validate();
        return time.UtcDateTime.Ticks / RefreshInterval.Ticks;
    }

    public DateTimeOffset CycleStart(DateTimeOffset time)
    {
        var index = CycleIndex(time);
        var ticks = checked(index * RefreshInterval.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static void RequireUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void RequirePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void RequireNonNegativeFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
