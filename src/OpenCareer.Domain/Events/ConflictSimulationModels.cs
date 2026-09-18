using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Events;

public enum ConflictEscalationStage
{
    Normal,
    Surveillance,
    LogisticsBuildup,
    Mobilization,
    ActiveConflict,
    Ceasefire,
    Recovery
}

public enum ConflictBaselineSource
{
    Simulated,
    CuratedRealWorld
}

public enum ConflictCampaignPhase
{
    ActiveConflict,
    Ceasefire,
    Recovery,
    Resolved
}

public enum ConflictResolutionKind
{
    Negotiated,
    Stalemate,
    SideAAdvantage,
    SideBAdvantage
}

public enum ConflictContributionKind
{
    Surveillance,
    CargoLogistics,
    TroopTransport,
    Medical,
    Evacuation,
    Humanitarian,
    Recovery
}

public sealed record ConflictRegionProfile(
    string RegionId,
    double BaselineTension,
    double InternalInstability,
    double BorderSecurityPressure,
    double CivilAviationResilience,
    ConflictBaselineSource BaselineSource = ConflictBaselineSource.Simulated,
    DateTimeOffset? BaselineAsOf = null,
    string? SourceReference = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionId);
        ValidateUnit(BaselineTension, nameof(BaselineTension));
        ValidateUnit(InternalInstability, nameof(InternalInstability));
        ValidateUnit(BorderSecurityPressure, nameof(BorderSecurityPressure));
        ValidateUnit(CivilAviationResilience, nameof(CivilAviationResilience));

        if (!Enum.IsDefined(BaselineSource))
            throw new ArgumentOutOfRangeException(nameof(BaselineSource));

        if (BaselineSource == ConflictBaselineSource.CuratedRealWorld
            && (BaselineAsOf is null || string.IsNullOrWhiteSpace(SourceReference)))
        {
            throw new ArgumentException(
                "Curated real-world baselines require an as-of time and source reference.");
        }
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record ConflictRegionConnection(
    string RegionAId,
    string RegionBId,
    double LandBorderExposure,
    double MaritimeExposure,
    double DisputeFriction,
    double AllianceStrength,
    double EconomicInterdependence)
{
    public string Key =>
        string.CompareOrdinal(RegionAId, RegionBId) <= 0
            ? $"{RegionAId}|{RegionBId}"
            : $"{RegionBId}|{RegionAId}";

    public bool Connects(string regionId) =>
        string.Equals(RegionAId, regionId, StringComparison.Ordinal)
        || string.Equals(RegionBId, regionId, StringComparison.Ordinal);

    public string Other(string regionId)
    {
        if (string.Equals(RegionAId, regionId, StringComparison.Ordinal))
            return RegionBId;
        if (string.Equals(RegionBId, regionId, StringComparison.Ordinal))
            return RegionAId;
        throw new ArgumentException("Region is not part of this connection.", nameof(regionId));
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionAId);
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionBId);
        if (string.Equals(RegionAId, RegionBId, StringComparison.Ordinal))
            throw new ArgumentException("A conflict connection requires two distinct regions.");

        ValidateUnit(LandBorderExposure, nameof(LandBorderExposure));
        ValidateUnit(MaritimeExposure, nameof(MaritimeExposure));
        ValidateUnit(DisputeFriction, nameof(DisputeFriction));
        ValidateUnit(AllianceStrength, nameof(AllianceStrength));
        ValidateUnit(EconomicInterdependence, nameof(EconomicInterdependence));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record CuratedConflictSeed(
    string CampaignId,
    IReadOnlyList<string> AffectedRegionIds,
    string SideAId,
    string SideBId,
    double Severity,
    DateTimeOffset BaselineAsOf,
    string SourceReference)
{
    public void Validate(IReadOnlySet<string> knownRegions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentNullException.ThrowIfNull(AffectedRegionIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(SideAId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SideBId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceReference);

        if (AffectedRegionIds.Count == 0
            || AffectedRegionIds.Distinct(StringComparer.Ordinal).Count() != AffectedRegionIds.Count
            || AffectedRegionIds.Any(region => !knownRegions.Contains(region)))
        {
            throw new ArgumentException("Curated conflict seeds require unique known affected regions.");
        }

        if (string.Equals(SideAId, SideBId, StringComparison.Ordinal))
            throw new ArgumentException("Conflict sides must be distinct.");

        if (!double.IsFinite(Severity) || Severity is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Severity));
    }
}

