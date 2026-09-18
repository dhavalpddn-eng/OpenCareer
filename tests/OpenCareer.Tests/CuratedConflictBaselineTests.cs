using OpenCareer.Application.Conflict;
using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public sealed class CuratedConflictBaselineTests
{
    [Fact]
    public void BaselineRequiresSourceProvenance()
    {
        var document = CuratedConflictBaselineDocument.Parse(ValidJson());
        var profile = document.ToWorldProfile();

        var region = Assert.Single(profile.Regions);
        Assert.Equal(OpenCareer.Domain.Events.ConflictBaselineSource.CuratedRealWorld, region.BaselineSource);
        Assert.Contains("UCDP", region.SourceReference!, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationMayFollowDataThroughDate()
    {
        var document = CuratedConflictBaselineDocument.Parse(ValidJson());

        var source = Assert.Single(document.Sources);
        Assert.True(source.PublishedAt > source.DataThrough);
        document.Validate();
    }

    [Fact]
    public void UnknownSourceReferenceIsRejected()
    {
        var json = ValidJson().Replace(
            "\"sourceIds\": [\"ucdp-candidate\"]",
            "\"sourceIds\": [\"missing\"]",
            StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => CuratedConflictBaselineDocument.Parse(json));
    }

    private static string ValidJson() =>
        """
        {
          "schemaVersion": 1,
          "datasetId": "test-baseline",
          "baselineAsOf": "2026-07-31T23:59:59Z",
          "sources": [
            {
              "sourceId": "ucdp-candidate",
              "publisher": "UCDP",
              "dataset": "Candidate Events",
              "version": "26.0.7",
              "dataThrough": "2026-07-31T23:59:59Z",
              "publishedAt": "2026-08-15T00:00:00Z",
              "reference": "https://ucdp.uu.se/downloads/",
              "license": "CC BY 4.0"
            }
          ],
          "regions": [
            {
              "regionId": "region-a",
              "baselineTension": 0.4,
              "internalInstability": 0.2,
              "borderSecurityPressure": 0.3,
              "civilAviationResilience": 0.8,
              "sourceIds": ["ucdp-candidate"]
            }
          ],
          "connections": [],
          "activeCampaigns": []
        }
        """;
}
