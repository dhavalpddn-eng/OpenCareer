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
/// Tuning surface for deterministic airport job boards. Defaults are provisional until playtesting
/// answers the open job-density, range, military-weighting and locked-preview questions.
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
    int MaxEquivalentOffersPerCycle)
{
    public static JobMarketPolicy Default { get; } = new(
        MinimumOffers: 8,
        MaximumOffers: 12,
        RefreshInterval: TimeSpan.FromHours(8),
        DistanceDecayNm: 250,
        MaximumGeneratedDistanceNm: 1_200,
        LongRangeFloorWeight: 0.06,
        EstablishedRouteBoost: 0.60,
        RelationshipBoost: 0.75,
        GovernmentTrackBoost: 1.35,
        MilitaryTrackBoost: 1.80,
        UasSpecialtyBoost: 1.60,
        CivilianEmploymentShare: 0.60,
        IndependentContractShare: 0.25,
        CompanyContractShare: 0.15,
        ShowLockedPreviews: true,
        LockedPreviewWeight: 0.15,
        LocalOperationWeight: 0.35,
        MaxEquivalentOffersPerCycle: 2);

    public void Validate()
    {
        if (MinimumOffers < 1 || MaximumOffers < MinimumOffers || MaximumOffers > 100)
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

        var civilianShare = CivilianEmploymentShare + IndependentContractShare + CompanyContractShare;
        if (Math.Abs(civilianShare - 1.0) > 1e-9)
            throw new ArgumentException("Civilian service-track shares must sum to one.");
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
