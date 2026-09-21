namespace OpenCareer.Domain.Careers;

public enum EmployerType
{
    FlightSchool,
    Charter,
    Cargo,
    Airline,
    Agricultural,
    Firefighting,
    Survey,
    Skydiving,
    GliderClub,
    Maintenance,
    Medical,
    Government,
    Military,
    PrivateOwner,
    Other
}

public sealed record Employer(
    Guid EmployerId,
    string Name,
    EmployerType Type,
    string HomeAirportIcao,
    double ReputationWithPlayer,
    decimal Cash,
    bool IsGovernmentEntity = false)
{
    public Employer AdjustReputation(double delta) => this with
    {
        ReputationWithPlayer = Math.Clamp(ReputationWithPlayer + delta, -100.0, 100.0)
    };
}