public sealed record ConflictWorldProfile(
    IReadOnlyList<ConflictRegionProfile> Regions,
    IReadOnlyList<ConflictRegionConnection> Connections,
    IReadOnlyList<CuratedConflictSeed>? CuratedConflicts = null)
{
    public IReadOnlyList<CuratedConflictSeed> EffectiveCuratedConflicts =>
        CuratedConflicts ?? Array.Empty<CuratedConflictSeed>();

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Connections);

        if (Regions.Count == 0)
            throw new ArgumentException("At least one conflict region is required.", nameof(Regions));

        foreach (var region in Regions)
            region.Validate();

        var ids = Regions.Select(region => region.RegionId).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != Regions.Count)
            throw new ArgumentException("Conflict region IDs must be unique.", nameof(Regions));

        foreach (var connection in Connections)
        {
            connection.Validate();
            if (!ids.Contains(connection.RegionAId) || !ids.Contains(connection.RegionBId))
                throw new ArgumentException("Conflict connections must reference known regions.");
        }

        if (Connections.Select(connection => connection.Key).Distinct(StringComparer.Ordinal).Count()
            != Connections.Count)
        {
            throw new ArgumentException("Duplicate conflict-region connection.");
        }

        foreach (var seed in EffectiveCuratedConflicts)
            seed.Validate(ids);

        if (EffectiveCuratedConflicts.Select(seed => seed.CampaignId)
            .Distinct(StringComparer.Ordinal).Count() != EffectiveCuratedConflicts.Count)
        {
            throw new ArgumentException("Curated conflict campaign IDs must be unique.");
        }
    }
}

public sealed record RegionalConflictPosture(
    string RegionId,
    ConflictEscalationStage Stage,
    double Tension,
    double Severity,
    DateTimeOffset StageStartedAt,
    DateTimeOffset UpdatedAt)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionId);
        if (!Enum.IsDefined(Stage))
            throw new ArgumentOutOfRangeException(nameof(Stage));
        if (!double.IsFinite(Tension) || Tension is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Tension));
        if (!double.IsFinite(Severity) || Severity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Severity));
        if (StageStartedAt > UpdatedAt)
            throw new ArgumentException("Conflict posture stage cannot begin after its update time.");
    }
}

public sealed record ConflictCampaignState(
    string CampaignId,
    IReadOnlyList<string> AffectedRegionIds,
    string SideAId,
    string SideBId,
    ConflictCampaignPhase Phase,
    double Severity,
    double OutcomeBalance,
    double ExpectedActiveDurationDays,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PhaseEndsAt = null,
    ConflictResolutionKind? Resolution = null,
    ConflictBaselineSource Origin = ConflictBaselineSource.Simulated,
    string? BaselineSourceReference = null)
{
    public bool IsOperational =>
        Phase is ConflictCampaignPhase.ActiveConflict
            or ConflictCampaignPhase.Ceasefire
            or ConflictCampaignPhase.Recovery;

    public void Validate(IReadOnlySet<string> knownRegions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentNullException.ThrowIfNull(AffectedRegionIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(SideAId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SideBId);

        if (!Enum.IsDefined(Phase)
            || !Enum.IsDefined(Origin)
            || (Resolution is not null && !Enum.IsDefined(Resolution.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(Phase));
        }

        if (AffectedRegionIds.Count == 0
            || AffectedRegionIds.Distinct(StringComparer.Ordinal).Count() != AffectedRegionIds.Count
            || AffectedRegionIds.Any(region => !knownRegions.Contains(region)))
        {
            throw new ArgumentException("Conflict campaigns require unique known affected regions.");
        }

        if (string.Equals(SideAId, SideBId, StringComparison.Ordinal))
            throw new ArgumentException("Conflict sides must be distinct.");
        if (!double.IsFinite(Severity) || Severity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Severity));
        if (!double.IsFinite(OutcomeBalance) || OutcomeBalance is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(OutcomeBalance));
        if (!double.IsFinite(ExpectedActiveDurationDays) || ExpectedActiveDurationDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(ExpectedActiveDurationDays));
        if (UpdatedAt < StartedAt)
            throw new ArgumentException("Campaign update cannot precede campaign start.");
        if (PhaseEndsAt is { } phaseEnd && phaseEnd <= UpdatedAt)
            throw new ArgumentException("Future phase end must follow the update time.");
        if (Origin == ConflictBaselineSource.CuratedRealWorld
            && string.IsNullOrWhiteSpace(BaselineSourceReference))
        {
            throw new ArgumentException("Curated campaign origins require source provenance.");
        }
    }
}

public sealed record SystemicConflictState(
    bool IsActive,
    double Severity,
    DateTimeOffset? StartedAt,
    IReadOnlyList<string> InvolvedRegionIds)
{
    public static SystemicConflictState Inactive { get; } =
        new(false, 0, null, Array.Empty<string>());

    public void Validate(IReadOnlySet<string> knownRegions)
    {
        ArgumentNullException.ThrowIfNull(InvolvedRegionIds);
        if (!double.IsFinite(Severity) || Severity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Severity));
        if (InvolvedRegionIds.Distinct(StringComparer.Ordinal).Count() != InvolvedRegionIds.Count
            || InvolvedRegionIds.Any(region => !knownRegions.Contains(region)))
        {
            throw new ArgumentException("Systemic-conflict regions must be unique known regions.");
        }

        if (IsActive && (StartedAt is null || InvolvedRegionIds.Count == 0 || Severity <= 0))
            throw new ArgumentException("An active systemic conflict requires time, regions and severity.");
        if (!IsActive && (StartedAt is not null || InvolvedRegionIds.Count != 0 || Severity != 0))
            throw new ArgumentException("Inactive systemic-conflict state must be empty.");
    }
}

