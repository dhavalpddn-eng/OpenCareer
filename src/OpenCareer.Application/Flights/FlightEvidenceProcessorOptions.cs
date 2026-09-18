namespace OpenCareer.Application.Flights;

public sealed record FlightEvidenceProcessorOptions
{
    public TimeSpan StableTelemetryDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MovementConfirmationDuration { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan TakeoffRollConfirmationDuration { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan AirborneConfirmationDuration { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ApproachConfirmationDuration { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan GoAroundConfirmationDuration { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RejectedTakeoffConfirmationDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan LandingRolloutConfirmationDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan ParkingConfirmationDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan BounceWindow { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan TouchAndGoWindow { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaximumContinuousSampleGap { get; init; } = TimeSpan.FromSeconds(5);

    public double MovementGroundSpeedKnots { get; init; } = 3;
    public double TakeoffRollGroundSpeedKnots { get; init; } = 25;
    public double AirborneGroundSpeedKnots { get; init; } = 25;
    public double AirborneMinimumAglFeet { get; init; } = 15;
    public double ApproachMaximumAglFeet { get; init; } = 1_500;
    public double ApproachMinimumDescentFromPeakFeet { get; init; } = 200;
    public double ApproachMaximumVerticalSpeedFpm { get; init; } = -150;
    public double GoAroundMinimumVerticalSpeedFpm { get; init; } = 300;
    public double LandingRolloutMaximumGroundSpeedKnots { get; init; } = 50;
    public double RejectedTakeoffMaximumGroundSpeedKnots { get; init; } = 10;
    public double StationaryGroundSpeedKnots { get; init; } = 1;

    public double LoadingSentinelLatitudeDegrees { get; init; } = 0;
    public double LoadingSentinelLongitudeDegrees { get; init; } = 90;
    public double LoadingSentinelToleranceDegrees { get; init; } = 0.05;
    public double ReconnectBaseDistanceNauticalMiles { get; init; } = 10;
    public double ReconnectMaximumPlausibleGroundSpeedKnots { get; init; } = 1_000;

    public void Validate()
    {
        TimeSpan[] durations =
        [
            StableTelemetryDuration,
            MovementConfirmationDuration,
            TakeoffRollConfirmationDuration,
            AirborneConfirmationDuration,
            ApproachConfirmationDuration,
            GoAroundConfirmationDuration,
            RejectedTakeoffConfirmationDuration,
            LandingRolloutConfirmationDuration,
            ParkingConfirmationDuration,
            BounceWindow,
            TouchAndGoWindow,
            MaximumContinuousSampleGap
        ];

        if (durations.Any(duration => duration < TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(StableTelemetryDuration));

        double[] nonnegative =
        [
            MovementGroundSpeedKnots,
            TakeoffRollGroundSpeedKnots,
            AirborneGroundSpeedKnots,
            AirborneMinimumAglFeet,
            ApproachMaximumAglFeet,
            ApproachMinimumDescentFromPeakFeet,
            GoAroundMinimumVerticalSpeedFpm,
            LandingRolloutMaximumGroundSpeedKnots,
            RejectedTakeoffMaximumGroundSpeedKnots,
            StationaryGroundSpeedKnots,
            LoadingSentinelToleranceDegrees,
            ReconnectBaseDistanceNauticalMiles,
            ReconnectMaximumPlausibleGroundSpeedKnots
        ];

        if (nonnegative.Any(value => !double.IsFinite(value) || value < 0)
            || !double.IsFinite(ApproachMaximumVerticalSpeedFpm)
            || !double.IsFinite(LoadingSentinelLatitudeDegrees)
            || !double.IsFinite(LoadingSentinelLongitudeDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(MovementGroundSpeedKnots));
        }
    }
}
