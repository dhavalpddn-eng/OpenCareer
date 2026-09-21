namespace OpenCareer.Domain.Careers;

[Flags]
public enum AirportOpportunity
{
    None = 0,
    Civilian = 1 << 0,
    Government = 1 << 1,
    Military = 1 << 2,
    UasResearch = 1 << 3,
    EmergencyServices = 1 << 4
}

public sealed record AirportCareerProfile(
    string Icao,
    string Name,
    AirportOpportunity Opportunities,
    double CivilianDemand,
    double GovernmentDemand,
    double MilitaryDemand,
    double UasResearchDemand,
    decimal MonthlyStorageCostIndex,
    double StorageScarcity)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Icao) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Airport identity is required.");
        ValidateUnit(CivilianDemand, nameof(CivilianDemand));
        ValidateUnit(GovernmentDemand, nameof(GovernmentDemand));
        ValidateUnit(MilitaryDemand, nameof(MilitaryDemand));
        ValidateUnit(UasResearchDemand, nameof(UasResearchDemand));
        ValidateUnit(StorageScarcity, nameof(StorageScarcity));
        if (MonthlyStorageCostIndex <= 0) throw new ArgumentOutOfRangeException(nameof(MonthlyStorageCostIndex));
    }

    public double DemandFor(ServiceTrack track)
    {
        Validate();
        var required = track switch
        {
            ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract => AirportOpportunity.Civilian,
            ServiceTrack.GovernmentContract => AirportOpportunity.Government,
            ServiceTrack.MilitaryService => AirportOpportunity.Military,
            _ => AirportOpportunity.None
        };
        if (required == AirportOpportunity.None || !Opportunities.HasFlag(required)) return 0;
        return track switch
        {
        ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract => CivilianDemand,
        ServiceTrack.GovernmentContract => GovernmentDemand,
        ServiceTrack.MilitaryService => MilitaryDemand,
        _ => 0
        };
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name);
    }
}

public static class InitialAirportProfiles
{
    // KRME is deliberately mixed-use: Oneida County describes commercial/corporate/business/
    // governmental/GA demand and an FAA UAS test site; nearby EADS/224 ADG creates unusually
    // strong defense demand without converting the public airport into a fighter base.
    public static AirportCareerProfile GriffissInternational { get; } = new(
        "KRME",
        "Griffiss International Airport",
        AirportOpportunity.Civilian | AirportOpportunity.Government | AirportOpportunity.Military |
        AirportOpportunity.UasResearch | AirportOpportunity.EmergencyServices,
        CivilianDemand: 0.75,
        GovernmentDemand: 0.72,
        MilitaryDemand: 0.58,
        UasResearchDemand: 0.90,
        MonthlyStorageCostIndex: 1.00m,
        StorageScarcity: 0.35);
}
