namespace OpenCareer.Domain.Careers;

public enum AirlineAircraftClass
{
    RegionalJet,
    Narrowbody,
    Widebody
}

public sealed record AirlineEmploymentRequirement(
    AirlineAircraftClass AircraftClass,
    double MinimumVerifiedFlightHours,
    int MinimumEarnedQualifications,
    EmployerTrustTier MinimumEmployerTrust,
    int MinimumCareerLevelForVisibility)
{
    public void Validate()
    {
        if (!Enum.IsDefined(AircraftClass))
            throw new ArgumentOutOfRangeException(nameof(AircraftClass));
        if (!double.IsFinite(MinimumVerifiedFlightHours) || MinimumVerifiedFlightHours < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumVerifiedFlightHours));
        if (MinimumEarnedQualifications < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumEarnedQualifications));
        if (!Enum.IsDefined(MinimumEmployerTrust))
            throw new ArgumentOutOfRangeException(nameof(MinimumEmployerTrust));
        if (MinimumCareerLevelForVisibility < 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumCareerLevelForVisibility));
    }
}

public sealed record AirlineEmploymentAccess(
    AirlineAircraftClass AircraftClass,
    bool IsVisible,
    bool IsQualified,
    bool HasRequiredFlightExperience,
    bool HasRequiredQualifications,
    bool HasRequiredEmployerTrust)
{
    public bool CanFlyEmployerAircraft =>
        IsVisible
        && IsQualified
        && HasRequiredFlightExperience
        && HasRequiredQualifications
        && HasRequiredEmployerTrust;
}

public static class AirlineCareerPolicy
{
    private static readonly IReadOnlyDictionary<AirlineAircraftClass, AirlineEmploymentRequirement>
        Requirements =
            new Dictionary<AirlineAircraftClass, AirlineEmploymentRequirement>
            {
                [AirlineAircraftClass.RegionalJet] =
                    new(
                        AirlineAircraftClass.RegionalJet,
                        MinimumVerifiedFlightHours: 350,
                        MinimumEarnedQualifications: 5,
                        MinimumEmployerTrust: EmployerTrustTier.Trusted,
                        MinimumCareerLevelForVisibility: 24),
                [AirlineAircraftClass.Narrowbody] =
                    new(
                        AirlineAircraftClass.Narrowbody,
                        MinimumVerifiedFlightHours: 500,
                        MinimumEarnedQualifications: 6,
                        MinimumEmployerTrust: EmployerTrustTier.Preferred,
                        MinimumCareerLevelForVisibility: 30),
                [AirlineAircraftClass.Widebody] =
                    new(
                        AirlineAircraftClass.Widebody,
                        MinimumVerifiedFlightHours: 700,
                        MinimumEarnedQualifications: 7,
                        MinimumEmployerTrust: EmployerTrustTier.Partner,
                        MinimumCareerLevelForVisibility: 35)
            };

    public static AirlineEmploymentRequirement GetRequirement(
        AirlineAircraftClass aircraftClass)
    {
        if (!Requirements.TryGetValue(aircraftClass, out var requirement))
            throw new ArgumentOutOfRangeException(nameof(aircraftClass));

        requirement.Validate();
        return requirement;
    }

    public static AirlineEmploymentAccess Evaluate(
        AirlineAircraftClass aircraftClass,
        CareerLevelSnapshot careerLevel,
        double verifiedFlightHours,
        int earnedQualifications,
        EmployerTrustTier employerTrust)
    {
        ArgumentNullException.ThrowIfNull(careerLevel);

        if (!double.IsFinite(verifiedFlightHours) || verifiedFlightHours < 0)
            throw new ArgumentOutOfRangeException(nameof(verifiedFlightHours));
        if (earnedQualifications < 0)
            throw new ArgumentOutOfRangeException(nameof(earnedQualifications));
        if (!Enum.IsDefined(employerTrust))
            throw new ArgumentOutOfRangeException(nameof(employerTrust));

        AirlineEmploymentRequirement requirement =
            GetRequirement(aircraftClass);

        bool visible =
            careerLevel.Level >= requirement.MinimumCareerLevelForVisibility;

        bool hoursReady =
            verifiedFlightHours >= requirement.MinimumVerifiedFlightHours;

        bool qualificationsReady =
            earnedQualifications >= requirement.MinimumEarnedQualifications;

        bool trustReady =
            employerTrust >= requirement.MinimumEmployerTrust;

        return new AirlineEmploymentAccess(
            aircraftClass,
            visible,
            qualificationsReady,
            hoursReady,
            qualificationsReady,
            trustReady);
    }
}
