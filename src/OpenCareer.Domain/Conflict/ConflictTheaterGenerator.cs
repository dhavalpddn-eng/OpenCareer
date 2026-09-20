using System.Buffers.Binary;
using OpenCareer.Domain.Simulation;

namespace OpenCareer.Domain.Conflict;

public sealed record ConflictTheaterTemplate(
    string TheaterId,
    GeoPoint Center,
    double RadiusNauticalMiles,
    int FriendlyGroundUnits,
    int HostileGroundUnits,
    int FriendlyAirUnits,
    int HostileAirUnits)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TheaterId);
        ArgumentNullException.ThrowIfNull(Center);
        Center.Validate();

        if (Math.Abs(Center.LatitudeDegrees) > 75)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Center),
                "Generated theaters are limited to latitudes within 75 degrees.");
        }

        if (!double.IsFinite(RadiusNauticalMiles)
            || RadiusNauticalMiles is < 30 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(RadiusNauticalMiles));
        }

        ValidateCount(FriendlyGroundUnits, nameof(FriendlyGroundUnits));
        ValidateCount(HostileGroundUnits, nameof(HostileGroundUnits));
        ValidateCount(FriendlyAirUnits, nameof(FriendlyAirUnits));
        ValidateCount(HostileAirUnits, nameof(HostileAirUnits));
    }

    private static void ValidateCount(int count, string name)
    {
        if (count is < 1 or > 50)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record FrontLinePoint(
    string SectorId,
    GeoPoint Position,
    double FriendlyControl,
    double IntelligenceConfidence);

public sealed record ConflictFrontSnapshot(
    string TheaterId,
    DateTimeOffset AsOf,
    FrontLinePoint[] Points,
    double ContestedSectorShare);

public static class ConflictFrontEstimator
{
    public static ConflictFrontSnapshot Create(ConflictWorldState state)
    {
        ConflictValidation.Validate(state);

        var contested = state.Sectors
            .Where(sector => sector.FriendlyControl is >= 0.35 and <= 0.65)
            .OrderBy(sector => sector.Center.LongitudeDegrees)
            .ThenBy(sector => sector.Center.LatitudeDegrees)
            .Select(ToPoint)
            .ToArray();

        if (contested.Length == 0 && state.Sectors.Length > 0)
        {
            contested = state.Sectors
                .OrderBy(sector => Math.Abs(sector.FriendlyControl - 0.50))
                .ThenBy(sector => sector.SectorId, StringComparer.Ordinal)
                .Take(Math.Min(3, state.Sectors.Length))
                .OrderBy(sector => sector.Center.LongitudeDegrees)
                .ThenBy(sector => sector.Center.LatitudeDegrees)
                .Select(ToPoint)
                .ToArray();
        }

        var share = state.Sectors.Length == 0
            ? 0
            : (double)state.Sectors.Count(
                sector => sector.FriendlyControl is >= 0.35 and <= 0.65)
              / state.Sectors.Length;

        return new ConflictFrontSnapshot(
            state.TheaterId,
            state.UpdatedAt,
            contested,
            share);
    }

    private static FrontLinePoint ToPoint(ConflictSectorState sector) =>
        new(
            sector.SectorId,
            sector.Center,
            sector.FriendlyControl,
            sector.IntelligenceConfidence);
}

public static class ConflictTheaterGenerator
{
    private static readonly GroundUnitRole[] GroundRoles =
    {
        GroundUnitRole.Infantry,
        GroundUnitRole.Armor,
        GroundUnitRole.Logistics,
        GroundUnitRole.AirDefense,
        GroundUnitRole.Command
    };

    public static ConflictWorldState Generate(
        ConflictTheaterTemplate template,
        ulong theaterSeed,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Validate();

        var random = DeterministicSeed.CreateStream(
            theaterSeed,
            $"conflict-theater:{template.TheaterId}");

        var orientation = random.NextDouble(0, 360);
        var sectors = GenerateSectors(
            template,
            random,
            orientation);

        var groundUnits = new List<GroundUnitState>(
            template.FriendlyGroundUnits + template.HostileGroundUnits);

        var threats = new List<ThreatState>();

        GenerateGroundSide(
            template,
            random,
            orientation,
            ConflictSide.Friendly,
            template.FriendlyGroundUnits,
            groundUnits,
            threats);

        GenerateGroundSide(
            template,
            random,
            orientation,
            ConflictSide.Hostile,
            template.HostileGroundUnits,
            groundUnits,
            threats);

        var airUnits = new List<SimulatedAirUnitState>(
            template.FriendlyAirUnits + template.HostileAirUnits);

        GenerateAirSide(
            template,
            random,
            orientation,
            ConflictSide.Friendly,
            template.FriendlyAirUnits,
            airUnits,
            threats);

        GenerateAirSide(
            template,
            random,
            orientation,
            ConflictSide.Hostile,
            template.HostileAirUnits,
            airUnits,
            threats);

        var state = ConflictWorldState.Create(
            template.TheaterId,
            theaterSeed,
            createdAt,
            groundUnits,
            sectors,
            threats,
            airUnits);

        state = ConflictWorldEngine.RecalculatePressureAndThreats(state);
        state = SupportRequestGenerator.Refresh(state, createdAt);
        ConflictValidation.Validate(state);
        return state;
    }

    private static ConflictSectorState[] GenerateSectors(
        ConflictTheaterTemplate template,
        DeterministicRandom random,
        double orientation)
    {
        var sectors = new List<ConflictSectorState>(9);
        var spacing = template.RadiusNauticalMiles / 3.2;

        for (var north = -1; north <= 1; north++)
        {
            for (var east = -1; east <= 1; east++)
            {
                var localBearing = Math.Atan2(east, north) * 180d / Math.PI;
                if (localBearing < 0)
                    localBearing += 360;

                var bearing = NormalizeDegrees(localBearing + orientation);
                var distance = Math.Sqrt(north * north + east * east) * spacing;
                var center = distance <= 0
                    ? template.Center
                    : DestinationPoint(template.Center, bearing, distance);

                var frontBias = -east * 0.15;
                var friendlyControl = Math.Clamp(
                    0.50 + frontBias + random.NextDouble(-0.05, 0.05),
                    0.12,
                    0.88);

                sectors.Add(new ConflictSectorState(
                    $"{template.TheaterId}-S{north + 2}{east + 2}",
                    center,
                    friendlyControl,
                    random.NextDouble(0.25, 0.80)));
            }
        }

        return sectors.ToArray();
    }

    private static void GenerateGroundSide(
        ConflictTheaterTemplate template,
        DeterministicRandom random,
        double orientation,
        ConflictSide side,
        int count,
        ICollection<GroundUnitState> units,
        ICollection<ThreatState> threats)
    {
        var sideBearing = side == ConflictSide.Friendly
            ? NormalizeDegrees(orientation + 270)
            : NormalizeDegrees(orientation + 90);

        for (var index = 0; index < count; index++)
        {
            var bearing = NormalizeDegrees(
                sideBearing + random.NextDouble(-55, 55));

            var distance = random.NextDouble(
                template.RadiusNauticalMiles * 0.15,
                template.RadiusNauticalMiles * 0.85);

            var role = GroundRoles[index % GroundRoles.Length];
            var unitId = NextGuid(random);

            var unit = new GroundUnitState(
                unitId,
                side,
                role,
                DestinationPoint(template.Center, bearing, distance),
                Strength: random.NextDouble(0.55, 0.96),
                Readiness: random.NextDouble(0.55, 0.96),
                Pressure: 0,
                IsMobile: role is not (
                    GroundUnitRole.AirDefense
                    or GroundUnitRole.Command));

            units.Add(unit);

            if (role == GroundUnitRole.AirDefense)
            {
                threats.Add(new ThreatState(
                    NextGuid(random),
                    unitId,
                    side,
                    AirThreatType.AirDefense,
                    unit.Position,
                    RadiusNauticalMiles: random.NextDouble(10, 25),
                    Severity: unit.Strength * unit.Readiness,
                    Active: unit.IsOperational));
            }
        }
    }

    private static void GenerateAirSide(
        ConflictTheaterTemplate template,
        DeterministicRandom random,
        double orientation,
        ConflictSide side,
        int count,
        ICollection<SimulatedAirUnitState> airUnits,
        ICollection<ThreatState> threats)
    {
        var startBearing = side == ConflictSide.Friendly
            ? NormalizeDegrees(orientation + 250)
            : NormalizeDegrees(orientation + 70);

        for (var index = 0; index < count; index++)
        {
            var role = SelectAirRole(side, index);
            var unitId = NextGuid(random);

            var position = DestinationPoint(
                template.Center,
                NormalizeDegrees(startBearing + random.NextDouble(-70, 70)),
                random.NextDouble(
                    template.RadiusNauticalMiles * 0.30,
                    template.RadiusNauticalMiles * 0.90));

            var destination = DestinationPoint(
                template.Center,
                NormalizeDegrees(startBearing + 180 + random.NextDouble(-45, 45)),
                random.NextDouble(
                    template.RadiusNauticalMiles * 0.30,
                    template.RadiusNauticalMiles * 0.90));

            var unit = new SimulatedAirUnitState(
                unitId,
                side,
                role,
                position,
                destination,
                AltitudeFeet: random.NextDouble(12_000, 35_000),
                GroundSpeedKnots: role == AirUnitRole.Fighter
                    ? random.NextDouble(350, 520)
                    : random.NextDouble(220, 380),
                Strength: random.NextDouble(0.65, 0.96),
                Readiness: random.NextDouble(0.60, 0.96),
                Active: true);

            airUnits.Add(unit);

            if (side == ConflictSide.Hostile
                && role == AirUnitRole.Fighter)
            {
                threats.Add(new ThreatState(
                    NextGuid(random),
                    unitId,
                    side,
                    AirThreatType.Interceptor,
                    unit.Position,
                    RadiusNauticalMiles: random.NextDouble(18, 35),
                    Severity: unit.Strength * unit.Readiness,
                    Active: true));
            }
        }
    }

    private static AirUnitRole SelectAirRole(
        ConflictSide side,
        int index)
    {
        if (side == ConflictSide.Hostile)
        {
            return index % 3 == 2
                ? AirUnitRole.Surveillance
                : AirUnitRole.Fighter;
        }

        return index % 4 switch
        {
            0 => AirUnitRole.Fighter,
            1 => AirUnitRole.Transport,
            2 => AirUnitRole.Surveillance,
            _ => AirUnitRole.Tanker
        };
    }

    private static Guid NextGuid(DeterministicRandom random)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes[..8],
            random.NextUInt64());
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes[8..],
            random.NextUInt64());

        return new Guid(bytes);
    }

    private static GeoPoint DestinationPoint(
        GeoPoint start,
        double bearingDegrees,
        double distanceNauticalMiles)
    {
        const double earthRadiusNauticalMiles = 3440.065;

        var angularDistance =
            distanceNauticalMiles / earthRadiusNauticalMiles;

        var bearing = bearingDegrees * Math.PI / 180d;
        var lat1 = start.LatitudeDegrees * Math.PI / 180d;
        var lon1 = start.LongitudeDegrees * Math.PI / 180d;

        var lat2 = Math.Asin(
            Math.Sin(lat1) * Math.Cos(angularDistance)
            + Math.Cos(lat1)
            * Math.Sin(angularDistance)
            * Math.Cos(bearing));

        var lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing)
            * Math.Sin(angularDistance)
            * Math.Cos(lat1),
            Math.Cos(angularDistance)
            - Math.Sin(lat1)
            * Math.Sin(lat2));

        lon2 = (lon2 + 3 * Math.PI) % (2 * Math.PI) - Math.PI;

        return new GeoPoint(
            lat2 * 180d / Math.PI,
            lon2 * 180d / Math.PI);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0
            ? normalized + 360
            : normalized;
    }
}
