namespace OpenCareer.Domain.Events;

public enum AirfieldOperationClass
{
    CivilianPassenger,
    CivilianCargo,
    Government,
    Humanitarian,
    MilitaryLogistics,
    Reconnaissance
}

public sealed record AirfieldOperationEligibility(
    bool IsAllowed,
    string Reason)
{
    public static AirfieldOperationEligibility Allowed(string reason) => new(true, reason);
    public static AirfieldOperationEligibility Denied(string reason) => new(false, reason);
}

public static class AirfieldOperationGuard
{
    public static AirfieldOperationEligibility Evaluate(
        AirfieldControlState state,
        AirfieldOperationClass operationClass,
        AirfieldControlPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!Enum.IsDefined(operationClass))
            throw new ArgumentOutOfRangeException(nameof(operationClass));

        var p = policy ?? AirfieldControlPolicy.Default;
        p.Validate();
        state.Validate();

        if (state.Status == AirfieldOperationalStatus.Closed)
            return AirfieldOperationEligibility.Denied("Airfield is closed.");

        if (state.RunwayServiceability < p.MinimumRunwayForOperations)
            return AirfieldOperationEligibility.Denied("Runway serviceability is below the operating threshold.");

        return operationClass switch
        {
            AirfieldOperationClass.MilitaryLogistics =>
                state.CanAcceptMilitaryLogistics(p)
                    ? AirfieldOperationEligibility.Allowed("Military logistics permitted by airfield state.")
                    : AirfieldOperationEligibility.Denied("Military logistics not permitted by airfield state."),

            AirfieldOperationClass.Humanitarian =>
                state.CanAcceptHumanitarianFlights(p)
                    ? AirfieldOperationEligibility.Allowed("Humanitarian operations permitted.")
                    : AirfieldOperationEligibility.Denied("Humanitarian operations not permitted."),

            AirfieldOperationClass.Reconnaissance =>
                state.Status == AirfieldOperationalStatus.Contested
                    ? AirfieldOperationEligibility.Allowed("Reconnaissance may operate around a contested airfield without requiring a landing.")
                    : AirfieldOperationEligibility.Allowed("Reconnaissance permitted."),

            AirfieldOperationClass.Government =>
                state.Status is AirfieldOperationalStatus.Restricted
                    or AirfieldOperationalStatus.Secured
                    or AirfieldOperationalStatus.Reopening
                    or AirfieldOperationalStatus.HeightenedSecurity
                    or AirfieldOperationalStatus.Open
                    ? AirfieldOperationEligibility.Allowed("Government operation permitted.")
                    : AirfieldOperationEligibility.Denied("Government operation not permitted."),

            AirfieldOperationClass.CivilianCargo =>
                state.CanAcceptCivilianFlights(p)
                    ? AirfieldOperationEligibility.Allowed("Civilian cargo operation permitted.")
                    : AirfieldOperationEligibility.Denied("Civilian cargo operation not permitted."),

            AirfieldOperationClass.CivilianPassenger =>
                state.CanAcceptCivilianFlights(p)
                    ? AirfieldOperationEligibility.Allowed("Civilian passenger operation permitted.")
                    : AirfieldOperationEligibility.Denied("Civilian passenger operation not permitted."),

            _ => throw new ArgumentOutOfRangeException(nameof(operationClass))
        };
    }
}
