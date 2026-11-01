namespace OpenCareer.Domain.Careers;

public sealed record PilotExperienceTotals(
    int FlightCount,
    TimeSpan CareerCreditTime,
    TimeSpan NightCareerCreditTime,
    TimeSpan ActualInstrumentCareerCreditTime,
    int TakeoffCount,
    int LandingEpisodeCount)
{
    public static PilotExperienceTotals Empty { get; } =
        new(
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            0);

    public void Validate()
    {
        if (FlightCount < 0)
            throw new ArgumentOutOfRangeException(nameof(FlightCount));

        if (CareerCreditTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CareerCreditTime));

        if (NightCareerCreditTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(NightCareerCreditTime));
        }

        if (ActualInstrumentCareerCreditTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ActualInstrumentCareerCreditTime));
        }

        if (NightCareerCreditTime > CareerCreditTime)
        {
            throw new InvalidOperationException(
                "Night career-credit time cannot exceed total career-credit time.");
        }

        if (ActualInstrumentCareerCreditTime > CareerCreditTime)
        {
            throw new InvalidOperationException(
                "Actual-instrument career-credit time cannot exceed total career-credit time.");
        }

        if (TakeoffCount < 0)
            throw new ArgumentOutOfRangeException(nameof(TakeoffCount));

        if (LandingEpisodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LandingEpisodeCount));
        }
    }
}
