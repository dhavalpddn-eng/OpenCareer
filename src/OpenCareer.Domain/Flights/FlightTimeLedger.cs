namespace OpenCareer.Domain.Flights;

public sealed record FlightTimeInterval(
    TimeSpan WallDuration,
    double SimulationRate,
    bool ValidOperationalEvidence,
    bool Paused,
    bool SlewActive,
    bool CountsTowardBlockTime,
    bool CountsTowardFlightTime,
    bool Airborne,
    bool TaxiOut,
    bool TaxiIn,
    bool Night,
    bool ActualInstrument);

public sealed record FlightTimeLedger(
    TimeSpan ObservedWallTime,
    TimeSpan SimulatedOperationalTime,
    TimeSpan BlockTime,
    TimeSpan MovementFlightTime,
    TimeSpan AirborneTime,
    TimeSpan TaxiOutTime,
    TimeSpan TaxiInTime,
    TimeSpan CareerCreditTime,
    TimeSpan PausedWallTime,
    TimeSpan AcceleratedWallTime,
    TimeSpan SlewWallTime,
    TimeSpan NightCareerCreditTime,
    TimeSpan ActualInstrumentCareerCreditTime)
{
    public static FlightTimeLedger Empty { get; } = new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);

    public FlightTimeLedger Add(FlightTimeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        Validate(interval);

        var observed = ObservedWallTime + interval.WallDuration;
        var paused = PausedWallTime + (interval.Paused ? interval.WallDuration : TimeSpan.Zero);
        var slew = SlewWallTime + (interval.SlewActive ? interval.WallDuration : TimeSpan.Zero);

        if (!interval.ValidOperationalEvidence || interval.Paused || interval.SlewActive)
        {
            return this with
            {
                ObservedWallTime = observed,
                PausedWallTime = paused,
                SlewWallTime = slew
            };
        }

        var simulatedDelta = Scale(interval.WallDuration, interval.SimulationRate);
        var careerDelta = interval.CountsTowardFlightTime
            ? Scale(interval.WallDuration, Math.Min(interval.SimulationRate, 1d))
            : TimeSpan.Zero;

        return this with
        {
            ObservedWallTime = observed,
            SimulatedOperationalTime = SimulatedOperationalTime + simulatedDelta,
            BlockTime = BlockTime + (interval.CountsTowardBlockTime ? simulatedDelta : TimeSpan.Zero),
            MovementFlightTime = MovementFlightTime + (interval.CountsTowardFlightTime ? simulatedDelta : TimeSpan.Zero),
            AirborneTime = AirborneTime + (interval.Airborne ? simulatedDelta : TimeSpan.Zero),
            TaxiOutTime = TaxiOutTime + (interval.TaxiOut ? simulatedDelta : TimeSpan.Zero),
            TaxiInTime = TaxiInTime + (interval.TaxiIn ? simulatedDelta : TimeSpan.Zero),
            CareerCreditTime = CareerCreditTime + careerDelta,
            PausedWallTime = paused,
            AcceleratedWallTime = AcceleratedWallTime +
                (interval.SimulationRate > 1d ? interval.WallDuration : TimeSpan.Zero),
            SlewWallTime = slew,
            NightCareerCreditTime = NightCareerCreditTime +
                (interval.Night ? careerDelta : TimeSpan.Zero),
            ActualInstrumentCareerCreditTime = ActualInstrumentCareerCreditTime +
                (interval.ActualInstrument ? careerDelta : TimeSpan.Zero)
        };
    }

    private static void Validate(FlightTimeInterval interval)
    {
        if (interval.WallDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Wall duration cannot be negative.");
        }

        if (!double.IsFinite(interval.SimulationRate) || interval.SimulationRate <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Simulation rate must be finite and positive.");
        }

        if (interval.Airborne && !interval.CountsTowardFlightTime)
        {
            throw new ArgumentException("Airborne time must count toward flight time.", nameof(interval));
        }

        if (interval.TaxiOut && interval.TaxiIn)
        {
            throw new ArgumentException("An interval cannot be both taxi-out and taxi-in.", nameof(interval));
        }

        if ((interval.TaxiOut || interval.TaxiIn) && !interval.CountsTowardFlightTime)
        {
            throw new ArgumentException("Taxi flight-time intervals must count toward flight time.", nameof(interval));
        }
    }

    private static TimeSpan Scale(TimeSpan duration, double factor)
    {
        var scaledTicks = duration.Ticks * factor;
        if (!double.IsFinite(scaledTicks) || scaledTicks > long.MaxValue)
        {
            throw new OverflowException("Scaled flight duration exceeds TimeSpan capacity.");
        }

        return TimeSpan.FromTicks((long)Math.Round(scaledTicks, MidpointRounding.AwayFromZero));
    }
}
