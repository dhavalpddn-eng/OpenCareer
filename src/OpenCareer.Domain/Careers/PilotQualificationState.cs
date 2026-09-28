using System.Collections.Immutable;

namespace OpenCareer.Domain.Careers;

public enum PilotLicenseLevel
{
    None = 0,
    Student = 1,
    Private = 2,
    Commercial = 3,
    AirlineTransport = 4
}

public enum PilotRating
{
    AirplaneSingleEngineLand = 0,
    AirplaneMultiEngineLand = 1,
    InstrumentAirplane = 2
}

public sealed record PilotQualificationState(
    PilotLicenseLevel License,
    ImmutableHashSet<PilotRating> Ratings)
{
    public static PilotQualificationState Entry { get; } =
        new(
            PilotLicenseLevel.None,
            ImmutableHashSet<PilotRating>.Empty);

    public void Validate()
    {
        if (!Enum.IsDefined(typeof(PilotLicenseLevel), License))
        {
            throw new InvalidOperationException(
                "Pilot license level is not recognized.");
        }

        ArgumentNullException.ThrowIfNull(Ratings);

        foreach (PilotRating rating in Ratings)
        {
            if (!Enum.IsDefined(typeof(PilotRating), rating))
            {
                throw new InvalidOperationException(
                    "Pilot rating is not recognized.");
            }
        }

        if (License is PilotLicenseLevel.None or PilotLicenseLevel.Student
            && Ratings.Count > 0)
        {
            throw new InvalidOperationException(
                "Pilot ratings require at least a private pilot license.");
        }
    }
}
