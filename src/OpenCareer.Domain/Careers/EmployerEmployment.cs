namespace OpenCareer.Domain.Careers;

public enum EmploymentStandingState
{
    Active,
    Probation,
    Suspended,
    Terminated
}

public enum EmploymentPerformanceEventKind
{
    SuccessfulAssignment,
    ExcellentAssignment,
    MinorServiceFailure,
    PreventableCancellation,
    NoShow,
    SeriousSafetyViolation,
    PreventableAircraftDamage,
    LegitimateGoAroundOrDiversion,
    SimulatorDisconnect
}

public sealed record EmployerEmployment(
    Guid EmployerId,
    string EmployeeRank,
    double Standing,
    EmploymentStandingState State = EmploymentStandingState.Active,
    int SeriousStrikes = 0,
    int CompletedAssignments = 0,
    int FailedAssignments = 0)
{
    public void Validate()
    {
        if (EmployerId == Guid.Empty)
            throw new ArgumentException("Employer id is required.", nameof(EmployerId));

        if (string.IsNullOrWhiteSpace(EmployeeRank))
            throw new ArgumentException("Employee rank is required.", nameof(EmployeeRank));

        if (!double.IsFinite(Standing) || Standing is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(Standing));

        if (SeriousStrikes < 0)
            throw new ArgumentOutOfRangeException(nameof(SeriousStrikes));

        if (CompletedAssignments < 0)
            throw new ArgumentOutOfRangeException(nameof(CompletedAssignments));

        if (FailedAssignments < 0)
            throw new ArgumentOutOfRangeException(nameof(FailedAssignments));
    }
}

public sealed class EmployerEmploymentPolicy
{
    public static EmployerEmploymentPolicy Default { get; } = new();

    public EmployerEmployment Apply(
        EmployerEmployment employment,
        EmploymentPerformanceEventKind eventKind)
    {
        ArgumentNullException.ThrowIfNull(employment);
        employment.Validate();

        if (employment.State == EmploymentStandingState.Terminated)
            return employment;

        double delta = eventKind switch
        {
            EmploymentPerformanceEventKind.ExcellentAssignment => 4,
            EmploymentPerformanceEventKind.SuccessfulAssignment => 2,
            EmploymentPerformanceEventKind.MinorServiceFailure => -3,
            EmploymentPerformanceEventKind.PreventableCancellation => -10,
            EmploymentPerformanceEventKind.NoShow => -18,
            EmploymentPerformanceEventKind.SeriousSafetyViolation => -25,
            EmploymentPerformanceEventKind.PreventableAircraftDamage => -30,
            EmploymentPerformanceEventKind.LegitimateGoAroundOrDiversion => 0,
            EmploymentPerformanceEventKind.SimulatorDisconnect => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(eventKind))
        };

        int seriousStrikes = employment.SeriousStrikes +
            (eventKind is EmploymentPerformanceEventKind.SeriousSafetyViolation or
                EmploymentPerformanceEventKind.PreventableAircraftDamage
                ? 1
                : 0);

        int completedAssignments = employment.CompletedAssignments +
            (eventKind is EmploymentPerformanceEventKind.SuccessfulAssignment or
                EmploymentPerformanceEventKind.ExcellentAssignment
                ? 1
                : 0);

        int failedAssignments = employment.FailedAssignments +
            (eventKind is EmploymentPerformanceEventKind.PreventableCancellation or
                EmploymentPerformanceEventKind.NoShow or
                EmploymentPerformanceEventKind.SeriousSafetyViolation or
                EmploymentPerformanceEventKind.PreventableAircraftDamage
                ? 1
                : 0);

        double standing = Math.Clamp(employment.Standing + delta, 0, 100);
        EmploymentStandingState state = Classify(standing, seriousStrikes);

        return employment with
        {
            Standing = standing,
            State = state,
            SeriousStrikes = seriousStrikes,
            CompletedAssignments = completedAssignments,
            FailedAssignments = failedAssignments
        };
    }

    public EmployerEmployment ReinstateAfterReview(
        EmployerEmployment employment,
        double standingFloor = 40)
    {
        ArgumentNullException.ThrowIfNull(employment);
        employment.Validate();

        if (employment.State == EmploymentStandingState.Terminated)
            throw new InvalidOperationException(
                "Terminated employment requires a separate rehire decision.");

        if (employment.State != EmploymentStandingState.Suspended)
            throw new InvalidOperationException(
                "Only suspended employment can be reinstated through review.");

        if (!double.IsFinite(standingFloor) || standingFloor is < 30 or >= 55)
            throw new ArgumentOutOfRangeException(nameof(standingFloor));

        return employment with
        {
            Standing = Math.Max(employment.Standing, standingFloor),
            State = EmploymentStandingState.Probation
        };
    }

    private static EmploymentStandingState Classify(
        double standing,
        int seriousStrikes)
    {
        if (seriousStrikes >= 3 || standing <= 10)
            return EmploymentStandingState.Terminated;

        if (standing < 30)
            return EmploymentStandingState.Suspended;

        if (standing < 55)
            return EmploymentStandingState.Probation;

        return EmploymentStandingState.Active;
    }
}
