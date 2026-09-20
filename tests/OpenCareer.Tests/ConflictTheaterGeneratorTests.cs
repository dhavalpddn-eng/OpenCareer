using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictTheaterGeneratorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameSeedProducesSameFictionalTheater()
    {
        var template = Template();

        var first = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed: 123456789UL,
            Epoch);

        var second = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed: 123456789UL,
            Epoch);

        Assert.Equal(first, second);
        Assert.Equal(9, first.Sectors.Length);
        Assert.Equal(template.FriendlyGroundUnits + template.HostileGroundUnits, first.Units.Length);
        Assert.Equal(template.FriendlyAirUnits + template.HostileAirUnits, first.AirUnits.Length);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentTheaterState()
    {
        var template = Template();

        var first = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed: 100UL,
            Epoch);

        var second = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed: 200UL,
            Epoch);

        Assert.NotEqual(first.Units, second.Units);
        Assert.NotEqual(first.AirUnits, second.AirUnits);
    }

    [Fact]
    public void GeneratedTheaterKeepsEveryUnitInsideConfiguredRegion()
    {
        var template = Template() with
        {
            RadiusNauticalMiles = 120
        };

        var state = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed: 0xBADC0FFEEUL,
            Epoch);

        foreach (var unit in state.Units)
        {
            Assert.InRange(
                ConflictGeometry.DistanceNauticalMiles(
                    template.Center,
                    unit.Position),
                0,
                template.RadiusNauticalMiles);
        }

        foreach (var airUnit in state.AirUnits)
        {
            Assert.InRange(
                ConflictGeometry.DistanceNauticalMiles(
                    template.Center,
                    airUnit.Position),
                0,
                template.RadiusNauticalMiles);

            Assert.InRange(
                ConflictGeometry.DistanceNauticalMiles(
                    template.Center,
                    airUnit.Destination),
                0,
                template.RadiusNauticalMiles);
        }
    }

    [Fact]
    public void GeneratedHostileAirDefenseAndFightersCreateThreatSources()
    {
        var state = ConflictTheaterGenerator.Generate(
            Template(),
            theaterSeed: 0x1234UL,
            Epoch);

        foreach (var threat in state.Threats)
        {
            Assert.NotNull(threat.SourceUnitId);

            var sourceExists =
                state.Units.Any(unit => unit.UnitId == threat.SourceUnitId)
                || state.AirUnits.Any(unit => unit.UnitId == threat.SourceUnitId);

            Assert.True(sourceExists);
        }

        Assert.Contains(
            state.Threats,
            threat => threat.Type == AirThreatType.AirDefense);

        Assert.Contains(
            state.Threats,
            threat => threat.Type == AirThreatType.Interceptor);
    }

    [Fact]
    public void FrontSnapshotUsesContestedSectorsAsDerivedFront()
    {
        var state = ConflictTheaterGenerator.Generate(
            Template(),
            theaterSeed: 0xCAFEUL,
            Epoch);

        var snapshot = ConflictFrontEstimator.Create(state);

        Assert.Equal(state.TheaterId, snapshot.TheaterId);
        Assert.Equal(state.UpdatedAt, snapshot.AsOf);
        Assert.NotEmpty(snapshot.Points);

        foreach (var point in snapshot.Points)
        {
            var source = state.Sectors.Single(
                sector => sector.SectorId == point.SectorId);

            Assert.Equal(source.Center, point.Position);
            Assert.Equal(source.FriendlyControl, point.FriendlyControl);
            Assert.Equal(
                source.IntelligenceConfidence,
                point.IntelligenceConfidence);
        }

        Assert.InRange(snapshot.ContestedSectorShare, 0, 1);
    }

    [Fact]
    public void GeneratedTheaterUsesAbstractFictionalIdentityOnly()
    {
        var template = Template();

        var state = ConflictTheaterGenerator.Generate(
            template,
            theaterSeed: 0xDEADBEEFUL,
            Epoch);

        Assert.Equal("FICTIONAL-NORTH", state.TheaterId);

        Assert.All(
            state.Sectors,
            sector => Assert.StartsWith(
                "FICTIONAL-NORTH-S",
                sector.SectorId,
                StringComparison.Ordinal));
    }

    private static ConflictTheaterTemplate Template() =>
        new(
            TheaterId: "FICTIONAL-NORTH",
            Center: new GeoPoint(35.25, -97.15),
            RadiusNauticalMiles: 140,
            FriendlyGroundUnits: 8,
            HostileGroundUnits: 8,
            FriendlyAirUnits: 4,
            HostileAirUnits: 4);
}
