using OpenCareer.Domain.Careers;

namespace OpenCareer.Tests;

public sealed class PlayerCareerExperienceTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewCareerStartsWithZeroExperience()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("bf01ee55-61da-45ef-9463-8c1b0df9402e"),
                "KRME",
                Epoch);

        Assert.Equal(PilotExperienceTotals.Empty, profile.Experience);
    }

    [Fact]
    public void NonNegativeExperienceWithinCareerCreditIsValid()
    {
        PlayerCareerProfile profile =
            PlayerCareerProfile.Start(
                Guid.Parse("a4a46ea4-abfc-4db8-b312-46cabcd4c271"),
                "KRME",
                Epoch) with
            {
                Experience =
                    new PilotExperienceTotals(
                        FlightCount: 12,
                        CareerCreditTime: TimeSpan.FromHours(18),
                        NightCareerCreditTime: TimeSpan.FromHours(4),
                        ActualInstrumentCareerCreditTime: TimeSpan.FromHours(2),
                        TakeoffCount: 15,
                        LandingEpisodeCount: 14)
            };

        profile.Validate();
    }

    [Fact]
    public void NegativeExperienceIsRejected()
    {
        var totals =
            PilotExperienceTotals.Empty with
            {
                FlightCount = -1
            };

        Assert.Throws<ArgumentOutOfRangeException>(
            totals.Validate);
    }

    [Fact]
    public void SpecializedCreditCannotExceedTotalCareerCredit()
    {
        var night =
            PilotExperienceTotals.Empty with
            {
                CareerCreditTime = TimeSpan.FromHours(1),
                NightCareerCreditTime = TimeSpan.FromHours(2)
            };

        var instrument =
            PilotExperienceTotals.Empty with
            {
                CareerCreditTime = TimeSpan.FromHours(1),
                ActualInstrumentCareerCreditTime = TimeSpan.FromHours(2)
            };

        Assert.Throws<InvalidOperationException>(
            night.Validate);
        Assert.Throws<InvalidOperationException>(
            instrument.Validate);
    }
}
