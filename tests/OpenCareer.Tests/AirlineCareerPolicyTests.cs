using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;

namespace OpenCareer.Tests;

public sealed class AirlineCareerPolicyTests
{
    [Fact]
    public void RegionalJetBecomesAvailableBeforeNarrowbodyAndWidebody()
    {
        CareerLevelSnapshot regionalLevel =
            CareerLevelPolicy.Default.Evaluate(
                new CareerProgressEvidence(
                    VerifiedFlightHours: 420,
                    CompletedContracts: 145,
                    EstablishedRouteMilestones: 15,
                    EmployerTrustMilestones: 12,
                    EarnedQualifications: 5));

        AirlineEmploymentAccess regional =
            AirlineCareerPolicy.Evaluate(
                AirlineAircraftClass.RegionalJet,
                regionalLevel,
                verifiedFlightHours: 420,
                earnedQualifications: 5,
                employerTrust: EmployerTrustTier.Preferred);

        AirlineEmploymentAccess narrowbody =
            AirlineCareerPolicy.Evaluate(
                AirlineAircraftClass.Narrowbody,
                regionalLevel,
                verifiedFlightHours: 420,
                earnedQualifications: 5,
                employerTrust: EmployerTrustTier.Preferred);

        Assert.True(regional.CanFlyEmployerAircraft);
        Assert.False(narrowbody.CanFlyEmployerAircraft);
    }

    [Fact]
    public void NarrowbodyRequiresQualificationsTrustAndExperienceNotJustLevel()
    {
        CareerLevelSnapshot level =
            new(
                Level: 40,
                MeritPoints: 200_000,
                NextLevelAt: 210_000);

        AirlineEmploymentAccess missingQualification =
            AirlineCareerPolicy.Evaluate(
                AirlineAircraftClass.Narrowbody,
                level,
                verifiedFlightHours: 600,
                earnedQualifications: 5,
                employerTrust: EmployerTrustTier.Partner);

        AirlineEmploymentAccess missingTrust =
            AirlineCareerPolicy.Evaluate(
                AirlineAircraftClass.Narrowbody,
                level,
                verifiedFlightHours: 600,
                earnedQualifications: 6,
                employerTrust: EmployerTrustTier.Trusted);

        AirlineEmploymentAccess ready =
            AirlineCareerPolicy.Evaluate(
                AirlineAircraftClass.Narrowbody,
                level,
                verifiedFlightHours: 600,
                earnedQualifications: 6,
                employerTrust: EmployerTrustTier.Preferred);

        Assert.False(missingQualification.CanFlyEmployerAircraft);
        Assert.False(missingTrust.CanFlyEmployerAircraft);
        Assert.True(ready.CanFlyEmployerAircraft);
    }

    [Fact]
    public void WidebodyIsAnEndgameEmploymentStep()
    {
        CareerLevelSnapshot endgameLevel =
            CareerLevelPolicy.Default.Evaluate(
                new CareerProgressEvidence(
                    VerifiedFlightHours: 750,
                    CompletedContracts: 240,
                    EstablishedRouteMilestones: 24,
                    EmployerTrustMilestones: 18,
                    EarnedQualifications: 7));

        Assert.Equal(37, endgameLevel.Level);

        AirlineEmploymentAccess access =
            AirlineCareerPolicy.Evaluate(
                AirlineAircraftClass.Widebody,
                endgameLevel,
                verifiedFlightHours: 750,
                earnedQualifications: 7,
                employerTrust: EmployerTrustTier.Partner);

        Assert.True(access.CanFlyEmployerAircraft);
    }

    [Theory]
    [InlineData(AirlineAircraftClass.RegionalJet, 4, 900)]
    [InlineData(AirlineAircraftClass.Narrowbody, 8, 2000)]
    [InlineData(AirlineAircraftClass.Widebody, 15, 5500)]
    public void AirlineEmploymentPaysPilotWithoutChargingAircraftOwnershipCosts(
        AirlineAircraftClass aircraftClass,
        double hours,
        double distanceNm)
    {
        ContractPayQuote quote =
            ContractPayQuoteEngine.Quote(
                new ContractPayQuoteRequest(
                    ServiceTrack.CivilianEmployment,
                    ContractKind.Passenger,
                    EstimatedFlightHours: hours,
                    DistanceNauticalMiles: distanceNm,
                    PayloadPounds: 20_000,
                    DemandAttractiveness: 1,
                    Urgency: 0.15,
                    Difficulty: 0.15,
                    RelationshipStrength: 0.50));

        Assert.Equal(CompensationModel.PilotWage, quote.Compensation.Model);
        Assert.True(quote.Compensation.EmployerCoversFuel);
        Assert.True(quote.Compensation.EmployerCoversMaintenance);
        Assert.True(quote.Compensation.EmployerCoversAirportFees);
        Assert.True(quote.PilotCashCompensation > 0m);

        if (aircraftClass == AirlineAircraftClass.Widebody)
            Assert.True(quote.ExtendedDutyMultiplier >= 1.25);
    }
}
