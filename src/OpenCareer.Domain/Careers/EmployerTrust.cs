namespace OpenCareer.Domain.Careers;

public enum EmployerScale
{
    Local,
    Regional,
    National,
    MajorAirline,
    MajorCargo,
    Government,
    Military
}

public enum EmployerTrustTier
{
    New,
    Known,
    Trusted,
    Preferred,
    Partner
}

public sealed record EmployerOperatingProfile(
    Guid EmployerId,
    string DisplayName,
    EmployerType Type,
    EmployerScale Scale,
    bool IsRealWorldCompany,
    bool DutyDeadheadProgramAvailable)
{
    public void Validate()
    {
        if (EmployerId == Guid.Empty || string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("Employer identity is required.");
        if (!Enum.IsDefined(Type) || !Enum.IsDefined(Scale))
            throw new ArgumentOutOfRangeException(nameof(Type));
    }
}

public sealed record EmployerTrustPolicy(
    double SuccessfulJobGain,
    double OnTimeBonus,
    double SafeOperationBonus,
    double FailedJobPenalty,
    double CancelledAfterAcceptancePenalty)
{
    public static EmployerTrustPolicy Default { get; } = new(4, 1, 1, 15, 5);

    public void Validate()
    {
        ValidateNonNegative(SuccessfulJobGain, nameof(SuccessfulJobGain));
        ValidateNonNegative(OnTimeBonus, nameof(OnTimeBonus));
        ValidateNonNegative(SafeOperationBonus, nameof(SafeOperationBonus));
        ValidateNonNegative(FailedJobPenalty, nameof(FailedJobPenalty));
        ValidateNonNegative(CancelledAfterAcceptancePenalty, nameof(CancelledAfterAcceptancePenalty));
    }

    private static void ValidateNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// Employer trust is independent from career level. It unlocks relationship-specific work and may
/// make an employer-provided deadhead available, but it never grants licenses or aircraft authority.
/// </summary>
public sealed record EmployerTrustState(
    Guid EmployerId,
    double TrustScore,
    int CompletedJobs,
    int FailedJobs,
    DateTimeOffset UpdatedAt)
{
    public EmployerTrustTier Tier => TrustScore switch
    {
        >= 90 => EmployerTrustTier.Partner,
        >= 70 => EmployerTrustTier.Preferred,
        >= 45 => EmployerTrustTier.Trusted,
        >= 20 => EmployerTrustTier.Known,
        _ => EmployerTrustTier.New
    };

    public double RelationshipStrength => Math.Clamp(TrustScore / 100.0, 0, 1);

    public static EmployerTrustState Start(Guid employerId, DateTimeOffset time)
    {
        if (employerId == Guid.Empty)
            throw new ArgumentException("Employer identity is required.", nameof(employerId));
        return new EmployerTrustState(employerId, 0, 0, 0, time);
    }

    public EmployerTrustState RecordSuccess(
        DateTimeOffset time,
        bool onTime,
        bool safeOperation,
        EmployerTrustPolicy? policy = null)
    {
        Validate();
        var effective = policy ?? EmployerTrustPolicy.Default;
        effective.Validate();
        RequireForwardTime(time);
        var gain = effective.SuccessfulJobGain
            + (onTime ? effective.OnTimeBonus : 0)
            + (safeOperation ? effective.SafeOperationBonus : 0);
        return this with
        {
            TrustScore = Math.Clamp(TrustScore + gain, 0, 100),
            CompletedJobs = checked(CompletedJobs + 1),
            UpdatedAt = time
        };
    }

    public EmployerTrustState RecordFailure(DateTimeOffset time, EmployerTrustPolicy? policy = null)
    {
        Validate();
        var effective = policy ?? EmployerTrustPolicy.Default;
        effective.Validate();
        RequireForwardTime(time);
        return this with
        {
            TrustScore = Math.Clamp(TrustScore - effective.FailedJobPenalty, 0, 100),
            FailedJobs = checked(FailedJobs + 1),
            UpdatedAt = time
        };
    }

    public EmployerTrustState RecordAcceptedCancellation(DateTimeOffset time, EmployerTrustPolicy? policy = null)
    {
        Validate();
        var effective = policy ?? EmployerTrustPolicy.Default;
        effective.Validate();
        RequireForwardTime(time);
        return this with
        {
            TrustScore = Math.Clamp(TrustScore - effective.CancelledAfterAcceptancePenalty, 0, 100),
            UpdatedAt = time
        };
    }

    public bool CanReceiveEmployerDeadhead(EmployerOperatingProfile employer)
    {
        ArgumentNullException.ThrowIfNull(employer);
        employer.Validate();
        Validate();
        if (employer.EmployerId != EmployerId || !employer.DutyDeadheadProgramAvailable)
            return false;

        var sufficientlyLarge = employer.Scale is EmployerScale.National or EmployerScale.MajorAirline or EmployerScale.MajorCargo;
        return sufficientlyLarge && Tier >= EmployerTrustTier.Trusted;
    }

    public void Validate()
    {
        if (EmployerId == Guid.Empty)
            throw new ArgumentException("Employer identity is required.", nameof(EmployerId));
        if (!double.IsFinite(TrustScore) || TrustScore is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(TrustScore));
        if (CompletedJobs < 0 || FailedJobs < 0)
            throw new ArgumentOutOfRangeException(nameof(CompletedJobs));
    }

    private void RequireForwardTime(DateTimeOffset time)
    {
        if (time < UpdatedAt)
            throw new InvalidOperationException("Employer trust cannot move backwards in time.");
    }
}
