namespace OpenCareer.Domain.Logbook;

public sealed record LogbookStatistics(
    int FlightCount,
    TimeSpan MovementFlightTime,
    TimeSpan CareerCreditTime,
    TimeSpan AirborneTime,
    TimeSpan NightCareerCreditTime,
    TimeSpan ActualInstrumentCareerCreditTime,
    int TakeoffCount,
    int LandingEpisodeCount,
    int FullStopLandingCount,
    int TouchAndGoCount,
    int StopAndGoCount)
{
    public static LogbookStatistics Empty { get; } =
        new(
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            0,
            0,
            0,
            0);
}

public static class LogbookStatisticsCalculator
{
    public static LogbookStatistics Calculate(IEnumerable<LogbookEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        int flights = 0;
        TimeSpan movement = TimeSpan.Zero;
        TimeSpan credit = TimeSpan.Zero;
        TimeSpan airborne = TimeSpan.Zero;
        TimeSpan night = TimeSpan.Zero;
        TimeSpan instrument = TimeSpan.Zero;
        int takeoffs = 0;
        int landingEpisodes = 0;
        int fullStop = 0;
        int touchAndGo = 0;
        int stopAndGo = 0;

        foreach (LogbookEntry entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            FlightDebrief debrief = entry.Debrief;

            flights++;
            movement += debrief.Time.MovementFlightTime;
            credit += debrief.Time.CareerCreditTime;
            airborne += debrief.Time.AirborneTime;
            night += debrief.Time.NightCareerCreditTime;
            instrument += debrief.Time.ActualInstrumentCareerCreditTime;
            takeoffs += debrief.Tracking.TakeoffCount;
            landingEpisodes += debrief.Tracking.LandingEpisodeCount;

            foreach (LandingDebrief landing in debrief.Landings)
            {
                switch (landing.OperationType)
                {
                    case LandingOperationType.FullStop:
                        fullStop++;
                        break;
                    case LandingOperationType.TouchAndGo:
                        touchAndGo++;
                        break;
                    case LandingOperationType.StopAndGo:
                        stopAndGo++;
                        break;
                }
            }
        }

        return new(
            flights,
            movement,
            credit,
            airborne,
            night,
            instrument,
            takeoffs,
            landingEpisodes,
            fullStop,
            touchAndGo,
            stopAndGo);
    }
}
