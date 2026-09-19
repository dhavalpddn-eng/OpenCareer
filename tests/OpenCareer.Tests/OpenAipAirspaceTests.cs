using System.Net;
using OpenCareer.Infrastructure.AviationData;

namespace OpenCareer.Tests;

public sealed class OpenAipAirspaceTests
{
    [Fact]
    public async Task Refresh_PaginatesAndKeepsPreviousCountryDataOnFailure()
    {
        var database = Path.Combine(Path.GetTempPath(), "opencareer-openaip-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var handler = new StubHandler();
            using var client = new HttpClient(handler);
            var service = new OpenAipAirspaceRefreshService(client);
            handler.Responses.Enqueue(Page(1, 2, Item("one", "Test Airspace One")));
            handler.Responses.Enqueue(Page(2, 2, Item("two", "Test Airspace Two")));
            Assert.Equal(2, await service.RefreshCountryAsync(database, "us", "test-key"));
            Assert.Equal(2, handler.Requests);
            var reference = new OpenAipAirspaceDatabase(database);
            var nearby = reference.FindBoundsCandidates("US", -91, 30, -88, 33);
            Assert.Equal(2, nearby.Count);
            Assert.All(nearby, airspace => Assert.True(airspace.ByNotam));
            Assert.Empty(reference.FindBoundsCandidates("US", 10, 10, 11, 11));

            handler.Responses.Enqueue(Page(1, 2, Item("replacement", "Replacement")));
            handler.Responses.Enqueue(Page(2, 2)); // The server reported two items but returned only one.
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RefreshCountryAsync(database, "US", "test-key"));
            Assert.Equal(2, reference.FindBoundsCandidates("US", -91, 30, -88, 33).Count);
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
        }
    }

    private static string Item(string id, string name) => """
        {"_id":"__ID__","name":"__NAME__","type":1,"icaoClass":8,"byNotam":true,
         "lowerLimit":{"value":0,"unit":1,"referenceDatum":0},
         "upperLimit":{"value":5000,"unit":1,"referenceDatum":1},
         "geometry":{"type":"Polygon","coordinates":[[[-90,31],[-89,31],[-89,32],[-90,31]]]}}
        """.Replace("__ID__", id).Replace("__NAME__", name);

    private static string Page(int page, int total, params string[] items) =>
        $"{{\"page\":{page},\"limit\":500,\"totalCount\":{total},\"totalPages\":2,\"items\":[{string.Join(',', items)}]}}";

    private sealed class StubHandler : HttpMessageHandler
    {
        public Queue<string> Responses { get; } = new();
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.core.openaip.net", request.RequestUri?.Host);
            Assert.Contains("country=US", request.RequestUri?.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-openaip-api-key")));
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Responses.Dequeue())
            });
        }
    }
}
