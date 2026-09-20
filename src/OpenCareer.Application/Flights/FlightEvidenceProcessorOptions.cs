namespace OpenCareer.Application.Flights;

public sealed record FlightEvidenceProcessorOptions(
    int StableTelemetrySamples = 3,
    int AirborneConfirmationSamples = 2,
    int InitialClimbConfirmationSamples = 2,
    int GroundConfirmationSamples = 2,
    double TaxiGroundSpeedKnots = 3,
    double TakeoffCandidateGroundSpeedKnots = 25,
    double TakeoffCandidateIndicatedAirspeedKnots = 25,
    double RejectedTakeoffGroundSpeedKnots = 10,
    double AirborneMinimumAglFeet = 15,
    double InitialClimbMinimumAglFeet = 200,
    double InitialClimbMinimumVerticalSpeedFeetPerMinute = 100,
    double ApproachMaximumAglFeet = 2_000,
    double ApproachMaximumVerticalSpeedFeetPerMinute = -100,
    double ParkingMaximumGroundSpeedKnots = 1,
    int MissionFlightConfirmationSamples = 3,
    double MissionFlightMinimumDistanceNauticalMiles = 0.5,
    int ApproachConfirmationSamples = 2,
    double LandingRolloutMaximumGroundSpeedKnots = 20,
    double TaxiInMaximumGroundSpeedKnots = 15)
{
    public void Validate()
    {
        if (StableTelemetrySamples < 1)
            throw new ArgumentOutOfRangeException(nameof(StableTelemetrySamples));

        if (AirborneConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(AirborneConfirmationSamples));

        if (InitialClimbConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(InitialClimbConfirmationSamples));

        if (GroundConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(GroundConfirmationSamples));

        if (MissionFlightConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(MissionFlightConfirmationSamples));

        if (ApproachConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(ApproachConfirmationSamples));

        ValidateNonNegativeFinite(
            TaxiGroundSpeedKnots,
            nameof(TaxiGroundSpeedKnots));

        ValidateNonNegativeFinite(
            TakeoffCandidateGroundSpeedKnots,
            nameof(TakeoffCandidateGroundSpeedKnots));

        ValidateNonNegativeFinite(
            TakeoffCandidateIndicatedAirspeedKnots,
            nameof(TakeoffCandidateIndicatedAirspeedKnots));

        ValidateNonNegativeFinite(
            RejectedTakeoffGroundSpeedKnots,
            nameof(RejectedTakeoffGroundSpeedKnots));

        ValidateNonNegativeFinite(
            AirborneMinimumAglFeet,
            nameof(AirborneMinimumAglFeet));

        ValidateNonNegativeFinite(
            InitialClimbMinimumAglFeet,
            nameof(InitialClimbMinimumAglFeet));

        ValidateNonNegativeFinite(
            InitialClimbMinimumVerticalSpeedFeetPerMinute,
            nameof(InitialClimbMinimumVerticalSpeedFeetPerMinute));

        ValidateNonNegativeFinite(
            ApproachMaximumAglFeet,
            nameof(ApproachMaximumAglFeet));

        if (!double.IsFinite(
                ApproachMaximumVerticalSpeedFeetPerMinute))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ApproachMaximumVerticalSpeedFeetPerMinute));
        }

        ValidateNonNegativeFinite(
            ParkingMaximumGroundSpeedKnots,
            nameof(ParkingMaximumGroundSpeedKnots));

        ValidateNonNegativeFinite(
            LandingRolloutMaximumGroundSpeedKnots,
            nameof(LandingRolloutMaximumGroundSpeedKnots));

        ValidateNonNegativeFinite(
            TaxiInMaximumGroundSpeedKnots,
            nameof(TaxiInMaximumGroundSpeedKnots));

        ValidatePositiveFinite(
            MissionFlightMinimumDistanceNauticalMiles,
            nameof(MissionFlightMinimumDistanceNauticalMiles));

        if (RejectedTakeoffGroundSpeedKnots
            >= TakeoffCandidateGroundSpeedKnots)
        {
            throw new ArgumentException(
                "Rejected-takeoff speed must be below the takeoff-candidate speed.");
        }
    }

    private static void ValidateNonNegativeFinite(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidatePositiveFinite(
        double value,
        string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