public sealed record ConflictWorldState(
    int SchemaVersion,
    ulong CareerSeed,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RegionalConflictPosture> Regions,
    IReadOnlyList<ConflictCampaignState> Campaigns,
    SystemicConflictState SystemicConflict)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate(ConflictWorldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("Unsupported conflict simulation checkpoint version.");
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Campaigns);
        ArgumentNullException.ThrowIfNull(SystemicConflict);

        var known = profile.Regions.Select(region => region.RegionId).ToHashSet(StringComparer.Ordinal);
        if (Regions.Count != known.Count
            || Regions.Select(region => region.RegionId).ToHashSet(StringComparer.Ordinal).SetEquals(known) == false)
        {
            throw new ArgumentException("Conflict checkpoint must contain one posture per configured region.");
        }

        foreach (var region in Regions)
        {
            region.Validate();
            if (region.UpdatedAt != UpdatedAt)
                throw new ArgumentException("All regional conflict postures must share the world checkpoint time.");
        }

        if (Campaigns.Select(campaign => campaign.CampaignId).Distinct(StringComparer.Ordinal).Count()
            != Campaigns.Count)
        {
            throw new ArgumentException("Conflict campaign IDs must be unique.");
        }

        foreach (var campaign in Campaigns)
        {
            campaign.Validate(known);
            if (campaign.UpdatedAt != UpdatedAt)
                throw new ArgumentException("All conflict campaigns must share the world checkpoint time.");
        }

        SystemicConflict.Validate(known);
    }
}

public sealed record ConflictPlayerContribution(
    string CampaignId,
    string? SupportedSideId,
    ConflictContributionKind Kind,
    double EffortPoints,
    DateTimeOffset OccurredAt)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        if (!double.IsFinite(EffortPoints) || EffortPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(EffortPoints));

        var neutral = Kind is ConflictContributionKind.Medical
            or ConflictContributionKind.Evacuation
            or ConflictContributionKind.Humanitarian
            or ConflictContributionKind.Recovery;

        if (!neutral && string.IsNullOrWhiteSpace(SupportedSideId))
            throw new ArgumentException("Operational conflict support must identify the supported side.");
    }
}

