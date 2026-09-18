using System.Text.Json;
using OpenCareer.Domain.Events;

namespace OpenCareer.Application.Conflict;

public sealed record ConflictBaselineSourceMetadata(
    string SourceId,
    string Publisher,
    string Dataset,
    string Version,
    DateTimeOffset DataThrough,
    DateTimeOffset? PublishedAt,
    string Reference,
    string License);

public sealed record ConflictBaselineRegion(
    string RegionId,
    double BaselineTension,
    double InternalInstability,
    double BorderSecurityPressure,
    double CivilAviationResilience,
    IReadOnlyList<string> SourceIds);

public sealed record ConflictBaselineConnection(
    string RegionAId,
    string RegionBId,
    double LandBorderExposure,
    double MaritimeExposure,
    double DisputeFriction,
    double AllianceStrength,
    double EconomicInterdependence,
    IReadOnlyList<string> SourceIds);

public sealed record ConflictBaselineCampaign(
    string CampaignId,
    IReadOnlyList<string> AffectedRegionIds,
    string SideAId,
    string SideBId,
    double Severity,
    IReadOnlyList<string> SourceIds);

public sealed record CuratedConflictBaselineDocument(
    int SchemaVersion,
    string DatasetId,
    DateTimeOffset BaselineAsOf,
    IReadOnlyList<ConflictBaselineSourceMetadata> Sources,
    IReadOnlyList<ConflictBaselineRegion> Regions,
    IReadOnlyList<ConflictBaselineConnection> Connections,
    IReadOnlyList<ConflictBaselineCampaign> ActiveCampaigns)
{
    public const int CurrentSchemaVersion = 1;

    public ConflictWorldProfile ToWorldProfile()
    {
        Validate();

        var sourceById = Sources.ToDictionary(source => source.SourceId, StringComparer.Ordinal);

        var regions = Regions.Select(region =>
        {
            var sourceRef = BuildSourceReference(region.SourceIds, sourceById);
            return new ConflictRegionProfile(
                region.RegionId,
                region.BaselineTension,
                region.InternalInstability,
                region.BorderSecurityPressure,
                region.CivilAviationResilience,
                OpenCareer.Domain.Events.ConflictBaselineSource.CuratedRealWorld,
                BaselineAsOf,
                sourceRef);
        }).ToArray();

        var connections = Connections.Select(connection =>
            new ConflictRegionConnection(
                connection.RegionAId,
                connection.RegionBId,
                connection.LandBorderExposure,
                connection.MaritimeExposure,
                connection.DisputeFriction,
                connection.AllianceStrength,
                connection.EconomicInterdependence)).ToArray();

        var campaigns = ActiveCampaigns.Select(campaign =>
            new CuratedConflictSeed(
                campaign.CampaignId,
                campaign.AffectedRegionIds.ToArray(),
                campaign.SideAId,
                campaign.SideBId,
                campaign.Severity,
                BaselineAsOf,
                BuildSourceReference(campaign.SourceIds, sourceById))).ToArray();

        var profile = new ConflictWorldProfile(regions, connections, campaigns);
        profile.Validate();
        return profile;
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("Unsupported curated conflict baseline schema.");

        ArgumentException.ThrowIfNullOrWhiteSpace(DatasetId);
        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Connections);
        ArgumentNullException.ThrowIfNull(ActiveCampaigns);

        if (Sources.Count == 0)
            throw new ArgumentException("At least one provenance source is required.");

        if (Sources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != Sources.Count)
            throw new ArgumentException("Conflict baseline source IDs must be unique.");

        var sourceIds = Sources.Select(source => source.SourceId).ToHashSet(StringComparer.Ordinal);
        foreach (var source in Sources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Publisher);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Dataset);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Version);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Reference);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.License);

            if (source.DataThrough > BaselineAsOf)
                throw new ArgumentException("A source cannot cover data later than the baseline as-of timestamp.");

            if (source.PublishedAt is { } publishedAt && publishedAt < source.DataThrough)
                throw new ArgumentException("A source publication timestamp cannot precede the data-through timestamp.");
        }

        foreach (var region in Regions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(region.RegionId);
            ValidateUnit(region.BaselineTension, nameof(region.BaselineTension));
            ValidateUnit(region.InternalInstability, nameof(region.InternalInstability));
            ValidateUnit(region.BorderSecurityPressure, nameof(region.BorderSecurityPressure));
            ValidateUnit(region.CivilAviationResilience, nameof(region.CivilAviationResilience));
            ValidateSourceReferences(region.SourceIds, sourceIds);
        }

        var regionIds = Regions.Select(region => region.RegionId).ToHashSet(StringComparer.Ordinal);
        if (regionIds.Count != Regions.Count)
            throw new ArgumentException("Conflict baseline region IDs must be unique.");

        foreach (var connection in Connections)
        {
            if (!regionIds.Contains(connection.RegionAId) || !regionIds.Contains(connection.RegionBId))
                throw new ArgumentException("Conflict baseline connection references an unknown region.");

            if (string.Equals(connection.RegionAId, connection.RegionBId, StringComparison.Ordinal))
                throw new ArgumentException("Conflict baseline connections require two distinct regions.");

            ValidateUnit(connection.LandBorderExposure, nameof(connection.LandBorderExposure));
            ValidateUnit(connection.MaritimeExposure, nameof(connection.MaritimeExposure));
            ValidateUnit(connection.DisputeFriction, nameof(connection.DisputeFriction));
            ValidateUnit(connection.AllianceStrength, nameof(connection.AllianceStrength));
            ValidateUnit(connection.EconomicInterdependence, nameof(connection.EconomicInterdependence));
            ValidateSourceReferences(connection.SourceIds, sourceIds);
        }

        var connectionKeys = Connections
            .Select(connection =>
                string.CompareOrdinal(connection.RegionAId, connection.RegionBId) <= 0
                    ? $"{connection.RegionAId}|{connection.RegionBId}"
                    : $"{connection.RegionBId}|{connection.RegionAId}")
            .ToArray();

        if (connectionKeys.Distinct(StringComparer.Ordinal).Count() != connectionKeys.Length)
            throw new ArgumentException("Conflict baseline connections must be unique.");

        if (ActiveCampaigns.Select(campaign => campaign.CampaignId)
            .Distinct(StringComparer.Ordinal).Count() != ActiveCampaigns.Count)
        {
            throw new ArgumentException("Conflict baseline campaign IDs must be unique.");
        }

        foreach (var campaign in ActiveCampaigns)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(campaign.CampaignId);
            ArgumentException.ThrowIfNullOrWhiteSpace(campaign.SideAId);
            ArgumentException.ThrowIfNullOrWhiteSpace(campaign.SideBId);

            if (string.Equals(campaign.SideAId, campaign.SideBId, StringComparison.Ordinal))
                throw new ArgumentException("Conflict baseline campaign sides must be distinct.");

            if (campaign.AffectedRegionIds.Count == 0
                || campaign.AffectedRegionIds.Distinct(StringComparer.Ordinal).Count() != campaign.AffectedRegionIds.Count
                || campaign.AffectedRegionIds.Any(region => !regionIds.Contains(region)))
            {
                throw new ArgumentException("Conflict baseline campaign references unknown or duplicate regions.");
            }

            if (!double.IsFinite(campaign.Severity) || campaign.Severity is <= 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(campaign.Severity));

            ValidateSourceReferences(campaign.SourceIds, sourceIds);
        }
    }

    public static CuratedConflictBaselineDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        var document = JsonSerializer.Deserialize<CuratedConflictBaselineDocument>(json, options)
            ?? throw new InvalidDataException("Curated conflict baseline JSON was empty.");

        document.Validate();
        return document;
    }

    private static void ValidateSourceReferences(
        IReadOnlyList<string> references,
        IReadOnlySet<string> knownSources)
    {
        ArgumentNullException.ThrowIfNull(references);

        if (references.Count == 0
            || references.Distinct(StringComparer.Ordinal).Count() != references.Count
            || references.Any(reference => !knownSources.Contains(reference)))
        {
            throw new ArgumentException("Every baseline item requires unique known source provenance.");
        }
    }

    private static string BuildSourceReference(
        IReadOnlyList<string> sourceIds,
        IReadOnlyDictionary<string, ConflictBaselineSourceMetadata> sourceById)
    {
        return string.Join(
            " | ",
            sourceIds.Select(sourceId =>
            {
                var source = sourceById[sourceId];
                return $"{source.Publisher}:{source.Dataset}:{source.Version}:{source.Reference}";
            }));
    }

    private static void ValidateUnit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name);
    }
}
