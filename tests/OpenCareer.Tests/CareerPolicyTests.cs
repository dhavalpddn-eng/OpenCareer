using System.Text.Json;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Economy;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public class CareerPolicyTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly AircraftCapabilityProfile Plane = new("test-cargo", "Test fixture",
        AircraftCapability.Cargo, AircraftAccess.Civilian, 3000, 900, 170, 9, 1, true, false, false);
    private static ContractDispatchContext Dispatch => new(Epoch.AddHours(1), Plane, AircraftAccess.Civilian, new(), true, true);
    private static JobContract Job(string origin = "KRME", string destination = "KALB") => new(
        Guid.NewGuid(), null, ContractKind.Cargo, ServiceTrack.CivilianEmployment, origin, destination,
        new(CompensationModel.PilotWage, 100m, 0m, true, true, true), Epoch, Epoch.AddHours(3), Epoch.AddHours(8),
        new(RequiredCapabilities: AircraftCapability.Cargo));

    [Theory]
    [InlineData(1)] [InlineData(60)] [InlineData(3650)]
    public void ProtectedAbsenceNeverCreatesBillsEvenWithStaff(int days)
    {
        foreach (var staffed in new[] { false, true })
        {
            var result = OfflineLiabilityPolicy.Default.Assess(Epoch, Epoch.AddDays(days), 100000m, staffed);
            Assert.Equal(0m, result.AccruedFixedLiabilities);
            Assert.Equal(result.Elapsed, result.ProtectedDuration);
        }
    }

    [Fact]
    public void OptionalCappedAssessmentAccountsForAllElapsedTime()
    {
        var policy = new OfflineLiabilityPolicy(TimeSpan.FromDays(3), TimeSpan.FromDays(30), TimeSpan.FromDays(90));
        var result = policy.Assess(Epoch, Epoch.AddDays(60), 1500m, false);
        Assert.Equal(1500m, result.AccruedFixedLiabilities);
        Assert.Equal(result.Elapsed, result.BillableDuration + result.ProtectedDuration);
        Assert.Equal(result, policy.Assess(Epoch, Epoch.AddDays(60), 1500m, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => (policy with { SoloAccrualCap = TimeSpan.FromDays(-1) }).Assess(Epoch, Epoch, 0m, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Assess(Epoch, Epoch.AddDays(-1), 0m, false));
    }

    [Fact]
    public void OwnershipTargetIsEconomicAndRejectsNonFiniteTuning()
    {
        var policy = CareerProgressionPolicy.Default;
        policy.Validate();
        Assert.InRange(policy.ExpectedHoursToAcquisition, 50, 80);
        Assert.Equal(64, policy.ExpectedHoursToAcquisition);
        Assert.Throws<ArgumentOutOfRangeException>(() => (policy with { TargetOwnershipHoursLow = double.NaN }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (policy with { TargetOwnershipHoursHigh = double.PositiveInfinity }).Validate());
    }

    [Fact]
    public void LocalJobsCannotTeleportPilot()
    {
        var career = CareerLocation.Start("krme", Epoch);
        Assert.Throws<InvalidOperationException>(() => career.AcceptLocalJob(Job("KLAX"), Dispatch));
        Assert.Throws<InvalidOperationException>(() => career.StartLocalJob(Job("KLAX").Accept(Dispatch), Dispatch));
        Assert.False(career.CanRequestStorage("KLAX"));
        Assert.Throws<InvalidOperationException>(() => career.ApplyCompletedTravel(Job()));
    }

    [Fact]
    public void CompletedTravelUnlocksConnectionWithoutMovingHomeAndSurvivesReload()
    {
        var career = CareerLocation.Start("KRME", Epoch);
        var flying = career.StartLocalJob(career.AcceptLocalJob(Job(), Dispatch), Dispatch);
        var completed = flying.Complete(Epoch.AddHours(3), true);
        var arrived = career.ApplyCompletedTravel(completed);
        Assert.Equal("KRME", arrived.HomeAirportIcao);
        Assert.Equal("KALB", arrived.CurrentAirportIcao);
        Assert.True(arrived.CanRequestStorage("KALB"));
        var restored = JsonSerializer.Deserialize<CareerLocation>(JsonSerializer.Serialize(arrived))!;
        Assert.Same(restored, restored.ApplyCompletedTravel(completed));
        Assert.Throws<InvalidOperationException>(() => restored.AcceptLocalJob(Job(), Dispatch));
    }

    [Theory]
    [InlineData(1, true, true)] [InlineData(3, true, true)] [InlineData(6, true, false)]
    [InlineData(7, false, false)] [InlineData(0, false, false)]
    public void SessionsPreferOneToThreeHoursAndSupportSix(int hours, bool fits, bool preferred)
    {
        Assert.Equal(fits, CareerSessionPolicy.FitsJobDuration(TimeSpan.FromHours(hours)));
        Assert.Equal(preferred, CareerSessionPolicy.IsPreferredDuration(TimeSpan.FromHours(hours)));
    }

    [Fact]
    public void GriffissKeepsCivilianAndDefenseOpportunities()
    {
        var airport = InitialAirportProfiles.GriffissInternational;
        airport.Validate();
        Assert.Equal("KRME", airport.Icao);
        Assert.Equal(0, (airport with { Opportunities = AirportOpportunity.Civilian }).DemandFor(ServiceTrack.MilitaryService));
        Assert.True(airport.DemandFor(ServiceTrack.CivilianEmployment) > 0);
        Assert.True(airport.DemandFor(ServiceTrack.MilitaryService) > 0);
    }

    [Fact]
    public void ManualRewardQuoteIsOptionalCappedAndDeduplicated()
    {
        var policy = ManualGroundProcedurePolicy.Default;
        var events = Enum.GetValues<GroundProcedureKind>().Select(kind => new GroundProcedureEvent(
            Epoch, kind, ProcedureOutcome.Completed, ProcedureObservationSource.UserConfirmed, 1)).ToArray();
        Assert.Equal(0m, policy.CalculateReward([]));
        Assert.Equal(35m, policy.CalculateReward(events.Concat(events)));
        Assert.Equal(35m, policy.CalculateReward(events.Concat(events), 1m));
        Assert.Equal(5.83m, policy.CalculateReward(events.Concat(events), 1m / 6m));
        Assert.Equal(8m, policy.CalculateReward([events[0], events[0]]));
        Assert.Equal(0m, policy.CalculateReward([events[0] with { Outcome = ProcedureOutcome.Skipped }]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualGroundProcedurePolicy(-1).CalculateReward([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.CalculateReward(events, 0));
    }
    [Fact]
    public void BankruptcyRequiresRepeatedActiveMissesInsolvencyAndRecoveryOffer()
    {
        Assert.Equal(FinancialRecoveryStage.Warning, BankruptcyPolicy.Assess(1, 100, 0, 1000, true));
        Assert.Equal(FinancialRecoveryStage.Restructuring, BankruptcyPolicy.Assess(3, 100, 0, 1000, false));
        Assert.Equal(FinancialRecoveryStage.Restructuring, BankruptcyPolicy.Assess(3, 100, 2000, 1000, true));
        Assert.Equal(FinancialRecoveryStage.BankruptcyEligible, BankruptcyPolicy.Assess(3, 100, 0, 1000, true));
        Assert.Equal(FinancialRecoveryStage.Healthy, BankruptcyPolicy.Assess(0, 0, 0, 1000, false));
    }

    [Theory]
    [InlineData(ContractKind.MilitaryTraining)]
    [InlineData(ContractKind.MilitaryEscort)]
    [InlineData(ContractKind.MilitaryTransport)]
    [InlineData(ContractKind.MilitaryFerry)]
    [InlineData(ContractKind.MilitarySurveillance)]
    public void MilitaryScenariosRequireAircraftAccessAndGovernmentAuthorization(ContractKind kind)
    {
        var job = Job() with { Kind = kind, ServiceTrack = ServiceTrack.MilitaryService,
            GovernmentAuthorizationRequired = true,
            AircraftRequirements = new(RequiredCapabilities: AircraftCapability.Military, AllowedAccess: AircraftAccess.Military) };
        Assert.Throws<InvalidOperationException>(() => job.Accept(Dispatch));
        var military = Dispatch with { Aircraft = Plane with { Access = AircraftAccess.Military,
            Capabilities = AircraftCapability.Military | AircraftCapability.Cargo }, AuthorizedAccess = AircraftAccess.Military };
        Assert.Throws<InvalidOperationException>(() => job.Accept(military));
        Assert.Equal(ContractStatus.Accepted, job.Accept(military with { GovernmentAuthorized = true }).Status);
    }
}
