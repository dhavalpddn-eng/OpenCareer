using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class AirfieldControlTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HugeSingleTroopLiftCannotInstantlyCaptureAirfield()
    {
        var state = AirfieldControlEngine.Create("KAAA", "region-a", Epoch);
        var context = ActiveContext(backgroundPressure: 0);

        var next = AirfieldControlEngine.AdvanceOneDay(
            state,
            context,
            careerSeed: 123,
            Epoch.AddDays(1),
            [
                new AirfieldMissionContribution(
                    context.CampaignId,
                    "KAAA",
                    context.SideAId,
                    AirfieldContributionKind.TroopLift,
                    EffortPoints: 1_000_000_000,
                    OccurredAt: Epoch.AddHours(5))
            ]);

        Assert.InRange(
            Math.Abs(next.ControlBalance),
            0,
            AirfieldControlPolicy.Default.MaxPlayerControlShiftPerDay + 0.08);

        Assert.NotEqual(AirfieldOperationalStatus.Secured, next.Status);
    }

    [Fact]
    public void HumanitarianWorkDoesNotChooseSide()
    {
        var state = AirfieldControlEngine.Create("KAAA", "region-a", Epoch);
        var context = ActiveContext(backgroundPressure: 0);

        var next = AirfieldControlEngine.AdvanceOneDay(
            state,
            context,
            careerSeed: 456,
            Epoch.AddDays(1),
            [
                new AirfieldMissionContribution(
                    context.CampaignId,
                    "KAAA",
                    SupportedSideId: null,
                    AirfieldContributionKind.Humanitarian,
                    EffortPoints: 50_000,
                    OccurredAt: Epoch.AddHours(3))
            ]);

        Assert.True(next.RunwayServiceability >= state.RunwayServiceability - 0.05);
        Assert.InRange(Math.Abs(next.ControlBalance), 0, 0.08);
    }

    [Fact]
    public void RecoveryRepairsDamagedAirfieldOverTime()
    {
        var state = AirfieldControlEngine.Create(
            "KAAA",
            "region-a",
            Epoch,
            runwayServiceability: 0.30,
            groundServicesCapacity: 0.20) with
        {
            Status = AirfieldOperationalStatus.Damaged
        };

        var context = new AirfieldDailyContext(
            "campaign-1",
            "side-a",
            "side-b",
            ConflictCampaignPhase.Recovery,
            CampaignSeverity: 0.25,
            BackgroundControlPressure: 0,
            InfrastructureDamagePressure: 0,
            CivilAviationResilience: 0.85);

        for (var day = 1; day <= 20; day++)
        {
            state = AirfieldControlEngine.AdvanceOneDay(
                state,
                context,
                careerSeed: 789,
                Epoch.AddDays(day));
        }

        Assert.True(state.RunwayServiceability > 0.70);
        Assert.True(state.GroundServicesCapacity > 0.55);
        Assert.Contains(
            state.Status,
            new[]
            {
                AirfieldOperationalStatus.Reopening,
                AirfieldOperationalStatus.HeightenedSecurity,
                AirfieldOperationalStatus.Open
            });
    }

    [Fact]
    public void DeterministicAcrossIdenticalInputs()
    {
        var left = AirfieldControlEngine.Create("KAAA", "region-a", Epoch);
        var right = left;
        var context = ActiveContext(backgroundPressure: 0.45);

        for (var day = 1; day <= 30; day++)
        {
            var through = Epoch.AddDays(day);
            left = AirfieldControlEngine.AdvanceOneDay(left, context, 424242, through);
            right = AirfieldControlEngine.AdvanceOneDay(right, context, 424242, through);
        }

        Assert.Equal(left, right);
    }

    [Fact]
    public void SecuredAirfieldCanSupportReinforcementWithoutBecomingGeneralCivilianHub()
    {
        var policy = AirfieldControlPolicy.Default;
        var state = new AirfieldControlState(
            AirfieldControlState.CurrentSchemaVersion,
            "KAAA",
            "region-a",
            AirfieldOperationalStatus.Secured,
            "side-a",
            ControlBalance: 0.85,
            RunwayServiceability: 0.70,
            GroundServicesCapacity: 0.20,
            SecurityPressure: 0.90,
            Epoch);

        state.Validate();

        Assert.True(state.CanAcceptMilitaryLogistics(policy));
        Assert.True(state.CanAcceptHumanitarianFlights(policy));
        Assert.False(state.CanAcceptCivilianFlights(policy));
    }

    [Fact]
    public void ModerateConflictRetainsSomeRepairCapacity()
    {
        var state = AirfieldControlEngine.Create("KAAA", "region-a", Epoch);
        var context = ActiveContext(backgroundPressure: 0);

        for (var day = 1; day <= 180; day++)
        {
            state = AirfieldControlEngine.AdvanceOneDay(
                state,
                context,
                careerSeed: 20260917,
                Epoch.AddDays(day));
        }

        Assert.True(state.RunwayServiceability > 0.15);
    }

    private static AirfieldDailyContext ActiveContext(double backgroundPressure) =>
        new(
            "campaign-1",
            "side-a",
            "side-b",
            ConflictCampaignPhase.ActiveConflict,
            CampaignSeverity: 0.80,
            BackgroundControlPressure: backgroundPressure,
            InfrastructureDamagePressure: 0.30,
            CivilAviationResilience: 0.75);
}
