using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class EmployerEmploymentPolicyTests
{
    private static readonly Guid EmployerId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(EmploymentPerformanceEventKind.LegitimateGoAroundOrDiversion)]
    [InlineData(EmploymentPerformanceEventKind.SimulatorDisconnect)]
    public void DefensibleSafetyAndTechnicalEventsDoNotDamageEmployment(
        EmploymentPerformanceEventKind eventKind)
    {
        EmployerEmployment starting = Employment(75);
        EmployerEmployment result =
            EmployerEmploymentPolicy.Default.Apply(starting, eventKind);

        Assert.Equal(starting.Standing, result.Standing);
        Assert.Equal(EmploymentStandingState.Active, result.State);
        Assert.Equal(0, result.SeriousStrikes);
        Assert.Equal(0, result.FailedAssignments);
    }

    [Fact]
    public void SingleMinorServiceFailureDoesNotCauseProbationOrTermination()
    {
        EmployerEmployment result = EmployerEmploymentPolicy.Default.Apply(
            Employment(75),
            EmploymentPerformanceEventKind.MinorServiceFailure);

        Assert.Equal(72, result.Standing);
        Assert.Equal(EmploymentStandingState.Active, result.State);
    }

    [Fact]
    public void RepeatedNoShowsEscalateThroughCompanyDiscipline()
    {
        EmployerEmployment employment = Employment(75);
        EmployerEmploymentPolicy policy = EmployerEmploymentPolicy.Default;

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.NoShow);
        Assert.Equal(EmploymentStandingState.Active, employment.State);

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.NoShow);
        Assert.Equal(EmploymentStandingState.Probation, employment.State);

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.NoShow);
        Assert.Equal(EmploymentStandingState.Suspended, employment.State);

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.NoShow);
        Assert.Equal(EmploymentStandingState.Terminated, employment.State);
    }

    [Fact]
    public void SeriousDamageRequiresRepeatedIncidentsBeforeStrikeTermination()
    {
        EmployerEmployment employment = Employment(90);
        EmployerEmploymentPolicy policy = EmployerEmploymentPolicy.Default;

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.PreventableAircraftDamage);

        Assert.NotEqual(EmploymentStandingState.Terminated, employment.State);
        Assert.Equal(1, employment.SeriousStrikes);

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.PreventableAircraftDamage);

        Assert.NotEqual(EmploymentStandingState.Terminated, employment.State);
        Assert.Equal(2, employment.SeriousStrikes);

        employment = policy.Apply(
            employment,
            EmploymentPerformanceEventKind.PreventableAircraftDamage);

        Assert.Equal(EmploymentStandingState.Terminated, employment.State);
        Assert.Equal(3, employment.SeriousStrikes);
    }

    [Fact]
    public void SuccessfulWorkCanRecoverProbation()
    {
        EmployerEmployment employment = Employment(
            54,
            EmploymentStandingState.Probation);

        EmployerEmployment result = EmployerEmploymentPolicy.Default.Apply(
            employment,
            EmploymentPerformanceEventKind.SuccessfulAssignment);

        Assert.Equal(56, result.Standing);
        Assert.Equal(EmploymentStandingState.Active, result.State);
        Assert.Equal(1, result.CompletedAssignments);
    }

    [Fact]
    public void SuspensionRequiresReviewBeforeReturningToProbation()
    {
        EmployerEmployment suspended = Employment(
            22,
            EmploymentStandingState.Suspended);

        EmployerEmployment result =
            EmployerEmploymentPolicy.Default.ReinstateAfterReview(suspended);

        Assert.Equal(40, result.Standing);
        Assert.Equal(EmploymentStandingState.Probation, result.State);
    }

    [Fact]
    public void TerminationIsAbsorbingForPerformanceEvents()
    {
        EmployerEmployment terminated = Employment(
            5,
            EmploymentStandingState.Terminated,
            seriousStrikes: 3);

        EmployerEmployment result = EmployerEmploymentPolicy.Default.Apply(
            terminated,
            EmploymentPerformanceEventKind.ExcellentAssignment);

        Assert.Equal(terminated, result);
    }

    private static EmployerEmployment Employment(
        double standing,
        EmploymentStandingState state = EmploymentStandingState.Active,
        int seriousStrikes = 0) =>
        new(
            EmployerId,
            "First Officer",
            standing,
            state,
            seriousStrikes);
}
