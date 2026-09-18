using OpenCareer.Domain.Careers;

namespace OpenCareer.Domain.Events;

public enum AirfieldOperationalStatus
{
    Open,
    HeightenedSecurity,
    Restricted,
    Contested,
    Secured,
    Damaged,
    Reopening,
    Closed
}

public enum AirfieldContributionKind
{
    Reconnaissance,
    CargoLogistics,
    TroopLift,
    Medical,
    Evacuation,
    Humanitarian,
    Engineering,
    RecoverySupply
}

public sealed record AirfieldControlPolicy(
    double MaxPlayerControlShiftPerDay = 0.025,
    double MaxPlayerRepairPerDay = 0.05,
    double SecureControlThreshold = 0.70,
    double ContestedControlThreshold = 0.20,
    double MinimumRunwayForOperations = 0.35,
    double MinimumGroundServicesForCivilianOperations = 0.30)
{
    public static AirfieldControlPolicy Default { get; } = new();

    public void Validate()
    {
        ValidateUnit(MaxPlayerControlShiftPerDay, nameof(MaxPlayerControlShiftPerDay));
        ValidateUnit(MaxPlayerRepairPerDay, nameof(MaxPlayerRepairPerDay));
        ValidateUnit(SecureControlThreshold, nameof(SecureControlThreshold));
        ValidateUnit(ContestedControlThreshold, nameof(ContestedControlThreshold));
        ValidateUnit(MinimumRunwayForOperations, nameof(MinimumRunwayForOperations));
        ValidateUnit(MinimumGroundServicesForCivilianOperations, nameof(MinimumGroundServicesForCivilianOperations));

        if (ContestedControlThreshold >= SecureControlThreshold)
            throw new ArgumentException("Contested threshold must be below secure threshold.");
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record AirfieldControlState(
    int SchemaVersion,
    string AirportIcao,
    string RegionId,
    AirfieldOperationalStatus Status,
    string? ControllingSideId,
    double ControlBalance,
    double RunwayServiceability,
    double GroundServicesCapacity,
    double SecurityPressure,
    DateTimeOffset UpdatedAt)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("Unsupported airfield-control schema version.");

        ValidateIcao(AirportIcao);
        ArgumentException.ThrowIfNullOrWhiteSpace(RegionId);

        if (!Enum.IsDefined(Status))
            throw new ArgumentOutOfRangeException(nameof(Status));

        ValidateRange(ControlBalance, -1, 1, nameof(ControlBalance));
        ValidateRange(RunwayServiceability, 0, 1, nameof(RunwayServiceability));
        ValidateRange(GroundServicesCapacity, 0, 1, nameof(GroundServicesCapacity));
        ValidateRange(SecurityPressure, 0, 1, nameof(SecurityPressure));

        if (Status == AirfieldOperationalStatus.Contested && !string.IsNullOrWhiteSpace(ControllingSideId))
            throw new ArgumentException("A contested airfield cannot have a controlling side.");

        if (Status == AirfieldOperationalStatus.Secured && string.IsNullOrWhiteSpace(ControllingSideId))
            throw new ArgumentException("A secured airfield requires a controlling side.");
    }

    public bool CanAcceptMilitaryLogistics(AirfieldControlPolicy? policy = null)
    {
        var p = policy ?? AirfieldControlPolicy.Default;
        p.Validate();
        Validate();

        return (Status is AirfieldOperationalStatus.Secured
            or AirfieldOperationalStatus.Restricted
            or AirfieldOperationalStatus.Reopening
            or AirfieldOperationalStatus.HeightenedSecurity)
            && RunwayServiceability >= p.MinimumRunwayForOperations;
    }

    public bool CanAcceptHumanitarianFlights(AirfieldControlPolicy? policy = null)
    {
        var p = policy ?? AirfieldControlPolicy.Default;
        p.Validate();
        Validate();

        return Status is not AirfieldOperationalStatus.Closed
            && Status is not AirfieldOperationalStatus.Contested
            && RunwayServiceability >= p.MinimumRunwayForOperations;
    }

    public bool CanAcceptCivilianFlights(AirfieldControlPolicy? policy = null)
    {
        var p = policy ?? AirfieldControlPolicy.Default;
        p.Validate();
        Validate();

        return (Status is AirfieldOperationalStatus.Open
            or AirfieldOperationalStatus.HeightenedSecurity
            or AirfieldOperationalStatus.Reopening
            or AirfieldOperationalStatus.Secured)
            && RunwayServiceability >= p.MinimumRunwayForOperations
            && GroundServicesCapacity >= p.MinimumGroundServicesForCivilianOperations;
    }

    private static void ValidateIcao(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 4 || value.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Airfield control requires a normalized four-letter ICAO.", nameof(AirportIcao));
    }

    private static void ValidateRange(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record AirfieldDailyContext(
    string CampaignId,
    string SideAId,
    string SideBId,
    ConflictCampaignPhase CampaignPhase,
    double CampaignSeverity,
    double BackgroundControlPressure,
    double InfrastructureDamagePressure,
    double CivilAviationResilience)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SideAId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SideBId);

        if (string.Equals(SideAId, SideBId, StringComparison.Ordinal))
            throw new ArgumentException("Airfield-control campaign sides must be distinct.");

        if (!Enum.IsDefined(CampaignPhase))
            throw new ArgumentOutOfRangeException(nameof(CampaignPhase));

        ValidateRange(CampaignSeverity, 0, 1, nameof(CampaignSeverity));
        ValidateRange(BackgroundControlPressure, -1, 1, nameof(BackgroundControlPressure));
        ValidateRange(InfrastructureDamagePressure, 0, 1, nameof(InfrastructureDamagePressure));
        ValidateRange(CivilAviationResilience, 0, 1, nameof(CivilAviationResilience));
    }

    private static void ValidateRange(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record AirfieldMissionContribution(
    string CampaignId,
    string AirportIcao,
    string? SupportedSideId,
    AirfieldContributionKind Kind,
    double EffortPoints,
    DateTimeOffset OccurredAt)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CampaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AirportIcao);

        if (AirportIcao.Length != 4 || AirportIcao.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("A normalized four-letter ICAO is required.", nameof(AirportIcao));

        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));

        if (!double.IsFinite(EffortPoints) || EffortPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(EffortPoints));

        var neutral = Kind is AirfieldContributionKind.Medical
            or AirfieldContributionKind.Evacuation
            or AirfieldContributionKind.Humanitarian
            or AirfieldContributionKind.Engineering
            or AirfieldContributionKind.RecoverySupply;

        if (!neutral && string.IsNullOrWhiteSpace(SupportedSideId))
            throw new ArgumentException("Side-specific operational support requires a supported side.");
    }
}
