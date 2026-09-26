using System.Collections.Immutable;
using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerQualificationTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewCareerStartsWithoutLicensesOrRatings()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("4f9f01f8-1118-4dc5-b2b4-9425d960edce"),
                "KRME",
                Epoch);

        Assert.Equal(
            PilotLicenseLevel.None,
            profile.Qualifications.License);
        Assert.Empty(profile.Qualifications.Ratings);
    }

    [Fact]
    public void PrivatePilotWithRatingsIsValid()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("f784e4a2-6966-4cf4-b567-d192ef393756"),
                "KRME",
                Epoch) with
            {
                Qualifications =
                    new PilotQualificationState(
                        PilotLicenseLevel.Private,
                        ImmutableHashSet.Create(
                            PilotRating.AirplaneSingleEngineLand,
                            PilotRating.InstrumentAirplane))
            };

        profile.Validate();
    }

    [Theory]
    [InlineData(PilotLicenseLevel.None)]
    [InlineData(PilotLicenseLevel.Student)]
    public void RatingsRequireAtLeastPrivateLicense(
        PilotLicenseLevel license)
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("8b8d67a5-05a4-4a6f-aaed-3186f6f05ae8"),
                "KRME",
                Epoch) with
            {
                Qualifications =
                    new PilotQualificationState(
                        license,
                        ImmutableHashSet.Create(
                            PilotRating.AirplaneSingleEngineLand))
            };

        Assert.Throws<InvalidOperationException>(
            profile.Validate);
    }
}
