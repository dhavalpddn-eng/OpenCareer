namespace OpenCareer.Application.Flights;

public sealed record FlightEvidenceProcessorOptions(
    int StableTelemetrySamples = 3,
    int AirborneConfirmationSamples = 2,
    int GroundConfirmationSamples = 2,
    double TaxiGroundSpeedKnots = 3,
    double TakeoffCandidateGroundSpeedKnots = 25,
    double TakeoffCandidateIndicatedAirspeedKnots = 25,
    double RejectedTakeoffGroundSpeedKnots = 10,
    double AirborneMinimumAglFeet = 15,
    double ApproachMaximumAglFeet = 2_000,
    double ApproachMaximumVerticalSpeedFeetPerMinute = -100,
    double ParkingMaximumGroundSpeedKnots = 1)
{
    public void Validate()
    {
        if (StableTelemetrySamples < 1)
            throw new ArgumentOutOfRangeException(nameof(StableTelemetrySamples));

        if (AirborneConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(AirborneConfirmationSamples));

        if (GroundConfirmationSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(GroundConfirmationSamples));

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
}
