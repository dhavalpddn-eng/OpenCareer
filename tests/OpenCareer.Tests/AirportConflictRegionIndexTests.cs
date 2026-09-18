using OpenCareer.Application.Conflict;

namespace OpenCareer.Tests;

public sealed class AirportConflictRegionIndexTests
{
    [Fact]
    public void ExactAirportBindingOverridesCountryFallback()
    {
        var index = new AirportConflictRegionIndex(
            [
                new AirportConflictRegionBinding("KAAA", "metro-special", "airport-source")
            ],
            [
                new CountryConflictRegionBinding("US", "country-default", "country-source")
            ]);

        var resolution = index.Resolve("kaaa", "us");

        Assert.NotNull(resolution);
        Assert.Equal("metro-special", resolution.RegionId);
        Assert.Equal("airport-source", resolution.SourceId);
        Assert.True(resolution.IsAirportSpecific);
    }

    [Fact]
    public void CountryFallbackWorksWhenAirportHasNoOverride()
    {
        var index = new AirportConflictRegionIndex(
            Array.Empty<AirportConflictRegionBinding>(),
            [
                new CountryConflictRegionBinding("US", "country-default", "country-source")
            ]);

        var resolution = index.Resolve("KBBB", "US");

        Assert.NotNull(resolution);
        Assert.Equal("country-default", resolution.RegionId);
        Assert.False(resolution.IsAirportSpecific);
    }
}