public sealed record ConflictSimulationPolicy(
    double CuratedBaselineRiskAmplification = 1.15,
    double TensionMeanReversionPerDay = 0.02,
    double TensionNoiseStandardDeviation = 0.015,
    double NeighborConflictTensionBoost = 0.18,
    double SurpriseEscalationAnnualRate = 0.02,
    double SystemicConflictAnnualRate = 0.01,
    int MinimumActiveCampaignsForSystemicConflict = 2,
    int MinimumSystemicConflictDays = 30,
    double MaxPlayerOutcomeShiftPerDay = 0.03,
    double MaxPlayerOutcomeBias = 0.15,
    double MaxHumanitarianSeverityReductionPerDay = 0.02)
{
    public static ConflictSimulationPolicy Default { get; } = new();

    public void Validate()
    {
        ValidateRange(CuratedBaselineRiskAmplification, 1, 2, nameof(CuratedBaselineRiskAmplification));
        ValidateRange(TensionMeanReversionPerDay, 0, 1, nameof(TensionMeanReversionPerDay));
        ValidateRange(TensionNoiseStandardDeviation, 0, 0.25, nameof(TensionNoiseStandardDeviation));
        ValidateRange(NeighborConflictTensionBoost, 0, 1, nameof(NeighborConflictTensionBoost));
        ValidateRange(SurpriseEscalationAnnualRate, 0, 1, nameof(SurpriseEscalationAnnualRate));
        ValidateRange(SystemicConflictAnnualRate, 0, 1, nameof(SystemicConflictAnnualRate));
        ValidateRange(MaxPlayerOutcomeShiftPerDay, 0, 0.25, nameof(MaxPlayerOutcomeShiftPerDay));
        ValidateRange(MaxPlayerOutcomeBias, 0, 0.5, nameof(MaxPlayerOutcomeBias));
        ValidateRange(MaxHumanitarianSeverityReductionPerDay, 0, 0.25, nameof(MaxHumanitarianSeverityReductionPerDay));

        if (MinimumActiveCampaignsForSystemicConflict < 2)
            throw new ArgumentOutOfRangeException(nameof(MinimumActiveCampaignsForSystemicConflict));
        if (MinimumSystemicConflictDays < 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumSystemicConflictDays));
    }

    private static void ValidateRange(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record ConflictAviationDemandProfile(
    double SurveillanceMultiplier,
    double CargoMultiplier,
    double MilitaryTransportMultiplier,
    double FighterOperationsMultiplier,
    double MedicalMultiplier,
    double EvacuationMultiplier,
    double HumanitarianMultiplier,
    double PassengerContinuityMultiplier)
{
    public void Validate()
    {
        var values = new[]
        {
            SurveillanceMultiplier,
            CargoMultiplier,
            MilitaryTransportMultiplier,
            FighterOperationsMultiplier,
            MedicalMultiplier,
            EvacuationMultiplier,
            HumanitarianMultiplier,
            PassengerContinuityMultiplier
        };

        if (values.Any(value => !double.IsFinite(value) || value < 0 || value > 10))
            throw new ArgumentOutOfRangeException(nameof(values));
    }

    public double TrackMultiplier(ServiceTrack track)
    {
        Validate();
        return track switch
        {
            ServiceTrack.MilitaryService =>
                Math.Clamp(
                    (SurveillanceMultiplier + MilitaryTransportMultiplier + FighterOperationsMultiplier) / 3.0,
                    0.25,
                    6.0),
            ServiceTrack.GovernmentContract =>
                Math.Clamp(
                    (SurveillanceMultiplier + CargoMultiplier + MedicalMultiplier + HumanitarianMultiplier) / 4.0,
                    0.25,
                    5.0),
            ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract =>
                Math.Clamp(PassengerContinuityMultiplier, 0.10, 1.50),
            _ => 1
        };
    }

    public double KindMultiplier(ContractKind kind)
    {
        Validate();
        return kind switch
        {
            ContractKind.MilitarySurveillance => SurveillanceMultiplier,
            ContractKind.Survey or ContractKind.Photography => Math.Max(1, SurveillanceMultiplier * 0.75),
            ContractKind.Cargo or ContractKind.ExpressCargo or ContractKind.AogPartsDelivery
                or ContractKind.GovernmentCourier => CargoMultiplier,
            ContractKind.MilitaryTransport or ContractKind.MilitaryFerry => MilitaryTransportMultiplier,
            ContractKind.MilitaryPatrol or ContractKind.MilitaryEscort or ContractKind.MilitaryIntercept
                or ContractKind.MilitaryTankerSupport or ContractKind.MilitaryReadiness
                or ContractKind.MilitaryTraining => FighterOperationsMultiplier,
            ContractKind.Medical or ContractKind.Medevac => MedicalMultiplier,
            ContractKind.Evacuation => EvacuationMultiplier,
            ContractKind.DisasterRelief or ContractKind.SearchAndRescue => HumanitarianMultiplier,
            ContractKind.Passenger or ContractKind.Charter => PassengerContinuityMultiplier,
            _ => 1
        };
    }
}
