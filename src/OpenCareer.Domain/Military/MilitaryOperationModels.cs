using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Domain.Military;

[Flags]
public enum MilitaryQualification
{
    None = 0,
    ServiceAuthorization = 1 << 0,
    Mobility = 1 << 1,
    FastJet = 1 << 2,
    Surveillance = 1 << 3,
    Tanker = 1 << 4,
    Aeromedical = 1 << 5,
    SearchAndRescue = 1 << 6,
    Instructor = 1 << 7
}

public enum MilitaryOperationKind
{
    Training,
    Readiness,
    Patrol,
    Surveillance,
    Transport,
    AeromedicalEvacuation,
    SearchAndRescue,
    TankerSupport,
    Escort,
    Intercept,
    AirfieldReinforcement,
    AirSupport
}

public enum MilitaryOperationPhase
{
    Briefed,
    Accepted,
    Preflight,
    EnRoute,
    OnStation,
    Objective,
    Egress,
    Recovery,
    Complete,
    Aborted,
    Failed
}

public sealed record MilitaryAuthorizationProfile(
    MilitaryQualification Qualifications,
    double ServiceTrust)
{
    public static MilitaryAuthorizationProfile None { get; } =
        new(MilitaryQualification.None, 0);

    public bool Has(MilitaryQualification required) =>
        (Qualifications & required) == required;

    public void Validate()
    {
        if ((Qualifications & ~MilitaryQualificationMask.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Qualifications));

        if (!double.IsFinite(ServiceTrust) || ServiceTrust is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(ServiceTrust));
    }
}

internal static class MilitaryQualificationMask
{
    public const MilitaryQualification All =
        MilitaryQualification.ServiceAuthorization |
        MilitaryQualification.Mobility |
        MilitaryQualification.FastJet |
        MilitaryQualification.Surveillance |
        MilitaryQualification.Tanker |
        MilitaryQualification.Aeromedical |
        MilitaryQualification.SearchAndRescue |
        MilitaryQualification.Instructor;
}

public sealed record MilitaryOperationRequirements(
    MilitaryQualification RequiredQualifications,
    AircraftCapability RequiredAircraftCapabilities,
    double MinimumServiceTrust)
{
    public void Validate()
    {
        if ((RequiredQualifications & ~MilitaryQualificationMask.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(RequiredQualifications));

        if (!double.IsFinite(MinimumServiceTrust) || MinimumServiceTrust is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumServiceTrust));
    }
}

public sealed record MilitaryOperationPlan(
    Guid OperationId,
    MilitaryOperationKind Kind,
    string OriginIcao,
    string RecoveryIcao,
    string ObjectiveAreaId,
    MilitaryOperationRequirements Requirements,
    double MinimumOnStationSeconds,
    bool RequiresObjectiveAction,
    string? CampaignId = null,
    string? SupportedSideId = null)
{
    public void Validate()
    {
        if (OperationId == Guid.Empty)
            throw new ArgumentException("Military operation requires an ID.", nameof(OperationId));

        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));

        ValidateIcao(OriginIcao, nameof(OriginIcao));
        ValidateIcao(RecoveryIcao, nameof(RecoveryIcao));
        ArgumentException.ThrowIfNullOrWhiteSpace(ObjectiveAreaId);
        ArgumentNullException.ThrowIfNull(Requirements);
        Requirements.Validate();

        if (!double.IsFinite(MinimumOnStationSeconds) || MinimumOnStationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumOnStationSeconds));

        if (IsSideSpecific(Kind) && string.IsNullOrWhiteSpace(SupportedSideId))
            throw new ArgumentException("Side-specific military operations require a supported side.");

        if (!string.IsNullOrWhiteSpace(SupportedSideId) && string.IsNullOrWhiteSpace(CampaignId))
            throw new ArgumentException("A supported side requires an associated campaign.");
    }

    public static bool IsSideSpecific(MilitaryOperationKind kind) =>
        kind is MilitaryOperationKind.Escort
            or MilitaryOperationKind.Intercept
            or MilitaryOperationKind.AirfieldReinforcement
            or MilitaryOperationKind.AirSupport;

    private static void ValidateIcao(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length != 4 || value.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Military operation airports require normalized four-letter ICAO identifiers.", parameterName);
    }
}

public sealed record MilitaryOperationEligibility(
    bool IsEligible,
    IReadOnlyList<string> Reasons)
{
    public static MilitaryOperationEligibility Allowed() =>
        new(true, Array.Empty<string>());

    public static MilitaryOperationEligibility Denied(params string[] reasons) =>
        new(false, reasons);
}

public static class MilitaryOperationRequirementsCatalog
{
    public static MilitaryOperationRequirements For(MilitaryOperationKind kind) =>
        kind switch
        {
            MilitaryOperationKind.Training => new(
                MilitaryQualification.ServiceAuthorization,
                AircraftCapability.Military,
                0),
            MilitaryOperationKind.Readiness => new(
                MilitaryQualification.ServiceAuthorization,
                AircraftCapability.Military,
                0.10),
            MilitaryOperationKind.Patrol => new(
                MilitaryQualification.ServiceAuthorization,
                AircraftCapability.Military,
                0.20),
            MilitaryOperationKind.Surveillance => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.Surveillance,
                AircraftCapability.Military | AircraftCapability.Surveillance,
                0.25),
            MilitaryOperationKind.Transport => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.Mobility,
                AircraftCapability.Military | AircraftCapability.Cargo,
                0.20),
            MilitaryOperationKind.AeromedicalEvacuation => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.Aeromedical,
                AircraftCapability.Military | AircraftCapability.Medical,
                0.30),
            MilitaryOperationKind.SearchAndRescue => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.SearchAndRescue,
                AircraftCapability.Military,
                0.30),
            MilitaryOperationKind.TankerSupport => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.Tanker,
                AircraftCapability.Military | AircraftCapability.Tanker,
                0.40),
            MilitaryOperationKind.Escort => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.FastJet,
                AircraftCapability.Military | AircraftCapability.Fighter,
                0.45),
            MilitaryOperationKind.Intercept => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.FastJet,
                AircraftCapability.Military | AircraftCapability.Fighter,
                0.50),
            MilitaryOperationKind.AirfieldReinforcement => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.Mobility,
                AircraftCapability.Military | AircraftCapability.Cargo,
                0.45),
            MilitaryOperationKind.AirSupport => new(
                MilitaryQualification.ServiceAuthorization | MilitaryQualification.FastJet,
                AircraftCapability.Military | AircraftCapability.Fighter,
                0.55),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

public static class MilitaryOperationAccessPolicy
{
    public static MilitaryOperationEligibility Evaluate(
        MilitaryOperationPlan operation,
        MilitaryAuthorizationProfile authorization,
        AircraftCapabilityProfile aircraft)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(aircraft);

        operation.Validate();
        authorization.Validate();
        aircraft.Validate();

        var reasons = new List<string>();

        if ((aircraft.Access & AircraftAccess.Military) == 0)
            reasons.Add("Aircraft is not authorized for military service.");

        if (!authorization.Has(operation.Requirements.RequiredQualifications))
            reasons.Add("Required military qualifications are missing.");

        if (authorization.ServiceTrust < operation.Requirements.MinimumServiceTrust)
            reasons.Add("Service trust is below the operation requirement.");

        if (!aircraft.Has(operation.Requirements.RequiredAircraftCapabilities))
            reasons.Add("Aircraft capabilities do not satisfy the operation.");

        return reasons.Count == 0
            ? MilitaryOperationEligibility.Allowed()
            : new MilitaryOperationEligibility(false, reasons);
    }
}
